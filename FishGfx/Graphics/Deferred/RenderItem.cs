using System.Numerics;

namespace FishGfx.Graphics;

/// <summary>
/// An immutable render batch with a captured transform and sorting metadata.
/// </summary>
/// <remarks>
/// A render item is valid until the queue that created it is cleared.
/// </remarks>
public sealed class RenderItem
{
	internal RenderItem(
		RenderCommandBatch batch,
		RenderQueueBucket bucket,
		Matrix4x4 model,
		Vector3 sortPosition,
		int layer,
		ulong sortKey,
		object tag,
		long sequence
	)
	{
		Batch = batch;
		Bucket = bucket;
		Model = model;
		SortPosition = sortPosition;
		Layer = layer;
		SortKey = sortKey;
		Tag = tag;
		Sequence = sequence;
	}

	public RenderCommandBatch Batch { get; }

	public RenderQueueBucket Bucket { get; }

	public Matrix4x4 Model { get; }

	public Vector3 SortPosition { get; }

	public int Layer { get; }

	public ulong SortKey { get; }

	public object Tag { get; }

	public long Sequence { get; }
}
