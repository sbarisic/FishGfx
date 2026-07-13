using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Silk.NET.OpenGL;

namespace FishGfx.Graphics.Drawables
{
	public sealed class Mesh2D : IDrawable, IDisposable
	{
		internal const int VERTEX_ATTRIB = 0;
		internal const int COLOR_ATTRIB = 1;
		internal const int UV_ATTRIB = 2;

		public Color DefaultColor = Color.White;
		public PolygonMode PolygonMode = PolygonMode.Fill;

		public VertexArray VAO { get; }
		GraphicsBuffer VertBuffer,
			ColorBuffer,
			UVBuffer,
			ElementBuffer;
		readonly BufferUsage Usage;
		int vertexCount;
		int elementCount;
		bool hasColors;
		bool disposed;

		public PrimitiveType PrimitiveType
		{
			get { return VAO.PrimitiveType; }
			set { VAO.PrimitiveType = value; }
		}

		public Mesh2D(BufferUsage Usage = BufferUsage.Static)
		{
			VAO = new VertexArray();
			this.Usage = Usage;
		}

		public void SetVertices(Vector2[] Verts)
		{
			ThrowIfDisposed();
			if (Verts == null || Verts.Length == 0) { VAO.AttribEnable(VERTEX_ATTRIB, false); vertexCount = 0; return; }
			if (VertBuffer == null)
			{
				VAO.AttribFormat(VERTEX_ATTRIB, 2);
				VertBuffer = CreateBuffer(Verts, BufferBindFlags.Vertex);
				VAO.AttribBinding(
					VERTEX_ATTRIB,
					VAO.BindVertexBuffer(VertBuffer, Stride: 2 * sizeof(float))
				);
			}
			else Upload(VertBuffer, Verts);
			vertexCount = Verts.Length;
			VAO.AttribEnable(VERTEX_ATTRIB);
		}

		public void SetColors(Color[] Colors)
		{
			ThrowIfDisposed();
			if (Colors == null || Colors.Length == 0) { VAO.AttribEnable(COLOR_ATTRIB, false); hasColors = false; return; }
			if (ColorBuffer == null)
			{
				VAO.AttribFormat(COLOR_ATTRIB, Size: 4, AttribType: VertexElementType.UnsignedByte, Normalized: true);
				ColorBuffer = CreateBuffer(Colors, BufferBindFlags.Vertex);
				VAO.AttribBinding(
					COLOR_ATTRIB,
					VAO.BindVertexBuffer(ColorBuffer, Stride: 4 * sizeof(byte))
				);
			}
			else Upload(ColorBuffer, Colors);
			VAO.AttribEnable(COLOR_ATTRIB);
			hasColors = true;
		}

		public void SetUVs(Vector2[] UVs)
		{
			ThrowIfDisposed();
			if (UVs == null || UVs.Length == 0) { VAO.AttribEnable(UV_ATTRIB, false); return; }
			if (UVBuffer == null)
			{
				VAO.AttribFormat(UV_ATTRIB, Size: 2);
				UVBuffer = CreateBuffer(UVs, BufferBindFlags.Vertex);
				VAO.AttribBinding(
					UV_ATTRIB,
					VAO.BindVertexBuffer(UVBuffer, Stride: 2 * sizeof(float))
				);
			}
			else Upload(UVBuffer, UVs);
			VAO.AttribEnable(UV_ATTRIB);
		}

		public void SetElements(params uint[] Elements)
		{
			ThrowIfDisposed();
			if (Elements != null && Elements.Length > 0)
			{
				if (ElementBuffer == null)
					ElementBuffer = CreateBuffer(Elements, BufferBindFlags.Index);
				else Upload(ElementBuffer, Elements);
				VAO.BindElementBuffer(ElementBuffer);
				elementCount = Elements.Length;
			}
			else {
				VAO.BindElementBuffer(null);
				elementCount = 0;
			}
		}

		public void SetVertices(params Vertex2[] Verts)
		{
			if (Verts == null) throw new ArgumentNullException(nameof(Verts));
			SetVertices(Verts.Select((V) => V.Position).ToArray());
			SetUVs(Verts.Select((V) => V.UV).ToArray());
			SetColors(Verts.Select((V) => V.Color).ToArray());
		}

		/*[Obsolete]
		public void Draw(int First, int Count) {
			if (VAO.HasElementBuffer)
				throw new Exception("Use DrawElements when you supply element buffer");

			VAO.Draw(First, Count);
		}

		[Obsolete]
		public void DrawElements(int Offset, int Count) {
			if (!VAO.HasElementBuffer)
				throw new Exception("Use Draw when you don't supply element buffer");

			VAO.DrawElements(Offset, Count, ElementType: IndexElementType.UnsignedInt);
		}*/

		public void DrawEx(int First, int Count)
		{
			ThrowIfDisposed();
			Internal_OpenGL.GL.PolygonMode(TriangleFace.FrontAndBack, (Silk.NET.OpenGL.PolygonMode)PolygonMode);
			//Internal_OpenGL.GL.LineWidth(10);

			if (!hasColors)
				VertexArray.VertexAttrib(COLOR_ATTRIB, DefaultColor);

			if (First == -1 && Count == -1)
			{
				if (!VAO.HasElementBuffer)
				{
					First = 0;
					Count = vertexCount;
				}
				else
				{
					First = 0;
					Count = elementCount;
				}
			}

			if (!VAO.HasElementBuffer)
			{
				VAO.Draw(First, Count);
				// Draw(First, Count);
			}
			else
			{
				VAO.DrawElements(First, Count, ElementType: IndexElementType.UnsignedInt);
				//DrawElements(First, Count);
			}
		}

		public void Draw()
		{
			DrawEx(-1, -1);
		}

		private GraphicsBuffer CreateBuffer<T>(T[] data, BufferBindFlags flags) where T : unmanaged =>
			GraphicsContext.Current.CreateBuffer<T>(data, flags, Usage);

		private static void Upload<T>(GraphicsBuffer buffer, T[] data) where T : unmanaged
		{
			int size = checked(data.Length * System.Runtime.CompilerServices.Unsafe.SizeOf<T>());
			if (size == 0) return;
			if (buffer.SizeInBytes != size) buffer.ResizeDiscard(size);
			buffer.Write<T>(data);
		}

		public void Dispose()
		{
			if (disposed) return;
			disposed = true;
			VertBuffer?.Dispose();
			ColorBuffer?.Dispose();
			UVBuffer?.Dispose();
			ElementBuffer?.Dispose();
			VAO.Dispose();
		}

		private void ThrowIfDisposed()
		{
			if (disposed) throw new ObjectDisposedException(nameof(Mesh2D));
		}
	}
}
