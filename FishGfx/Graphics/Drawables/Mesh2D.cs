using OpenGL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace FishGfx.Graphics.Drawables {
	[Obsolete]
	public class Mesh2D : IDrawable {
		internal const int VERTEX_ATTRIB = 0;
		internal const int COLOR_ATTRIB = 1;
		internal const int UV_ATTRIB = 2;

		public Color DefaultColor = Color.White;
		public PolygonMode PolygonMode = PolygonMode.Fill;

		public VertexArray VAO;
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

		// TODO: Port code from Mesh3D
		public void SetVertices(params Vertex2[] Verts) {
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

			VAO.DrawElements(Offset, Count, ElementType: DrawElementsType.UnsignedInt);
		}*/

		public void DrawEx(int First, int Count) {
			Gl.PolygonMode(MaterialFace.FrontAndBack, (OpenGL.PolygonMode)PolygonMode);
			//Gl.LineWidth(10);

			if (ColorBuffer == null)
				VertexArray.VertexAttrib(COLOR_ATTRIB, DefaultColor);

			if (First == -1 && Count == -1) {
				if (!VAO.HasElementBuffer) {
					First = 0;
					Count = VertBuffer.ElementCount;
				} else {
					First = 0;
					Count = -1;
				}
			}

			if (!VAO.HasElementBuffer) {
				VAO.Draw(First, Count);
				// Draw(First, Count);
			} else {
				VAO.DrawElements(First, Count, ElementType: DrawElementsType.UnsignedInt);
				//DrawElements(First, Count);
			}
		}

		public void Draw() {
			DrawEx(-1, -1);
		}
	}
}
