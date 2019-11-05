using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;
using OpenGL;
using FishGfx.Formats;

namespace FishGfx.Graphics.Drawables {
	public enum PolygonMode {
		Point = 6912,
		Line = 6913,
		Fill = 6914
	}

	public unsafe class Mesh3D : IDrawable {
		internal const int VERTEX_ATTRIB = 0;
		internal const int COLOR_ATTRIB = 1;
		internal const int UV_ATTRIB = 2;

		public Color DefaultColor = Color.White;
		public PolygonMode PolygonMode = PolygonMode.Fill;

		public VertexArray VAO;
		public BufferObject VertBuffer, ColorBuffer, UVBuffer, ElementBuffer;
		BufferUsage Usage;

		public PrimitiveType PrimitiveType {
			get {
				return VAO.PrimitiveType;
			}

			set {
				VAO.PrimitiveType = value;
			}
		}

		public Mesh3D(BufferUsage Usage = BufferUsage.StaticDraw) {
			VAO = new VertexArray();
			this.Usage = Usage;
		}

		public Mesh3D(Vertex3[] Vertices, bool HasUVs = true, bool HasColors = true) : this() {
			SetVertices(Vertices, Vertices.Length, HasUVs, HasColors);
		}

		public Mesh3D(Vertex2[] Vertices, bool HasUVs = true, bool HasColors = true) : this() {
			Vertex3[] Vertices3 = Vertices.Select(V => new Vertex3(new Vector3(V.Position.X, V.Position.Y, 0), V.UV, V.Color)).ToArray();
			SetVertices(Vertices3, Vertices3.Length, HasUVs, HasColors);
		}

		public Mesh3D(GenericMesh Msh, bool HasUVs = true, bool HasColors = true) : this(Msh.Vertices.ToArray(), HasUVs, HasColors) {
		}

		void SetVertices(uint Size, IntPtr Data, int ElementCount, int RelativeOffset, int Stride) {
			if (VertBuffer == null) {
				VAO.AttribFormat(VERTEX_ATTRIB, ElementCount);
				VAO.AttribBinding(VERTEX_ATTRIB, VAO.BindVertexBuffer(VertBuffer = new BufferObject(), Offset: RelativeOffset, Stride: Stride));
			}

			if (Data != IntPtr.Zero)
				VertBuffer.SetData(Size, Data, (int)(Size / Stride), Usage);
			VAO.AttribEnable(VERTEX_ATTRIB, Data != IntPtr.Zero);
		}

		public void SetVertices(Vector3[] Verts) {
			fixed (Vector3* VertsPtr = Verts)
				SetVertices((uint)(Verts.Length * sizeof(Vector3)), new IntPtr(VertsPtr), 3, 0, sizeof(Vector3));
		}

		void SetColors(uint Size, IntPtr Data, int ElementCount, int RelativeOffset, int Stride) {
			if (ColorBuffer == null) {
				VAO.AttribFormat(COLOR_ATTRIB, Size: ElementCount, AttribType: VertexAttribType.UnsignedByte, Normalized: true);
				VAO.AttribBinding(COLOR_ATTRIB, VAO.BindVertexBuffer(ColorBuffer = new BufferObject(), Offset: RelativeOffset, Stride: Stride));
			}

			if (Data != IntPtr.Zero)
				ColorBuffer.SetData(Size, Data, (int)(Size / Stride), Usage);
			VAO.AttribEnable(COLOR_ATTRIB, Data != IntPtr.Zero);
		}


		public void SetColors(Color[] Colors) {
			fixed (Color* ColorPtr = Colors)
				SetColors((uint)(Colors.Length * sizeof(Color)), new IntPtr(ColorPtr), 4, 0, sizeof(Color));
		}

		void SetUVs(uint Size, IntPtr Data, int ElementCount, int RelativeOffset, int Stride) {
			/*if (UVBuffer == null) {
				VAO.AttribFormat(UV_ATTRIB, ElementCount);
				VAO.AttribBinding(UV_ATTRIB, VAO.BindVertexBuffer(UVBuffer = new BufferObject(), Offset: RelativeOffset, Stride: Stride));
			}*/

			if (UVBuffer == null)
				UVBuffer = new BufferObject();

			// TODO: Fix this, is BindVertexBuffer correct? Shouldn't this be element buffer? How to change stride on the fly?
			VAO.AttribFormat(UV_ATTRIB, ElementCount);
			VAO.AttribBinding(UV_ATTRIB, VAO.BindVertexBuffer(UVBuffer, Offset: RelativeOffset, Stride: Stride));

			if (Data != IntPtr.Zero)
				UVBuffer.SetData(Size, Data, (int)(Size / Stride), Usage);
			VAO.AttribEnable(UV_ATTRIB, Data != IntPtr.Zero);
		}

		public void SetUVs(Vector2[] UVs) {
			fixed (Vector2* UVPtr = UVs)
				SetUVs((uint)(UVs.Length * sizeof(Vector2)), new IntPtr(UVPtr), 2, 0, sizeof(Vector2));
		}

		public void SetElements(params uint[] Elements) {
			if (ElementBuffer == null)
				ElementBuffer = new BufferObject();

			if (Elements != null) {
				VAO.BindElementBuffer(ElementBuffer);
				ElementBuffer.SetData(Elements, Usage: Usage);
			} else
				VAO.BindElementBuffer(null);
		}

		public void SetVertices(params Vertex3[] Verts) {
			SetVertices(Verts, Verts.Length);
		}

		public void SetVertices(Vertex3[] Verts, int Count, bool HasUVs = true, bool HasColors = true) {
			fixed (Vertex3* VertsPtr = Verts)
				SetVertices(new IntPtr(VertsPtr), Count, HasUVs, HasColors);
		}

		void SetVertices(IntPtr VertsPtr, int Count, bool HasUVs = true, bool HasColors = true) {
			uint Size = (uint)(Count * sizeof(Vertex3));

			SetVertices(Size, VertsPtr, 3, 0, sizeof(Vertex3));

			if (HasUVs)
				SetUVs(Size, VertsPtr, 2, 3 * sizeof(float), sizeof(Vertex3));

			if (HasColors)
				SetColors(Size, VertsPtr, 4, 5 * sizeof(float), sizeof(Vertex3));
		}

		public void Draw() {
			Gl.PolygonMode(MaterialFace.FrontAndBack, (OpenGL.PolygonMode)PolygonMode);
			//Gl.LineWidth(10);

			if (ColorBuffer == null)
				VertexArray.VertexAttrib(COLOR_ATTRIB, DefaultColor);

			if (!VAO.HasElementBuffer)
				VAO.Draw(0, VertBuffer?.ElementCount ?? 0);
			else
				VAO.DrawElements(ElementType: DrawElementsType.UnsignedInt);
		}
	}
}
