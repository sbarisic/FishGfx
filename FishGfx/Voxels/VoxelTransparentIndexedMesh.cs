using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using FishGfx.Graphics;

namespace FishGfx.Voxels;

internal sealed class VoxelTransparentIndexRing : IDisposable
{
	private const int PreferredSlotCount = 3;
	private readonly GraphicsContext graphics;
	private readonly List<VoxelTransparentIndexSlot> slots = new();
	private bool disposed;

	internal VoxelTransparentIndexRing(GraphicsContext graphics)
	{
		this.graphics = graphics ?? throw new ArgumentNullException(nameof(graphics));
	}

	internal VoxelTransparentDrawSnapshot Upload(
		VoxelTransparentBufferGeneration generation,
		VoxelTransparentOrderingResult result
	)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(generation);
		ArgumentNullException.ThrowIfNull(result);
		CleanupOldSlots(generation);
		VoxelTransparentIndexSlot slot = null;
		int matchingSlots = 0;

		for (int index = 0; index < slots.Count; index++)
		{
			VoxelTransparentIndexSlot candidate = slots[index];

			if (!ReferenceEquals(candidate.Generation, generation))
			{
				continue;
			}

			matchingSlots++;

			if (candidate.TryAcquire())
			{
				slot = candidate;
				break;
			}
		}

		if (slot == null)
		{
			if (matchingSlots >= PreferredSlotCount)
			{
				throw new InvalidOperationException(
					"All transparent index-buffer slots are retained by deferred frames."
				);
			}

			slot = new VoxelTransparentIndexSlot(graphics, generation);
			slots.Add(slot);

			if (!slot.TryAcquire())
			{
				throw new InvalidOperationException("A new transparent index slot could not be acquired.");
			}
		}

		try
		{
			slot.Upload(result.Indices);
			return new VoxelTransparentDrawSnapshot(slot, result);
		}
		catch
		{
			slot.ReleaseRetained();
			throw;
		}
	}

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}

		disposed = true;

		foreach (VoxelTransparentIndexSlot slot in slots)
		{
			slot.ReleaseOwner();
		}

		slots.Clear();
	}

	private void CleanupOldSlots(VoxelTransparentBufferGeneration current)
	{
		for (int index = slots.Count - 1; index >= 0; index--)
		{
			VoxelTransparentIndexSlot slot = slots[index];

			if (ReferenceEquals(slot.Generation, current) || !slot.IsAvailable)
			{
				continue;
			}

			slots.RemoveAt(index);
			slot.ReleaseOwner();
		}
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(disposed, this);
	}
}

internal sealed class VoxelTransparentIndexSlot
{
	private readonly GraphicsBuffer indexBuffer;
	private readonly VertexArray vertexArray;
	private int ownerReleased;
	private int retainedCount;
	private int disposed;

	internal VoxelTransparentIndexSlot(
		GraphicsContext graphics,
		VoxelTransparentBufferGeneration generation
	)
	{
		ArgumentNullException.ThrowIfNull(graphics);
		Generation = generation ?? throw new ArgumentNullException(nameof(generation));
		Generation.Retain();

		try
		{
			indexBuffer = graphics.CreateBuffer(new GraphicsBufferDescriptor(
				256,
				BufferBindFlags.Index,
				BufferUsage.Stream
			));
			vertexArray = graphics.CreateVertexArray();
			vertexArray.PrimitiveType = PrimitiveType.Triangles;
			ConfigureVertexArray(vertexArray, Generation.Buffer, indexBuffer);
		}
		catch
		{
			vertexArray?.Dispose();
			indexBuffer?.Dispose();
			Generation.ReleaseRetained();
			throw;
		}
	}

	internal VoxelTransparentBufferGeneration Generation { get; }

	internal bool IsAvailable => Volatile.Read(ref retainedCount) == 0
		&& Volatile.Read(ref ownerReleased) == 0;

	internal bool TryAcquire()
	{
		if (Volatile.Read(ref ownerReleased) != 0
			|| Interlocked.CompareExchange(ref retainedCount, 1, 0) != 0)
		{
			return false;
		}

		if (Volatile.Read(ref ownerReleased) == 0)
		{
			return true;
		}

		ReleaseRetained();
		return false;
	}

	internal void Upload(ReadOnlySpan<uint> indices)
	{
		int requiredBytes = checked(indices.Length * sizeof(uint));

		if (requiredBytes > indexBuffer.SizeInBytes)
		{
			int capacity = indexBuffer.SizeInBytes;

			while (capacity < requiredBytes)
			{
				capacity = checked(capacity * 2);
			}

			indexBuffer.ResizeDiscard(capacity);
		}
		else
		{
			indexBuffer.DiscardContents();
		}

		if (indices.Length > 0)
		{
			indexBuffer.Write(indices);
		}
	}

	internal void Draw(int indexCount)
	{
		if (Volatile.Read(ref disposed) != 0)
		{
			throw new ObjectDisposedException(nameof(VoxelTransparentIndexSlot));
		}

		vertexArray.DrawElements(
			count: indexCount,
			elementType: IndexElementType.UnsignedInt
		);
	}

	internal void ReleaseRetained()
	{
		int remaining = Interlocked.Decrement(ref retainedCount);

		if (remaining < 0)
		{
			Interlocked.Increment(ref retainedCount);
			throw new InvalidOperationException("The transparent index slot has no retained reference to release.");
		}

		TryDispose();
	}

	internal void ReleaseOwner()
	{
		if (Interlocked.Exchange(ref ownerReleased, 1) == 0)
		{
			TryDispose();
		}
	}

	private void TryDispose()
	{
		if (Volatile.Read(ref ownerReleased) == 0 || Volatile.Read(ref retainedCount) != 0)
		{
			return;
		}

		if (Interlocked.Exchange(ref disposed, 1) != 0)
		{
			return;
		}

		vertexArray.Dispose();
		indexBuffer.Dispose();
		Generation.ReleaseRetained();
	}

	private static void ConfigureVertexArray(
		VertexArray array,
		GraphicsBuffer vertices,
		GraphicsBuffer indices
	)
	{
		int stride = Marshal.SizeOf<VoxelVertex>();
		uint binding = array.BindVertexBuffer(vertices, 0, 0, stride);
		array.AttribFormat(0, 3, VertexElementType.Float, false, 0);
		array.AttribBinding(0, binding);
		array.AttribFormat(1, 4, VertexElementType.UnsignedByte, true, 12);
		array.AttribBinding(1, binding);
		array.AttribFormat(2, 2, VertexElementType.Float, false, 16);
		array.AttribBinding(2, binding);
		array.AttribFormat(3, 3, VertexElementType.Float, false, 24);
		array.AttribBinding(3, binding);
		array.AttribFormat(4, 4, VertexElementType.Float, false, 36);
		array.AttribBinding(4, binding);
		array.AttribFormat(5, 4, VertexElementType.Float, false, 52);
		array.AttribBinding(5, binding);
		array.AttribFormat(6, 4, VertexElementType.UnsignedByte, true, 68);
		array.AttribBinding(6, binding);
		array.AttribIFormat(8, 1, VertexElementType.Int, 72);
		array.AttribBinding(8, binding);
		array.BindElementBuffer(indices);
	}
}

internal sealed class VoxelTransparentDrawSnapshot : IDisposable
{
	private readonly VoxelTransparentIndexSlot slot;
	private readonly VoxelTransparentOrderingSource source;
	private int ownerReleased;
	private int retainedCount;
	private int released;

	internal VoxelTransparentDrawSnapshot(
		VoxelTransparentIndexSlot slot,
		VoxelTransparentOrderingResult result
	)
	{
		this.slot = slot ?? throw new ArgumentNullException(nameof(slot));
		ArgumentNullException.ThrowIfNull(result);
		source = result.Source;
		source.Retain();
		IndexCount = result.IndexCount;
		FaceCount = result.FaceCount;
		VisibleChunkCount = result.VisibleChunkCount;
		CameraPosition = result.CameraPosition;
		CameraForward = result.CameraForward;
		GeometryRevision = source.GeometryRevision;
		ActiveSetGeneration = source.ActiveSetGeneration;
		Reason = result.Reason;
		RequestSequence = result.RequestSequence;
		CompletedTimestamp = result.CompletedTimestamp;
	}

	internal int IndexCount { get; }

	internal int FaceCount { get; }

	internal int VisibleChunkCount { get; }

	internal Vector3 CameraPosition { get; }

	internal Vector3 CameraForward { get; }

	internal long GeometryRevision { get; }

	internal long ActiveSetGeneration { get; }

	internal VoxelTransparentInvalidationReason Reason { get; }

	internal long RequestSequence { get; }

	internal long CompletedTimestamp { get; }

	internal void RetainReference()
	{
		if (Volatile.Read(ref ownerReleased) != 0)
		{
			throw new ObjectDisposedException(nameof(VoxelTransparentDrawSnapshot));
		}

		Interlocked.Increment(ref retainedCount);
	}

	internal void DrawRetained()
	{
		if (Volatile.Read(ref released) != 0)
		{
			throw new ObjectDisposedException(nameof(VoxelTransparentDrawSnapshot));
		}

		slot.Draw(IndexCount);
	}

	internal void ReleaseReference()
	{
		int remaining = Interlocked.Decrement(ref retainedCount);

		if (remaining < 0)
		{
			Interlocked.Increment(ref retainedCount);
			throw new InvalidOperationException("The transparent draw snapshot has no retained reference to release.");
		}

		TryRelease();
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref ownerReleased, 1) == 0)
		{
			TryRelease();
		}
	}

	private void TryRelease()
	{
		if (Volatile.Read(ref ownerReleased) == 0 || Volatile.Read(ref retainedCount) != 0)
		{
			return;
		}

		if (Interlocked.Exchange(ref released, 1) != 0)
		{
			return;
		}

		source.ReleaseRetained();
		slot.ReleaseRetained();
	}
}
