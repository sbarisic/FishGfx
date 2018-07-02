using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;
using OpenGL;

namespace FishGfx.Graphics.Drawables {
	public enum PolygonMode {
		Point = 6912,
		Line = 6913,
		Fill = 6914
	}

	public class Mesh2D : IDrawable {
		internal const int VERTEX_ATTRIB = 0;
		internal const int COLOR_ATTRIB = 1;
		internal const int UV_ATTRIB = 2;

		public Color DefaultColor = Color.White;
		public PolygonMode PolygonMode = PolygonMode.Fill;

		VertexArray VAO;
		BufferObject VertBuffer, ColorBuffer, UVBuffer, ElementBuffer;
		BufferUsage Usage;

		public PrimitiveType PrimitiveType {
			get {
				return VAO.PrimitiveType;
			}

			set {
				VAO.PrimitiveType = value;
			}
		}

		public Mesh2D(BufferUsage Usage = BufferUsage.StaticDraw) {
			VAO = new VertexArray();
			this.Usage = Usage;
		}

		public void SetVertices(Vector2[] Verts) {
			if (VertBuffer == null) {
				VAO.AttribFormat(VERTEX_ATTRIB, 2);
				VAO.AttribBinding(VERTEX_ATTRIB, VAO.BindVertexBuffer(VertBuffer = new BufferObject(), Stride: 2 * sizeof(float)));
			}

			VertBuffer.SetData(Verts, Usage: Usage);
			VAO.AttribEnable(VERTEX_ATTRIB, Verts != null);
		}

		public void SetColors(Color[] Colors) {
			if (ColorBuffer == null) {
				VAO.AttribFormat(COLOR_ATTRIB, Size: 4, AttribType: VertexAttribType.UnsignedByte, Normalized: true);
				VAO.AttribBinding(COLOR_ATTRIB, VAO.BindVertexBuffer(ColorBuffer = new BufferObject(), Stride: 4 * sizeof(byte)));
			}

			ColorBuffer.SetData(Colors, Usage: Usage);
			VAO.AttribEnable(COLOR_ATTRIB, Colors != null);
		}

		public void SetUVs(Vector2[] UVs) {
			if (UVBuffer == null) {
				VAO.AttribFormat(UV_ATTRIB, Size: 2);
				VAO.AttribBinding(UV_ATTRIB, VAO.BindVertexBuffer(UVBuffer = new BufferObject(), Stride: 2 * sizeof(float)));
			}

			UVBuffer.SetData(UVs, Usage: Usage);
			VAO.AttribEnable(UV_ATTRIB, UVs != null);
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

		public void SetVertices(params Vertex2[] Verts) {
			SetVertices(Verts.Select((V) => V.Position).ToArray());
			SetUVs(Verts.Select((V) => V.UV).ToArray());
			SetColors(Verts.Select((V) => V.Color).ToArray());
		}

		public void Draw(int First, int Count) {
			if (VAO.HasElementBuffer)
				throw new Exception("Use DrawElements when you supply element buffer");

			VAO.Draw(First, Count);
		}

		public void DrawElements(int Offset, int Count) {
			if (!VAO.HasElementBuffer)
				throw new Exception("Use Draw when you don't supply element buffer");

			VAO.DrawElements(Offset, Count, ElementType: DrawElementsType.UnsignedInt);
		}

		public void Draw() {
			Gl.PolygonMode(MaterialFace.FrontAndBack, (OpenGL.PolygonMode)PolygonMode);
			//Gl.LineWidth(10);

			if (ColorBuffer == null)
				VertexArray.VertexAttrib(COLOR_ATTRIB, DefaultColor);

			if (!VAO.HasElementBuffer)
				Draw(0, VertBuffer.ElementCount);
			else
				DrawElements(0, -1);
		}
	}
}
