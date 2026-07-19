using System;
using System.Runtime.InteropServices;
using System.Threading;
using FishGfx.Graphics;

namespace FishGfx.Voxels;

/// <summary>
/// Context-thread GPU storage for voxel vertices with dedicated normal, wave, and packed-light attributes.
/// </summary>
internal sealed class VoxelMesh : IDisposable
{
	private readonly VertexArray vertexArray;
	private readonly GraphicsBuffer vertexBuffer;
	private int capacity;
	private int ownerReleased;
	private int storageDisposed;
	private int retainedCount;

	internal VoxelMesh(
		GraphicsContext graphics,
		BufferUsage usage = BufferUsage.Dynamic
	)
		: this(graphics, 64, usage)
	{
	}

	internal VoxelMesh(
		GraphicsContext graphics,
		int initialCapacity,
		BufferUsage usage = BufferUsage.Dynamic
	)
	{
		if (graphics == null)
		{
			throw new ArgumentNullException(nameof(graphics));
		}

		if (initialCapacity <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(initialCapacity));
		}

		vertexArray = graphics.CreateVertexArray();
		vertexArray.PrimitiveType = PrimitiveType.Triangles;
		int stride = Marshal.SizeOf<VoxelVertex>();
		capacity = CalculateCapacity(0, initialCapacity);
		vertexBuffer = graphics.CreateBuffer(
			new GraphicsBufferDescriptor(stride * capacity, BufferBindFlags.Vertex, usage)
		);
		uint binding = vertexArray.BindVertexBuffer(vertexBuffer, -1, 0, stride);

		vertexArray.AttribFormat(0, 3, VertexElementType.Float, false, 0);
		vertexArray.AttribBinding(0, binding);
		vertexArray.AttribFormat(
			1,
			4,
			VertexElementType.UnsignedByte,
			true,
			12
		);
		vertexArray.AttribBinding(1, binding);
		vertexArray.AttribFormat(2, 2, VertexElementType.Float, false, 16);
		vertexArray.AttribBinding(2, binding);
		vertexArray.AttribFormat(3, 3, VertexElementType.Float, false, 24);
		vertexArray.AttribBinding(3, binding);
		vertexArray.AttribFormat(4, 4, VertexElementType.Float, false, 36);
		vertexArray.AttribBinding(4, binding);
		vertexArray.AttribFormat(5, 4, VertexElementType.Float, false, 52);
		vertexArray.AttribBinding(5, binding);
		vertexArray.AttribFormat(
			6,
			4,
			VertexElementType.UnsignedByte,
			true,
			68
		);
		vertexArray.AttribBinding(6, binding);
	}

	internal int VertexCount { get; private set; }
	internal bool IsRetained => Volatile.Read(ref retainedCount) > 0;

	internal void RetainReference()
	{
		ThrowIfDisposed();
		Interlocked.Increment(ref retainedCount);

		if (Volatile.Read(ref ownerReleased) != 0)
		{
			ReleaseReference();
			throw new ObjectDisposedException(nameof(VoxelMesh));
		}
	}

	internal void Update(VoxelVertex[] vertices)
	{
		Update(vertices, vertices?.Length ?? throw new ArgumentNullException(nameof(vertices)));
	}

	internal void Update(ReadOnlySpan<VoxelVertex> vertices)
	{
		Update(vertices, vertices.Length);
	}

	internal void Update(VoxelVertex[] vertices, int count)
	{
		ThrowIfDisposed();

		if (vertices == null)
		{
			throw new ArgumentNullException(nameof(vertices));
		}

		if (count < 0 || count > vertices.Length)
		{
			throw new ArgumentOutOfRangeException(nameof(count));
		}

		if (count > capacity)
		{
			capacity = CalculateCapacity(capacity, count);
			vertexBuffer.ResizeDiscard(checked(capacity * Marshal.SizeOf<VoxelVertex>()));
		}

		if (count > 0)
		{
			vertexBuffer.Write<VoxelVertex>(vertices.AsSpan(0, count));
		}

		VertexCount = count;
	}

	private void Update(ReadOnlySpan<VoxelVertex> vertices, int count)
	{
		ThrowIfDisposed();

		if (count < 0 || count > vertices.Length)
		{
			throw new ArgumentOutOfRangeException(nameof(count));
		}

		if (count > capacity)
		{
			capacity = CalculateCapacity(capacity, count);
			vertexBuffer.ResizeDiscard(checked(capacity * Marshal.SizeOf<VoxelVertex>()));
		}

		if (count > 0)
		{
			vertexBuffer.Write(vertices[..count]);
		}

		VertexCount = count;
	}

	internal void Draw()
	{
		ThrowIfDisposed();
		vertexArray.Draw(0, VertexCount);
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref ownerReleased, 1) != 0)
		{
			return;
		}

		TryDisposeStorage();
	}

	internal void DrawRetained()
	{
		if (Volatile.Read(ref storageDisposed) != 0)
		{
			throw new ObjectDisposedException(nameof(VoxelMesh));
		}

		vertexArray.Draw(0, VertexCount);
	}

	internal void ReleaseReference()
	{
		int remaining = Interlocked.Decrement(ref retainedCount);

		if (remaining < 0)
		{
			Interlocked.Increment(ref retainedCount);
			throw new InvalidOperationException("The voxel mesh has no retained references to release.");
		}

		TryDisposeStorage();
	}

	private void TryDisposeStorage()
	{
		if (Volatile.Read(ref ownerReleased) == 0 || Volatile.Read(ref retainedCount) > 0)
		{
			return;
		}

		if (Interlocked.CompareExchange(ref storageDisposed, 1, 0) != 0)
		{
			return;
		}

		vertexBuffer?.Dispose();
		vertexArray?.Dispose();
	}

	private void ThrowIfDisposed()
	{
		if (Volatile.Read(ref ownerReleased) != 0 || Volatile.Read(ref storageDisposed) != 0)
		{
			throw new ObjectDisposedException(nameof(VoxelMesh));
		}
	}

	internal static int CalculateCapacity(int current, int required)
	{
		if (required < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(required));
		}

		if (required <= current)
		{
			return current;
		}

		int capacity = Math.Max(64, current);

		while (capacity < required)
		{
			capacity = checked(capacity * 2);
		}

		return capacity;
	}
}
