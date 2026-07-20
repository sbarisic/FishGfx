using System;
using System.Buffers;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using FishGfx.Graphics;

namespace FishGfx.Voxels;

internal readonly record struct VoxelTransparentFaceRecord(
	Vector3 WorldCenter,
	uint FirstVertex,
	int VertexCount,
	ChunkCoordinate Coordinate,
	int FaceIndex
);

internal sealed class VoxelTransparentGeometryStore : IDisposable
{
	private const int VertexAlignment = 6;
	private readonly GraphicsContext graphics;
	private readonly VoxelVertexRangeAllocator ranges;
	private VoxelTransparentBufferGeneration generation;
	private bool disposed;

	internal VoxelTransparentGeometryStore(GraphicsContext graphics, int initialSizeBytes)
	{
		this.graphics = graphics ?? throw new ArgumentNullException(nameof(graphics));

		if (initialSizeBytes <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(initialSizeBytes));
		}

		int stride = Marshal.SizeOf<VoxelVertex>();
		int capacity = Math.Max(
			VertexAlignment,
			AlignDown(initialSizeBytes / stride, VertexAlignment)
		);
		ranges = new VoxelVertexRangeAllocator(capacity, VertexAlignment);
		generation = new VoxelTransparentBufferGeneration(graphics, capacity);
	}

	internal VoxelTransparentBufferGeneration Generation => generation;

	internal int Capacity => ranges.Capacity;

	internal int AllocationCount => ranges.AllocationCount;

	internal VoxelTransparentAllocation Update(
		VoxelTransparentAllocation current,
		VoxelTransparentFace[] faces,
		ChunkCoordinate coordinate,
		Vector3 origin
	)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(faces);
		int vertexCount = CountVertices(faces);

		if (vertexCount == 0)
		{
			current?.ReleaseOwner();
			return null;
		}

		VoxelTransparentAllocation replacement = current;

		if (replacement == null
			|| replacement.IsRetained
			|| replacement.Capacity < vertexCount)
		{
			replacement = Allocate(vertexCount);
		}

		VoxelVertex[] vertices = ArrayPool<VoxelVertex>.Shared.Rent(vertexCount);
		VoxelTransparentFaceRecord[] records = new VoxelTransparentFaceRecord[faces.Length];

		try
		{
			int destination = 0;

			for (int faceIndex = 0; faceIndex < faces.Length; faceIndex++)
			{
				VoxelTransparentFace face = faces[faceIndex];
				VoxelVertex[] source = face.VertexArray;
				records[faceIndex] = new VoxelTransparentFaceRecord(
					face.Center + origin,
					checked((uint)(replacement.FirstVertex + destination)),
					source.Length,
					coordinate,
					faceIndex
				);

				for (int vertexIndex = 0; vertexIndex < source.Length; vertexIndex++)
				{
					VoxelVertex vertex = source[vertexIndex];
					vertex.Position += origin;
					vertices[destination++] = vertex;
				}
			}

			generation.Buffer.Write(
				vertices.AsSpan(0, vertexCount),
				checked(replacement.FirstVertex * Marshal.SizeOf<VoxelVertex>())
			);
			replacement.SetGeometry(vertexCount, records);
		}
		catch
		{
			if (!ReferenceEquals(replacement, current))
			{
				replacement.ReleaseOwner();
			}

			throw;
		}
		finally
		{
			ArrayPool<VoxelVertex>.Shared.Return(vertices);
		}

		if (!ReferenceEquals(replacement, current))
		{
			current?.ReleaseOwner();
		}

		return replacement;
	}

	internal VoxelTransparentAllocation Reserve(int vertexCount)
	{
		ThrowIfDisposed();
		return vertexCount == 0 ? null : Allocate(vertexCount);
	}

	internal void WriteSlice(
		VoxelTransparentAllocation allocation,
		ReadOnlySpan<VoxelVertex> vertices,
		int destinationVertexOffset)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(allocation);
		if (destinationVertexOffset < 0
			|| destinationVertexOffset + vertices.Length > allocation.Capacity)
		{
			throw new ArgumentOutOfRangeException(nameof(destinationVertexOffset));
		}
		generation.Buffer.Write(
			vertices,
			checked((allocation.FirstVertex + destinationVertexOffset)
				* Marshal.SizeOf<VoxelVertex>())
		);
	}

	internal static void Complete(
		VoxelTransparentAllocation allocation,
		int vertexCount,
		VoxelTransparentFaceRecord[] records)
	{
		if (allocation != null)
			allocation.SetGeometry(vertexCount, records);
	}

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}

		disposed = true;
		generation.ReleaseOwner();
	}

	internal void Free(VoxelTransparentAllocation allocation)
	{
		ranges.Free(allocation.FirstVertex, allocation.Capacity);
	}

	private VoxelTransparentAllocation Allocate(int vertexCount)
	{
		int capacity = AlignUp(vertexCount, VertexAlignment);

		if (!ranges.TryAllocate(capacity, out int firstVertex))
		{
			Grow(capacity);

			if (!ranges.TryAllocate(capacity, out firstVertex))
			{
				throw new InvalidOperationException("Transparent geometry growth did not produce a usable range.");
			}
		}

		return new VoxelTransparentAllocation(this, firstVertex, capacity);
	}

	private void Grow(int requiredVertices)
	{
		int currentCapacity = ranges.Capacity;
		int newCapacity = currentCapacity;

		do
		{
			newCapacity = checked(newCapacity * 2);
		}
		while (newCapacity - currentCapacity < requiredVertices);

		newCapacity = AlignUp(newCapacity, VertexAlignment);
		VoxelTransparentBufferGeneration replacement = new(graphics, newCapacity);

		try
		{
			generation.Buffer.CopyTo(
				replacement.Buffer,
				0,
				0,
				generation.Buffer.SizeInBytes
			);
		}
		catch
		{
			replacement.ReleaseOwner();
			throw;
		}

		VoxelTransparentBufferGeneration previous = generation;
		generation = replacement;
		ranges.Grow(newCapacity);
		previous.ReleaseOwner();
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(disposed, this);
	}

	private static int CountVertices(VoxelTransparentFace[] faces)
	{
		int count = 0;

		for (int index = 0; index < faces.Length; index++)
		{
			count = checked(count + faces[index].VertexArray.Length);
		}

		return count;
	}

	private static int AlignUp(int value, int alignment)
	{
		return checked((value + alignment - 1) / alignment * alignment);
	}

	private static int AlignDown(int value, int alignment)
	{
		return value / alignment * alignment;
	}
}

internal sealed class VoxelTransparentBufferGeneration
{
	private int ownerReleased;
	private int retainedCount;
	private int disposed;

	internal VoxelTransparentBufferGeneration(GraphicsContext graphics, int vertexCapacity)
	{
		ArgumentNullException.ThrowIfNull(graphics);
		VertexCapacity = vertexCapacity;
		Buffer = graphics.CreateBuffer(new GraphicsBufferDescriptor(
			checked(vertexCapacity * Marshal.SizeOf<VoxelVertex>()),
			BufferBindFlags.Vertex
				| BufferBindFlags.TransferSource
				| BufferBindFlags.TransferDestination,
			BufferUsage.Dynamic
		));
	}

	internal GraphicsBuffer Buffer { get; }

	internal int VertexCapacity { get; }

	internal void Retain()
	{
		if (Volatile.Read(ref ownerReleased) != 0)
		{
			throw new ObjectDisposedException(nameof(VoxelTransparentBufferGeneration));
		}

		Interlocked.Increment(ref retainedCount);
	}

	internal void ReleaseRetained()
	{
		int remaining = Interlocked.Decrement(ref retainedCount);

		if (remaining < 0)
		{
			Interlocked.Increment(ref retainedCount);
			throw new InvalidOperationException("The transparent buffer generation has no retained reference to release.");
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

		if (Interlocked.Exchange(ref disposed, 1) == 0)
		{
			Buffer.Dispose();
		}
	}
}

internal sealed class VoxelTransparentAllocation
{
	private readonly VoxelTransparentGeometryStore store;
	private int ownerReleased;
	private int retainedCount;
	private int freed;

	internal VoxelTransparentAllocation(
		VoxelTransparentGeometryStore store,
		int firstVertex,
		int capacity
	)
	{
		this.store = store ?? throw new ArgumentNullException(nameof(store));
		FirstVertex = firstVertex;
		Capacity = capacity;
		FaceRecords = Array.Empty<VoxelTransparentFaceRecord>();
	}

	internal int FirstVertex { get; }

	internal int Capacity { get; }

	internal int VertexCount { get; private set; }

	internal VoxelTransparentFaceRecord[] FaceRecords { get; private set; }

	internal bool IsRetained => Volatile.Read(ref retainedCount) > 0;

	internal void SetGeometry(
		int vertexCount,
		VoxelTransparentFaceRecord[] faceRecords
	)
	{
		VertexCount = vertexCount;
		FaceRecords = faceRecords ?? throw new ArgumentNullException(nameof(faceRecords));
	}

	internal void Retain()
	{
		if (Volatile.Read(ref ownerReleased) != 0)
		{
			throw new ObjectDisposedException(nameof(VoxelTransparentAllocation));
		}

		Interlocked.Increment(ref retainedCount);
	}

	internal void ReleaseRetained()
	{
		int remaining = Interlocked.Decrement(ref retainedCount);

		if (remaining < 0)
		{
			Interlocked.Increment(ref retainedCount);
			throw new InvalidOperationException("The transparent allocation has no retained reference to release.");
		}

		TryFree();
	}

	internal void ReleaseOwner()
	{
		if (Interlocked.Exchange(ref ownerReleased, 1) == 0)
		{
			TryFree();
		}
	}

	private void TryFree()
	{
		if (Volatile.Read(ref ownerReleased) == 0 || Volatile.Read(ref retainedCount) != 0)
		{
			return;
		}

		if (Interlocked.Exchange(ref freed, 1) == 0)
		{
			store.Free(this);
		}
	}
}

internal sealed class VoxelVertexRangeAllocator
{
	private readonly int alignment;
	private readonly List<Range> freeRanges = new();
	private readonly object sync = new();

	internal VoxelVertexRangeAllocator(int capacity, int alignment)
	{
		if (capacity <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(capacity));
		}

		if (alignment <= 0 || capacity % alignment != 0)
		{
			throw new ArgumentOutOfRangeException(nameof(alignment));
		}

		this.alignment = alignment;
		Capacity = capacity;
		freeRanges.Add(new Range(0, capacity));
	}

	internal int Capacity { get; private set; }

	internal int AllocationCount { get; private set; }

	internal bool TryAllocate(int count, out int first)
	{
		if (count <= 0 || count % alignment != 0)
		{
			throw new ArgumentOutOfRangeException(nameof(count));
		}

		lock (sync)
		{
			int bestIndex = -1;
			int bestCount = int.MaxValue;

			for (int index = 0; index < freeRanges.Count; index++)
			{
				Range range = freeRanges[index];

				if (range.Count >= count && range.Count < bestCount)
				{
					bestIndex = index;
					bestCount = range.Count;
				}
			}

			if (bestIndex < 0)
			{
				first = 0;
				return false;
			}

			Range selected = freeRanges[bestIndex];
			first = selected.First;
			freeRanges.RemoveAt(bestIndex);

			if (selected.Count > count)
			{
				freeRanges.Insert(bestIndex, new Range(selected.First + count, selected.Count - count));
			}

			AllocationCount++;
			return true;
		}
	}

	internal void Free(int first, int count)
	{
		lock (sync)
		{
			freeRanges.Add(new Range(first, count));
			freeRanges.Sort(static (left, right) => left.First.CompareTo(right.First));
			Coalesce();
			AllocationCount--;
		}
	}

	internal void Grow(int newCapacity)
	{
		lock (sync)
		{
			if (newCapacity <= Capacity || newCapacity % alignment != 0)
			{
				throw new ArgumentOutOfRangeException(nameof(newCapacity));
			}

			freeRanges.Add(new Range(Capacity, newCapacity - Capacity));
			Capacity = newCapacity;
			freeRanges.Sort(static (left, right) => left.First.CompareTo(right.First));
			Coalesce();
		}
	}

	private void Coalesce()
	{
		int write = 0;

		for (int read = 0; read < freeRanges.Count; read++)
		{
			Range range = freeRanges[read];

			if (write > 0)
			{
				Range previous = freeRanges[write - 1];

				if (previous.First + previous.Count == range.First)
				{
					freeRanges[write - 1] = new Range(previous.First, previous.Count + range.Count);
					continue;
				}
			}

			freeRanges[write++] = range;
		}

		if (write < freeRanges.Count)
		{
			freeRanges.RemoveRange(write, freeRanges.Count - write);
		}
	}

	private readonly record struct Range(int First, int Count);
}
