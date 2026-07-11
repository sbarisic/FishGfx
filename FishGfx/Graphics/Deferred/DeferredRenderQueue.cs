using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;

namespace FishGfx.Graphics
{
	/// <summary>
	/// Collects immutable command submissions for later inspection, sorting, and execution.
	/// </summary>
	public sealed class DeferredRenderQueue
	{
		private readonly Dictionary<RenderBucket, List<RenderSubmission>> submissions =
			new Dictionary<RenderBucket, List<RenderSubmission>>();
		private readonly Dictionary<RenderBucket, ReadOnlyCollection<RenderSubmission>> readOnlySubmissions =
			new Dictionary<RenderBucket, ReadOnlyCollection<RenderSubmission>>();
		private readonly List<RenderBucket> buckets = new List<RenderBucket>();
		private readonly ReadOnlyCollection<RenderBucket> readOnlyBuckets;
		private long nextSequence;

		public DeferredRenderQueue()
		{
			readOnlyBuckets = buckets.AsReadOnly();
		}

		public int Count { get; private set; }
		public bool IsExecuting { get; private set; }
		public IReadOnlyList<RenderBucket> Buckets => readOnlyBuckets;

		public RenderSubmission Submit(
			RenderBucket bucket,
			CommandList commands,
			Matrix4x4 model,
			Vector3? sortPosition = null,
			int layer = 0,
			ulong sortKey = 0,
			object tag = null
		)
		{
			if (commands == null)
				throw new ArgumentNullException(nameof(commands));

			return Submit(bucket, commands.Snapshot(), model, sortPosition, layer, sortKey, tag);
		}

		public RenderSubmission Submit(
			RenderBucket bucket,
			GraphicsCommandBatch batch,
			Matrix4x4 model,
			Vector3? sortPosition = null,
			int layer = 0,
			ulong sortKey = 0,
			object tag = null
		)
		{
			EnsureMutable();

			if (batch == null)
				throw new ArgumentNullException(nameof(batch));
			if (batch.Count == 0)
				throw new ArgumentException("Deferred submissions cannot contain an empty command batch.", nameof(batch));
			if (string.IsNullOrWhiteSpace(bucket.Name))
				throw new ArgumentException("A valid render bucket is required.", nameof(bucket));
			if (!IsFinite(model))
				throw new ArgumentOutOfRangeException(nameof(model), "Model matrices must contain only finite values.");

			Vector3 resolvedPosition = sortPosition ?? model.Translation;

			if (!IsFinite(resolvedPosition))
				throw new ArgumentOutOfRangeException(
					nameof(sortPosition),
					"Sort positions must contain only finite values."
				);

			RenderSubmission submission = new RenderSubmission(
				batch,
				bucket,
				model,
				resolvedPosition,
				layer,
				sortKey,
				tag,
				nextSequence++
			);

			GetOrCreateBucket(bucket).Add(submission);
			Count++;

			return submission;
		}

		public RenderSubmission SubmitOpaque(
			CommandList commands,
			Matrix4x4 model,
			Vector3? sortPosition = null,
			int layer = 0,
			ulong sortKey = 0,
			object tag = null
		)
		{
			return Submit(RenderBucket.Opaque, commands, model, sortPosition, layer, sortKey, tag);
		}

		public RenderSubmission SubmitOpaque(
			GraphicsCommandBatch batch,
			Matrix4x4 model,
			Vector3? sortPosition = null,
			int layer = 0,
			ulong sortKey = 0,
			object tag = null
		)
		{
			return Submit(RenderBucket.Opaque, batch, model, sortPosition, layer, sortKey, tag);
		}

		public RenderSubmission SubmitTransparent(
			CommandList commands,
			Matrix4x4 model,
			Vector3? sortPosition = null,
			int layer = 0,
			ulong sortKey = 0,
			object tag = null
		)
		{
			return Submit(RenderBucket.Transparent, commands, model, sortPosition, layer, sortKey, tag);
		}

		public RenderSubmission SubmitTransparent(
			GraphicsCommandBatch batch,
			Matrix4x4 model,
			Vector3? sortPosition = null,
			int layer = 0,
			ulong sortKey = 0,
			object tag = null
		)
		{
			return Submit(RenderBucket.Transparent, batch, model, sortPosition, layer, sortKey, tag);
		}

		public IReadOnlyList<RenderSubmission> Query(RenderBucket bucket)
		{
			return readOnlySubmissions.TryGetValue(bucket, out ReadOnlyCollection<RenderSubmission> result)
				? result
				: Array.Empty<RenderSubmission>();
		}

		public IReadOnlyList<RenderSubmission> GetSorted(
			RenderBucket bucket,
			IComparer<RenderSubmission> comparer
		)
		{
			if (comparer == null)
				throw new ArgumentNullException(nameof(comparer));

			RenderSubmission[] sorted = Query(bucket).ToArray();
			Array.Sort(sorted, comparer);

			return Array.AsReadOnly(sorted);
		}

		public void Execute(RenderBucket bucket, IComparer<RenderSubmission> comparer = null)
		{
			if (IsExecuting)
				throw new InvalidOperationException("A deferred render queue cannot execute recursively.");

			IsExecuting = true;

			try
			{
				RenderSubmission[] executionOrder = Query(bucket).ToArray();

				if (comparer != null)
					Array.Sort(executionOrder, comparer);

				foreach (RenderSubmission submission in executionOrder)
					submission.Execute();
			}
			finally
			{
				IsExecuting = false;
			}
		}

		public void BeginFrame() => Clear();

		public void Clear()
		{
			EnsureMutable();

			submissions.Clear();
			readOnlySubmissions.Clear();
			buckets.Clear();
			Count = 0;
			nextSequence = 0;
		}

		private List<RenderSubmission> GetOrCreateBucket(RenderBucket bucket)
		{
			if (submissions.TryGetValue(bucket, out List<RenderSubmission> existing))
				return existing;

			List<RenderSubmission> created = new List<RenderSubmission>();
			submissions.Add(bucket, created);
			readOnlySubmissions.Add(bucket, created.AsReadOnly());
			buckets.Add(bucket);

			return created;
		}

		private void EnsureMutable()
		{
			if (IsExecuting)
				throw new InvalidOperationException("A deferred render queue cannot be modified while it is executing.");
		}

		private static bool IsFinite(Vector3 value)
		{
			return float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
		}

		private static bool IsFinite(Matrix4x4 value)
		{
			return
				float.IsFinite(value.M11)
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
}
