using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

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

	public unsafe class VertexArray : GraphicsObject {
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
			if (Internal_OpenGL.Is45OrAbove)
				ID = Internal_OpenGL.GL.CreateVertexArray();
			else
				ID = Internal_OpenGL.GL.GenVertexArray();

			PrimitiveType = PrimitiveType.Triangles;
			BufferObjects = new List<BufferObject>();
		}

		public override void Bind() {
			Internal_OpenGL.GL.BindVertexArray(ID);
		}

		public override void Unbind() {
			Internal_OpenGL.GL.BindVertexArray(0);
		}

		public void Draw(int First, int Count) {
			if (Count == 0)
				return;

			Bind();
			Internal_OpenGL.GL.DrawArrays((Silk.NET.OpenGL.PrimitiveType)PrimitiveType, First, (uint)Count);
			Unbind();
		}

		public void DrawElements(int Offset = 0, int Count = -1, IndexElementType ElementType = IndexElementType.UnsignedShort) {
			if (ElementBuffer == null)
				throw new Exception("Use Draw instead");

			if (Count == -1)
				Count = ElementBuffer.ElementCount;

			int ElementSize = 1;

			switch (ElementType) {
				case IndexElementType.UnsignedByte:
					ElementSize = sizeof(byte);
					break;
				case IndexElementType.UnsignedShort:
					ElementSize = sizeof(ushort);
					break;
				case IndexElementType.UnsignedInt:
					ElementSize = sizeof(uint);
					break;
				default:
					throw new Exception("Unknown IndexElementType " + ElementType);
			}

			Bind();
			Internal_OpenGL.GL.DrawElements((Silk.NET.OpenGL.PrimitiveType)PrimitiveType, (uint)Count, (Silk.NET.OpenGL.DrawElementsType)ElementType, (void*)(Offset * ElementSize));
			Unbind();
		}

		public uint BindVertexBuffer(BufferObject Obj, int BindingIndex = -1, int Offset = 0, int Stride = 3 * sizeof(float)) {
			if (!BufferObjects.Contains(Obj))
				BufferObjects.Add(Obj);

			if (BindingIndex == -1)
				BindingIndex = FreeBindingIndex++;

			if (Obj != null) {
				if (Internal_OpenGL.Is45OrAbove)
					Internal_OpenGL.GL.VertexArrayVertexBuffer(ID, (uint)BindingIndex, Obj.ID, Offset, (uint)Stride);
				else {
					Bind();
					Obj.Bind();
					Internal_OpenGL.GL.BindVertexBuffer((uint)BindingIndex, Obj.ID, Offset, (uint)Stride);
					Unbind();
				}
			}

			return (uint)BindingIndex;
		}

		public void BindElementBuffer(BufferObject Obj) {
			ElementBuffer = Obj;

			uint ObjID = Obj != null ? Obj.ID : 0;

			if (Internal_OpenGL.Is45OrAbove)
				Internal_OpenGL.GL.VertexArrayElementBuffer(ID, ObjID);
			else {
				Bind();
				Internal_OpenGL.GL.BindBuffer(BufferTargetARB.ElementArrayBuffer, ObjID);
				Unbind();
			}
		}

		public void AttribEnable(uint AttribIdx, bool Enable = true) {
			if (Enable) {
				if (Internal_OpenGL.Is45OrAbove)
					Internal_OpenGL.GL.EnableVertexArrayAttrib(ID, AttribIdx);
				else {
					Bind();
					Internal_OpenGL.GL.EnableVertexAttribArray(AttribIdx);
					Unbind();
				}
			} else {
				if (Internal_OpenGL.Is45OrAbove)
					Internal_OpenGL.GL.DisableVertexArrayAttrib(ID, AttribIdx);
				else {
					Bind();
					Internal_OpenGL.GL.DisableVertexAttribArray(AttribIdx);
					Unbind();
				}
			}
		}

		public void AttribFormat(uint AttribIdx, int Size = 3, VertexElementType AttribType = VertexElementType.Float, bool Normalized = false, uint RelativeOffset = 0) {
			if (Internal_OpenGL.Is45OrAbove)
				Internal_OpenGL.GL.VertexArrayAttribFormat(ID, AttribIdx, Size, (Silk.NET.OpenGL.VertexAttribType)AttribType, Normalized, RelativeOffset);
			else {
				Bind();
				Internal_OpenGL.GL.VertexAttribFormat(AttribIdx, Size, (GLEnum)AttribType, Normalized, RelativeOffset);
				Unbind();
			}
		}

		public void AttribBinding(uint AttribIdx, uint BindingIdx) {
			AttribEnable(AttribIdx);

			if (Internal_OpenGL.Is45OrAbove)
				Internal_OpenGL.GL.VertexArrayAttribBinding(ID, AttribIdx, BindingIdx);
			else {
				Bind();
				Internal_OpenGL.GL.VertexAttribBinding(AttribIdx, BindingIdx);
				Unbind();
			}
		}

		public override void GraphicsDispose() {
			Internal_OpenGL.GL.DeleteVertexArrays(new uint[] { ID });
		}

		public static void VertexAttrib(uint Attrib, Vector4 Vec) {
			Internal_OpenGL.GL.VertexAttrib4(Attrib, Vec.X, Vec.Y, Vec.Z, Vec.W);
		}

		public static void VertexAttrib(uint Attrib, Color Clr) {
			VertexAttrib(Attrib, new Vector4(Clr.R, Clr.G, Clr.B, Clr.A) / 255);
		}
	}
}
