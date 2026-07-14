using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;

namespace FishGfx.Graphics;

/// <summary>
/// Collects immutable render items for later inspection, sorting, and pass-side execution.
/// </summary>
public sealed class RenderQueue
{
	private readonly Dictionary<RenderQueueBucket, List<RenderItem>> items = new();
	private readonly Dictionary<RenderQueueBucket, ReadOnlyCollection<RenderItem>> readOnlyItems = new();
	private readonly Dictionary<RenderItem, IDisposable> retainedResources = new();
	private readonly List<RenderQueueBucket> buckets = new();
	private readonly ReadOnlyCollection<RenderQueueBucket> readOnlyBuckets;
	private long nextSequence;
	private bool isExecuting;

	public RenderQueue()
	{
		readOnlyBuckets = buckets.AsReadOnly();
	}

	public int Count { get; private set; }

	public bool IsExecuting => isExecuting;

	public IReadOnlyList<RenderQueueBucket> Buckets => readOnlyBuckets;

	public RenderItem Submit(
		RenderQueueBucket bucket,
		RenderCommandList commands,
		Matrix4x4 model,
		Vector3? sortPosition = null,
		int layer = 0,
		ulong sortKey = 0,
		object tag = null
	)
	{
		ArgumentNullException.ThrowIfNull(commands);

		return Submit(bucket, commands.Snapshot(), model, sortPosition, layer, sortKey, tag);
	}

	public RenderItem Submit(
		RenderQueueBucket bucket,
		RenderCommandBatch batch,
		Matrix4x4 model,
		Vector3? sortPosition = null,
		int layer = 0,
		ulong sortKey = 0,
		object tag = null
	)
	{
		EnsureMutable();
		ArgumentNullException.ThrowIfNull(batch);

		if (batch.Count == 0)
		{
			throw new ArgumentException(
				"Render items cannot contain an empty command batch.",
				nameof(batch)
			);
		}

		if (string.IsNullOrWhiteSpace(bucket.Name))
		{
			throw new ArgumentException("A valid render queue bucket is required.", nameof(bucket));
		}

		if (!IsFinite(model))
		{
			throw new ArgumentOutOfRangeException(
				nameof(model),
				"Model matrices must contain only finite values."
			);
		}

		Vector3 resolvedPosition = sortPosition ?? model.Translation;

		if (!IsFinite(resolvedPosition))
		{
			throw new ArgumentOutOfRangeException(
				nameof(sortPosition),
				"Sort positions must contain only finite values."
			);
		}

		RenderItem item = new(
			batch,
			bucket,
			model,
			resolvedPosition,
			layer,
			sortKey,
			tag,
			nextSequence
		);

		nextSequence++;
		GetOrCreateBucket(bucket).Add(item);
		Count++;

		return item;
	}

	public RenderItem SubmitOpaque(
		RenderCommandList commands,
		Matrix4x4 model,
		Vector3? sortPosition = null,
		int layer = 0,
		ulong sortKey = 0,
		object tag = null
	)
	{
		return Submit(
			RenderQueueBucket.Opaque,
			commands,
			model,
			sortPosition,
			layer,
			sortKey,
			tag
		);
	}

	public RenderItem SubmitOpaque(
		RenderCommandBatch batch,
		Matrix4x4 model,
		Vector3? sortPosition = null,
		int layer = 0,
		ulong sortKey = 0,
		object tag = null
	)
	{
		return Submit(
			RenderQueueBucket.Opaque,
			batch,
			model,
			sortPosition,
			layer,
			sortKey,
			tag
		);
	}

	public RenderItem SubmitTransparent(
		RenderCommandList commands,
		Matrix4x4 model,
		Vector3? sortPosition = null,
		int layer = 0,
		ulong sortKey = 0,
		object tag = null
	)
	{
		return Submit(
			RenderQueueBucket.Transparent,
			commands,
			model,
			sortPosition,
			layer,
			sortKey,
			tag
		);
	}

	internal RenderItem SubmitRetained(
		RenderQueueBucket bucket,
		RenderCommandBatch batch,
		Matrix4x4 model,
		IDisposable retainedResource,
		Vector3? sortPosition = null,
		int layer = 0,
		ulong sortKey = 0,
		object tag = null
	)
	{
		ArgumentNullException.ThrowIfNull(retainedResource);
		RenderItem item = Submit(
			bucket,
			batch,
			model,
			sortPosition,
			layer,
			sortKey,
			tag
		);

		retainedResources.Add(item, retainedResource);

		return item;
	}

	public RenderItem SubmitTransparent(
		RenderCommandBatch batch,
		Matrix4x4 model,
		Vector3? sortPosition = null,
		int layer = 0,
		ulong sortKey = 0,
		object tag = null
	)
	{
		return Submit(
			RenderQueueBucket.Transparent,
			batch,
			model,
			sortPosition,
			layer,
			sortKey,
			tag
		);
	}

	public IReadOnlyList<RenderItem> Query(RenderQueueBucket bucket)
	{
		if (readOnlyItems.TryGetValue(bucket, out ReadOnlyCollection<RenderItem> result))
		{
			return result;
		}

		return Array.Empty<RenderItem>();
	}

	public IReadOnlyList<RenderItem> GetSorted(
		RenderQueueBucket bucket,
		IComparer<RenderItem> comparer
	)
	{
		ArgumentNullException.ThrowIfNull(comparer);

		return Array.AsReadOnly(CreateExecutionOrder(bucket, comparer));
	}

	public void BeginFrame()
	{
		Clear();
	}

	/// <summary>
	/// Removes all items and releases resources retained for their deferred execution.
	/// </summary>
	/// <remarks>
	/// Items returned by this queue must not be replayed after the queue is cleared.
	/// </remarks>
	public void Clear()
	{
		EnsureMutable();
		List<Exception> disposalFailures = null;

		foreach (IDisposable resource in retainedResources.Values)
		{
			try
			{
				resource.Dispose();
			}
			catch (Exception exception)
			{
				disposalFailures ??= new List<Exception>();
				disposalFailures.Add(exception);
			}
		}

		retainedResources.Clear();
		items.Clear();
		readOnlyItems.Clear();
		buckets.Clear();
		Count = 0;
		nextSequence = 0;

		if (disposalFailures != null)
		{
			throw new AggregateException(
				"One or more retained render resources could not be released.",
				disposalFailures
			);
		}
	}

	internal void BeginExecution()
	{
		RenderCommandReplay.Begin("render queue", ref isExecuting);
	}

	internal void EndExecution()
	{
		RenderCommandReplay.End(ref isExecuting);
	}

	internal RenderItem[] CreateExecutionOrder(
		RenderQueueBucket bucket,
		IComparer<RenderItem> comparer
	)
	{
		RenderItem[] order = Query(bucket).ToArray();

		if (comparer != null)
		{
			Array.Sort(order, comparer);
		}

		return order;
	}

	private List<RenderItem> GetOrCreateBucket(RenderQueueBucket bucket)
	{
		if (items.TryGetValue(bucket, out List<RenderItem> existing))
		{
			return existing;
		}

		List<RenderItem> created = new();
		items.Add(bucket, created);
		readOnlyItems.Add(bucket, created.AsReadOnly());
		buckets.Add(bucket);

		return created;
	}

	private void EnsureMutable()
	{
		if (isExecuting)
		{
			throw new InvalidOperationException(
				"A render queue cannot be modified while it is executing."
			);
		}
	}

	private static bool IsFinite(Vector3 value)
	{
		return float.IsFinite(value.X)
			&& float.IsFinite(value.Y)
			&& float.IsFinite(value.Z);
	}

	private static bool IsFinite(Matrix4x4 value)
	{
		return float.IsFinite(value.M11)
			&& float.IsFinite(value.M12)
			&& float.IsFinite(value.M13)
			&& float.IsFinite(value.M14)
			&& float.IsFinite(value.M21)
			&& float.IsFinite(value.M22)
			&& float.IsFinite(value.M23)
			&& float.IsFinite(value.M24)
			&& float.IsFinite(value.M31)
			&& float.IsFinite(value.M32)
			&& float.IsFinite(value.M33)
			&& float.IsFinite(value.M34)
			&& float.IsFinite(value.M41)
			&& float.IsFinite(value.M42)
			&& float.IsFinite(value.M43)
			&& float.IsFinite(value.M44);
	}
}
