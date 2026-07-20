using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using FishGfx.Graphics;

namespace FishGfx.Voxels;

internal sealed class VoxelGeometryPagePool : IDisposable
{
	private const int VertexAlignment = 6;
	private readonly GraphicsContext graphics;
	private readonly int pageVertexCapacity;
	private readonly List<VoxelGeometryPage> pages = new();
	private bool disposed;

	internal VoxelGeometryPagePool(GraphicsContext graphics, int pageSizeBytes)
	{
		this.graphics = graphics ?? throw new ArgumentNullException(nameof(graphics));

		if (pageSizeBytes <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(pageSizeBytes));
		}

		int vertexStride = Marshal.SizeOf<VoxelVertex>();
		pageVertexCapacity = Math.Max(
			VertexAlignment,
			AlignDown(pageSizeBytes / vertexStride, VertexAlignment)
		);
	}

	internal int PageCount => pages.Count;

	internal int AllocationCount
	{
		get
		{
			int count = 0;

			foreach (VoxelGeometryPage page in pages)
			{
				count += page.AllocationCount;
			}

			return count;
		}
	}

	internal VoxelGeometryAllocation Update(
		VoxelGeometryAllocation current,
		ReadOnlySpan<VoxelVertex> vertices,
		Vector3 origin
	)
	{
		ThrowIfDisposed();

		if (vertices.Length == 0)
		{
			current?.ReleaseOwner();
			return null;
		}

		if (current != null
			&& !current.IsRetained
			&& current.Capacity >= vertices.Length)
		{
			current.Page.Write(current, vertices, origin);
			return current;
		}

		VoxelGeometryAllocation replacement = Allocate(vertices.Length);

		try
		{
			replacement.Page.Write(replacement, vertices, origin);
		}
		catch
		{
			replacement.ReleaseOwner();
			throw;
		}

		current?.ReleaseOwner();
		return replacement;
	}

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}

		disposed = true;

		foreach (VoxelGeometryPage page in pages)
		{
			page.Dispose();
		}

		pages.Clear();
	}

	private VoxelGeometryAllocation Allocate(int vertexCount)
	{
		int capacity = AlignUp(vertexCount, VertexAlignment);
		VoxelGeometryPage bestPage = null;
		int bestRange = int.MaxValue;

		foreach (VoxelGeometryPage page in pages)
		{
			int range = page.FindBestRange(capacity);

			if (range >= capacity && range < bestRange)
			{
				bestPage = page;
				bestRange = range;
			}
		}

		if (bestPage == null)
		{
			int pageCapacity = Math.Max(pageVertexCapacity, capacity);
			bestPage = new VoxelGeometryPage(graphics, pages.Count, pageCapacity);
			pages.Add(bestPage);
		}

		return bestPage.Allocate(capacity, vertexCount);
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(disposed, this);
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

internal sealed class VoxelGeometryPage : IDisposable
{
	private const int MinimumVerticesPerOrigin = 6;
	private readonly object sync = new();
	private readonly GraphicsBuffer vertexBuffer;
	private readonly GraphicsBuffer originBuffer;
	private readonly VertexArray vertexArray;
	private readonly List<VoxelGeometryRange> freeRanges = new();
	private readonly Stack<int> freeOriginIndices = new();
	private int nextOriginIndex;
	private bool disposed;

	internal VoxelGeometryPage(GraphicsContext graphics, int pageIndex, int vertexCapacity)
	{
		ArgumentNullException.ThrowIfNull(graphics);

		if (vertexCapacity <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(vertexCapacity));
		}

		PageIndex = pageIndex;
		VertexCapacity = vertexCapacity;
		int vertexStride = Marshal.SizeOf<VoxelVertex>();
		int originCapacity = Math.Max(1, vertexCapacity / MinimumVerticesPerOrigin);
		vertexBuffer = graphics.CreateBuffer(new GraphicsBufferDescriptor(
			checked(vertexCapacity * vertexStride),
			BufferBindFlags.Vertex,
			BufferUsage.Dynamic
		));
		originBuffer = graphics.CreateBuffer(new GraphicsBufferDescriptor(
			checked(originCapacity * Marshal.SizeOf<Vector3>()),
			BufferBindFlags.Vertex,
			BufferUsage.Dynamic
		));
		vertexArray = graphics.CreateVertexArray();
		vertexArray.PrimitiveType = PrimitiveType.Triangles;
		ConfigureVertexArray(vertexArray, vertexBuffer, originBuffer, vertexStride);
		freeRanges.Add(new VoxelGeometryRange(0, vertexCapacity));
		OriginCapacity = originCapacity;
	}

	internal int PageIndex { get; }

	internal int VertexCapacity { get; }

	internal int OriginCapacity { get; }

	internal int AllocationCount { get; private set; }

	internal int FindBestRange(int required)
	{
		lock (sync)
		{
			int best = int.MaxValue;

			foreach (VoxelGeometryRange range in freeRanges)
			{
				if (range.Count >= required && range.Count < best)
				{
					best = range.Count;
				}
			}

			return best;
		}
	}

	internal VoxelGeometryAllocation Allocate(int capacity, int vertexCount)
	{
		lock (sync)
		{
			ObjectDisposedException.ThrowIf(disposed, this);
			int bestIndex = -1;
			int bestSize = int.MaxValue;

			for (int index = 0; index < freeRanges.Count; index++)
			{
				VoxelGeometryRange range = freeRanges[index];

				if (range.Count >= capacity && range.Count < bestSize)
				{
					bestIndex = index;
					bestSize = range.Count;
				}
			}

			if (bestIndex < 0)
			{
				throw new InvalidOperationException("The selected geometry page has no suitable free range.");
			}

			VoxelGeometryRange selected = freeRanges[bestIndex];
			freeRanges.RemoveAt(bestIndex);

			if (selected.Count > capacity)
			{
				freeRanges.Insert(
					bestIndex,
					new VoxelGeometryRange(selected.First + capacity, selected.Count - capacity)
				);
			}

			int originIndex = AllocateOriginIndex();
			AllocationCount++;

			return new VoxelGeometryAllocation(
				this,
				selected.First,
				capacity,
				vertexCount,
				originIndex
			);
		}
	}

	internal void Write(
		VoxelGeometryAllocation allocation,
		ReadOnlySpan<VoxelVertex> vertices,
		Vector3 origin
	)
	{
		ObjectDisposedException.ThrowIf(disposed, this);

		if (!ReferenceEquals(allocation.Page, this))
		{
			throw new ArgumentException("The allocation belongs to another geometry page.", nameof(allocation));
		}

		if (vertices.Length > allocation.Capacity)
		{
			throw new ArgumentOutOfRangeException(nameof(vertices));
		}

		int vertexStride = Marshal.SizeOf<VoxelVertex>();
		vertexBuffer.Write(vertices, checked(allocation.FirstVertex * vertexStride));
		Span<Vector3> originValue = stackalloc Vector3[1];
		originValue[0] = origin;
		originBuffer.Write(originValue, checked(allocation.OriginIndex * Marshal.SizeOf<Vector3>()));
		allocation.VertexCount = vertices.Length;
	}

	internal void Draw(GraphicsBuffer indirectBuffer, int drawCount)
	{
		ObjectDisposedException.ThrowIf(disposed, this);
		vertexArray.MultiDrawArraysIndirect(indirectBuffer, 0, drawCount);
	}

	internal void Free(VoxelGeometryAllocation allocation)
	{
		lock (sync)
		{
			if (disposed)
			{
				return;
			}

			freeRanges.Add(new VoxelGeometryRange(allocation.FirstVertex, allocation.Capacity));
			freeRanges.Sort(static (left, right) => left.First.CompareTo(right.First));
			CoalesceFreeRanges();
			freeOriginIndices.Push(allocation.OriginIndex);
			AllocationCount--;
		}
	}

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}

		disposed = true;
		vertexArray.Dispose();
		originBuffer.Dispose();
		vertexBuffer.Dispose();
		freeRanges.Clear();
		freeOriginIndices.Clear();
	}

	private int AllocateOriginIndex()
	{
		if (freeOriginIndices.Count > 0)
		{
			return freeOriginIndices.Pop();
		}

		if (nextOriginIndex >= OriginCapacity)
		{
			throw new InvalidOperationException("The geometry page origin table is full.");
		}

		return nextOriginIndex++;
	}

	private void CoalesceFreeRanges()
	{
		int write = 0;

		for (int read = 0; read < freeRanges.Count; read++)
		{
			VoxelGeometryRange range = freeRanges[read];

			if (write > 0)
			{
				VoxelGeometryRange previous = freeRanges[write - 1];

				if (previous.First + previous.Count == range.First)
				{
					freeRanges[write - 1] = new VoxelGeometryRange(
						previous.First,
						previous.Count + range.Count
					);
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

	private static void ConfigureVertexArray(
		VertexArray array,
		GraphicsBuffer vertices,
		GraphicsBuffer origins,
		int stride
	)
	{
		uint vertexBinding = array.BindVertexBuffer(vertices, 0, 0, stride);
		array.AttribFormat(0, 3, VertexElementType.Float, false, 0);
		array.AttribBinding(0, vertexBinding);
		array.AttribFormat(1, 4, VertexElementType.UnsignedByte, true, 12);
		array.AttribBinding(1, vertexBinding);
		array.AttribFormat(2, 2, VertexElementType.Float, false, 16);
		array.AttribBinding(2, vertexBinding);
		array.AttribFormat(3, 3, VertexElementType.Float, false, 24);
		array.AttribBinding(3, vertexBinding);
		array.AttribFormat(4, 4, VertexElementType.Float, false, 36);
		array.AttribBinding(4, vertexBinding);
		array.AttribFormat(5, 4, VertexElementType.Float, false, 52);
		array.AttribBinding(5, vertexBinding);
		array.AttribFormat(6, 4, VertexElementType.UnsignedByte, true, 68);
		array.AttribBinding(6, vertexBinding);
		array.AttribIFormat(8, 1, VertexElementType.Int, 72);
		array.AttribBinding(8, vertexBinding);

		uint originBinding = array.BindVertexBuffer(
			origins,
			1,
			0,
			Marshal.SizeOf<Vector3>()
		);
		array.AttribFormat(7, 3, VertexElementType.Float, false, 0);
		array.AttribBinding(7, originBinding);
		array.BindingDivisor(originBinding, 1);
	}

	private readonly record struct VoxelGeometryRange(int First, int Count);
}

internal sealed class VoxelGeometryAllocation
{
	private int retainedCount;
	private int ownerReleased;
	private int freed;

	internal VoxelGeometryAllocation(
		VoxelGeometryPage page,
		int firstVertex,
		int capacity,
		int vertexCount,
		int originIndex
	)
	{
		Page = page ?? throw new ArgumentNullException(nameof(page));
		FirstVertex = firstVertex;
		Capacity = capacity;
		VertexCount = vertexCount;
		OriginIndex = originIndex;
	}

	internal VoxelGeometryPage Page { get; }

	internal int FirstVertex { get; }

	internal int Capacity { get; }

	internal int VertexCount { get; set; }

	internal int OriginIndex { get; }

	internal bool IsRetained => Volatile.Read(ref retainedCount) > 0;

	internal DrawArraysIndirectCommand CreateDrawCommand()
	{
		return new DrawArraysIndirectCommand(
			checked((uint)VertexCount),
			1,
			checked((uint)FirstVertex),
			checked((uint)OriginIndex)
		);
	}

	internal void Retain()
	{
		if (Volatile.Read(ref ownerReleased) != 0)
		{
			throw new ObjectDisposedException(nameof(VoxelGeometryAllocation));
		}

		Interlocked.Increment(ref retainedCount);
	}

	internal void ReleaseRetained()
	{
		int remaining = Interlocked.Decrement(ref retainedCount);

		if (remaining < 0)
		{
			Interlocked.Increment(ref retainedCount);
			throw new InvalidOperationException("The geometry allocation has no retained reference to release.");
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
			Page.Free(this);
		}
	}
}
