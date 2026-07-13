using System;
using System.Linq;
using System.Numerics;
using FishGfx.Formats;
using Silk.NET.OpenGL;

namespace FishGfx.Graphics.Drawables
{
	public enum PolygonMode
	{
		Point = 6912,
		Line = 6913,
		Fill = 6914,
	}

	public sealed class Mesh3D : IDrawable, IDisposable
	{
		internal const int VERTEX_ATTRIB = 0;
		internal const int COLOR_ATTRIB = 1;
		internal const int UV_ATTRIB = 2;

		private readonly BufferUsage usage;
		private GraphicsBuffer vertBuffer;
		private GraphicsBuffer colorBuffer;
		private GraphicsBuffer uvBuffer;
		private GraphicsBuffer elementBuffer;
		private int vertexBinding = -1;
		private int colorBinding = -1;
		private int uvBinding = -1;
		private int vertexCount;
		private int elementCount;
		private bool hasColors;
		private bool disposed;

		public Color DefaultColor = Color.White;
		public PolygonMode PolygonMode = PolygonMode.Fill;
		public VertexArray VAO { get; }
		public GraphicsBuffer VertexBuffer => vertBuffer;
		public GraphicsBuffer ColorBuffer => colorBuffer;
		public GraphicsBuffer UVBuffer => uvBuffer;
		public GraphicsBuffer ElementBuffer => elementBuffer;

		public PrimitiveType PrimitiveType
		{
			get => VAO.PrimitiveType;
			set => VAO.PrimitiveType = value;
		}

		public Mesh3D(BufferUsage usage = BufferUsage.Static)
		{
			VAO = new VertexArray();
			this.usage = usage;
		}

		public Mesh3D(Vertex3[] vertices, bool HasUVs = true, bool HasColors = true) : this() =>
			SetVertices(vertices, vertices?.Length ?? throw new ArgumentNullException(nameof(vertices)), HasUVs, HasColors);

		public Mesh3D(Vertex2[] vertices, bool HasUVs = true, bool HasColors = true) : this()
		{
			if (vertices == null) throw new ArgumentNullException(nameof(vertices));
			Vertex3[] vertices3 = vertices.Select(v => new Vertex3(new Vector3(v.Position.X, v.Position.Y, 0), v.UV, v.Color)).ToArray();
			SetVertices(vertices3, vertices3.Length, HasUVs, HasColors);
		}

		public Mesh3D(GenericMesh mesh, bool HasUVs = true, bool HasColors = true) :
			this((mesh ?? throw new ArgumentNullException(nameof(mesh))).Vertices.ToArray(), HasUVs, HasColors) { }

		public void SetVertices(Vector3[] vertices)
		{
			ThrowIfDisposed();
			if (vertices == null) throw new ArgumentNullException(nameof(vertices));
			SetAttribute(ref vertBuffer, ref vertexBinding, vertices, VERTEX_ATTRIB, 3, VertexElementType.Float, false, 0, sizeof(float) * 3);
			vertexCount = vertices.Length;
		}

		public void SetColors(Color[] colors)
		{
			ThrowIfDisposed();
			if (colors == null) throw new ArgumentNullException(nameof(colors));
			hasColors = SetAttribute(ref colorBuffer, ref colorBinding, colors, COLOR_ATTRIB, 4, VertexElementType.UnsignedByte, true, 0, sizeof(byte) * 4);
		}

		public void SetUVs(Vector2[] uvs)
		{
			ThrowIfDisposed();
			if (uvs == null) throw new ArgumentNullException(nameof(uvs));
			SetAttribute(ref uvBuffer, ref uvBinding, uvs, UV_ATTRIB, 2, VertexElementType.Float, false, 0, sizeof(float) * 2);
		}

		public void SetElements(params uint[] elements)
		{
			ThrowIfDisposed();
			if (elements == null || elements.Length == 0)
			{
				VAO.BindElementBuffer(null);
				elementCount = 0;
				return;
			}
			if (elementBuffer == null)
				elementBuffer = GraphicsContext.Current.CreateBuffer<uint>(elements, BufferBindFlags.Index, usage);
			else
				Upload(elementBuffer, elements);
			VAO.BindElementBuffer(elementBuffer);
			elementCount = elements.Length;
		}

		public void SetVertices(params Vertex3[] vertices) =>
			SetVertices(vertices, vertices?.Length ?? throw new ArgumentNullException(nameof(vertices)));

		public void SetVertices(Vertex3[] vertices, int count, bool HasUVs = true, bool HasColors = true)
		{
			ThrowIfDisposed();
			if (vertices == null) throw new ArgumentNullException(nameof(vertices));
			if (count < 0 || count > vertices.Length) throw new ArgumentOutOfRangeException(nameof(count));
			ReadOnlySpan<Vertex3> data = vertices.AsSpan(0, count);
			SetInterleavedAttribute(ref vertBuffer, ref vertexBinding, data, VERTEX_ATTRIB, 3, VertexElementType.Float, false, 0);
			if (HasUVs) SetInterleavedAttribute(ref uvBuffer, ref uvBinding, data, UV_ATTRIB, 2, VertexElementType.Float, false, 3 * sizeof(float));
			else VAO.AttribEnable(UV_ATTRIB, false);
			hasColors = HasColors && SetInterleavedAttribute(ref colorBuffer, ref colorBinding, data, COLOR_ATTRIB, 4, VertexElementType.UnsignedByte, true, 5 * sizeof(float));
			if (!HasColors) VAO.AttribEnable(COLOR_ATTRIB, false);
			vertexCount = count;
		}

		public void Draw()
		{
			ThrowIfDisposed();
			Internal_OpenGL.GL.PolygonMode(TriangleFace.FrontAndBack, (Silk.NET.OpenGL.PolygonMode)PolygonMode);
			if (!hasColors) VertexArray.VertexAttrib(COLOR_ATTRIB, DefaultColor);
			if (!VAO.HasElementBuffer) VAO.Draw(0, vertexCount);
			else VAO.DrawElements(Count: elementCount, ElementType: IndexElementType.UnsignedInt);
		}

		private bool SetAttribute<T>(ref GraphicsBuffer buffer, ref int binding, ReadOnlySpan<T> data, uint attribute, int components,
			VertexElementType type, bool normalized, uint offset, int stride) where T : unmanaged
		{
			if (data.Length == 0) { VAO.AttribEnable(attribute, false); return false; }
			if (buffer == null) buffer = GraphicsContext.Current.CreateBuffer<T>(data, BufferBindFlags.Vertex, usage);
			else Upload(buffer, data);
			if (binding < 0) binding = (int)VAO.BindVertexBuffer(buffer, Stride: stride);
			else VAO.BindVertexBuffer(buffer, binding, Stride: stride);
			VAO.AttribFormat(attribute, components, type, normalized, offset);
			VAO.AttribBinding(attribute, (uint)binding);
			return true;
		}

		private bool SetInterleavedAttribute(ref GraphicsBuffer buffer, ref int binding, ReadOnlySpan<Vertex3> data, uint attribute,
			int components, VertexElementType type, bool normalized, uint offset) =>
			SetAttribute(ref buffer, ref binding, data, attribute, components, type, normalized, offset,
				System.Runtime.CompilerServices.Unsafe.SizeOf<Vertex3>());

		private static void Upload<T>(GraphicsBuffer buffer, ReadOnlySpan<T> data) where T : unmanaged
		{
			int size = checked(data.Length * System.Runtime.CompilerServices.Unsafe.SizeOf<T>());
			if (buffer.SizeInBytes != size) buffer.ResizeDiscard(size);
			buffer.Write(data);
		}

		public void Dispose()
		{
			if (disposed) return;
			disposed = true;
			vertBuffer?.Dispose();
			colorBuffer?.Dispose();
			uvBuffer?.Dispose();
			elementBuffer?.Dispose();
			VAO.Dispose();
		}

		private void ThrowIfDisposed()
		{
			if (disposed) throw new ObjectDisposedException(nameof(Mesh3D));
		}
	}
}
