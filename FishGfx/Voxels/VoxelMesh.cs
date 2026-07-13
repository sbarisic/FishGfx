using System;
using System.Runtime.InteropServices;
using FishGfx.Graphics;

namespace FishGfx.Voxels
{
	/// <summary>
	/// Context-thread GPU storage for voxel vertices with a dedicated normal attribute.
	/// </summary>
	public sealed class VoxelMesh : IDisposable
	{
		private readonly VertexArray vertexArray;
		private readonly GraphicsBuffer vertexBuffer;
		private bool disposed;

		public VoxelMesh(BufferUsage usage = BufferUsage.Dynamic)
		{
			Usage = usage;
			vertexArray = new VertexArray { PrimitiveType = PrimitiveType.Triangles };
			int stride = Marshal.SizeOf<VoxelVertex>();
			vertexBuffer = GraphicsContext.Current.CreateBuffer(
				new GraphicsBufferDescriptor(stride * 64, BufferBindFlags.Vertex, usage)
			);
			Capacity = 64;
			uint binding = vertexArray.BindVertexBuffer(vertexBuffer, Stride: stride);

			vertexArray.AttribFormat(0, 3, RelativeOffset: 0);
			vertexArray.AttribBinding(0, binding);
			vertexArray.AttribFormat(
				1,
				4,
				VertexElementType.UnsignedByte,
				Normalized: true,
				RelativeOffset: 12
			);
			vertexArray.AttribBinding(1, binding);
			vertexArray.AttribFormat(2, 2, RelativeOffset: 16);
			vertexArray.AttribBinding(2, binding);
			vertexArray.AttribFormat(3, 3, RelativeOffset: 24);
			vertexArray.AttribBinding(3, binding);
			vertexArray.AttribFormat(4, 4, RelativeOffset: 36);
			vertexArray.AttribBinding(4, binding);
		}

		public BufferUsage Usage { get; }
		public int VertexCount { get; private set; }
		public int Capacity { get; private set; }

		public void Update(VoxelVertex[] vertices)
		{
			Update(vertices, vertices?.Length ?? throw new ArgumentNullException(nameof(vertices)));
		}

		internal void Update(VoxelVertex[] vertices, int count)
		{
			ThrowIfDisposed();

			if (vertices == null)
				throw new ArgumentNullException(nameof(vertices));
			if (count < 0 || count > vertices.Length)
				throw new ArgumentOutOfRangeException(nameof(count));

			if (count > Capacity)
			{
				Capacity = CalculateCapacity(Capacity, count);
				vertexBuffer.ResizeDiscard(checked(Capacity * Marshal.SizeOf<VoxelVertex>()));
			}

			if (count > 0)
				vertexBuffer.Write<VoxelVertex>(vertices.AsSpan(0, count));

			VertexCount = count;
		}

		public void Draw()
		{
			ThrowIfDisposed();
			vertexArray.Draw(0, VertexCount);
		}

		public void Dispose()
		{
			if (disposed)
				return;

			disposed = true;
			vertexBuffer.Dispose();
			vertexArray.Dispose();
		}

		private void ThrowIfDisposed()
		{
			if (disposed)
				throw new ObjectDisposedException(nameof(VoxelMesh));
		}

		internal static int CalculateCapacity(int current, int required)
		{
			if (required < 0)
				throw new ArgumentOutOfRangeException(nameof(required));
			if (required <= current)
				return current;

			int capacity = Math.Max(64, current);

			while (capacity < required)
				capacity = checked(capacity * 2);

			return capacity;
		}
	}
}
