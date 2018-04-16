using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenGL;
using System.Numerics;

namespace FishGfx.Graphics {
	public enum PrimitiveType {
		Points = 0,
		Lines = 1,
		LineLoop = 2,
		LineStrip = 3,
		Triangles = 4,
		TriangleStrip = 5,
		TriangleFan = 6,
		Quads = 7,
		QuadStrip = 8,
		Polygon = 9,
		LinesAdjacency = 10,
		LineStripAdjacency = 11,
		TrianglesAdjacency = 12,
		TriangleStripAdjacency = 13,
		Patches = 14
	}

	public class VertexArray : GraphicsObject {
		public PrimitiveType PrimitiveType;

		BufferObject ElementBuffer;
		List<BufferObject> BufferObjects;
		int FreeBindingIndex = 0;

		public bool HasElementBuffer {
			get {
				return ElementBuffer != null;
			}
		}

		public VertexArray() {
			ID = Gl.CreateVertexArray();

			PrimitiveType = PrimitiveType.Triangles;
			BufferObjects = new List<BufferObject>();
		}

		public override void Bind() {
			Gl.BindVertexArray(ID);
		}

		public override void Unbind() {
			Gl.BindVertexArray(0);
		}

		public void Draw(int First, int Count) {
			Bind();
			Gl.DrawArrays((OpenGL.PrimitiveType)PrimitiveType, First, Count);
			Unbind();
		}

		public void DrawElements(int Offset = 0, int Count = -1, DrawElementsType ElementType = DrawElementsType.UnsignedShort) {
			if (ElementBuffer == null)
				throw new Exception("Use Draw instead");

			if (Count == -1)
				Count = ElementBuffer.ElementCount;

			Bind();
			Gl.DrawElements((OpenGL.PrimitiveType)PrimitiveType, Count, ElementType, (IntPtr)Offset);
			Unbind();
		}

		public uint BindVertexBuffer(BufferObject Obj, int BindingIndex = -1, int Offset = 0, int Stride = 3 * sizeof(float)) {
			if (!BufferObjects.Contains(Obj))
				BufferObjects.Add(Obj);

			if (BindingIndex == -1)
				BindingIndex = FreeBindingIndex++;

			if (Obj != null)
				Gl.VertexArrayVertexBuffer(ID, (uint)BindingIndex, Obj.ID, (IntPtr)Offset, Stride);

			return (uint)BindingIndex;
		}

		public void BindElementBuffer(BufferObject Obj) {
			ElementBuffer = Obj;

			if (Obj != null)
				Gl.VertexArrayElementBuffer(ID, Obj.ID);
			else
				Gl.VertexArrayElementBuffer(ID, 0);
		}

		public void AttribEnable(uint AttribIdx, bool Enable = true) {
			if (Enable)
				Gl.EnableVertexArrayAttrib(ID, AttribIdx);
			else
				Gl.DisableVertexArrayAttrib(ID, AttribIdx);
		}

		public void AttribFormat(uint AttribIdx, int Size = 3, VertexAttribType AttribType = VertexAttribType.Float, bool Normalized = false, uint RelativeOffset = 0) {
			Gl.VertexArrayAttribFormat(ID, AttribIdx, Size, AttribType, Normalized, RelativeOffset);
		}

		public void AttribBinding(uint AttribIdx, uint BindingIdx) {
			AttribEnable(AttribIdx);
			Gl.VertexArrayAttribBinding(ID, AttribIdx, BindingIdx);
		}

		public override void GraphicsDispose() {
			Gl.DeleteVertexArrays(new uint[] { ID });
		}

		public static void VertexAttrib(uint Attrib, Vector4 Vec) {
			Gl.VertexAttrib4(Attrib, Vec.X, Vec.Y, Vec.Z, Vec.W);
		}

		public static void VertexAttrib(uint Attrib, Color Clr) {
			VertexAttrib(Attrib, new Vector4(Clr.R, Clr.G, Clr.B, Clr.A) / 255);
		}
	}
}
