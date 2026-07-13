using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Silk.NET.OpenGL;

namespace FishGfx.Graphics
{
	public enum PrimitiveType
	{
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
		Patches = 14,
	}

	public unsafe class VertexArray : GraphicsObject
	{
		public PrimitiveType PrimitiveType;

		GraphicsBuffer ElementBuffer;
		Dictionary<uint, GraphicsBuffer> BufferObjects;
		int FreeBindingIndex = 0;

		public bool HasElementBuffer
		{
			get { return ElementBuffer != null; }
		}

		public VertexArray()
		{
			if (Internal_OpenGL.Is45OrAbove)
				ID = Internal_OpenGL.GL.CreateVertexArray();
			else
				ID = Internal_OpenGL.GL.GenVertexArray();

			PrimitiveType = PrimitiveType.Triangles;
			BufferObjects = new Dictionary<uint, GraphicsBuffer>();
		}

		public override void Bind()
		{
			EnsureCurrentOwner();
			Internal_OpenGL.GL.BindVertexArray(ID);
		}

		public override void Unbind()
		{
			Internal_OpenGL.GL.BindVertexArray(0);
		}

		public void Draw(int First, int Count)
		{
			EnsureCurrentOwner();
			if (First < 0) throw new ArgumentOutOfRangeException(nameof(First));
			if (Count < 0) throw new ArgumentOutOfRangeException(nameof(Count));
			if (Count == 0)
				return;
			ValidateBuffers();
			WithBound(() => Internal_OpenGL.GL.DrawArrays((Silk.NET.OpenGL.PrimitiveType)PrimitiveType, First, (uint)Count));
		}

		public void DrawElements(
			int Offset = 0,
			int Count = -1,
			IndexElementType ElementType = IndexElementType.UnsignedShort
		)
		{
			EnsureCurrentOwner();
			if (ElementBuffer == null)
				throw new InvalidOperationException("No element buffer is bound. Use Draw for non-indexed geometry.");

			if (Offset < 0)
				throw new ArgumentOutOfRangeException(nameof(Offset));
			if (Count < 0)
				throw new ArgumentOutOfRangeException(nameof(Count), "An explicit element count is required.");
			if (Count == 0)
				return;

			int ElementSize = 1;

			switch (ElementType)
			{
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
					throw new ArgumentOutOfRangeException(nameof(ElementType));
			}
			if (checked(((long)Offset + Count) * ElementSize) > ElementBuffer.SizeInBytes)
				throw new ArgumentOutOfRangeException(nameof(Count), "The indexed draw exceeds the element buffer bounds.");
			ValidateBuffers();

			WithBound(() => Internal_OpenGL.GL.DrawElements(
				(Silk.NET.OpenGL.PrimitiveType)PrimitiveType,
				(uint)Count,
				(Silk.NET.OpenGL.DrawElementsType)ElementType,
				(void*)(Offset * ElementSize)
			));
		}

		public uint BindVertexBuffer(
			GraphicsBuffer Obj,
			int BindingIndex = -1,
			int Offset = 0,
			int Stride = 3 * sizeof(float)
		)
		{
			EnsureCurrentOwner();
			Obj?.EnsureOwner(GraphicsContext.Current);
			if (BindingIndex < -1) throw new ArgumentOutOfRangeException(nameof(BindingIndex));
			if (Offset < 0) throw new ArgumentOutOfRangeException(nameof(Offset));
			if (Stride <= 0) throw new ArgumentOutOfRangeException(nameof(Stride));
			if (Obj != null && (Obj.BindFlags & BufferBindFlags.Vertex) == 0)
				throw new InvalidOperationException("The buffer was not created with the Vertex binding flag.");

			if (BindingIndex == -1)
				BindingIndex = FreeBindingIndex++;
			else
				FreeBindingIndex = Math.Max(FreeBindingIndex, checked(BindingIndex + 1));
			uint binding = (uint)BindingIndex;
			if (Obj == null) BufferObjects.Remove(binding);
			else BufferObjects[binding] = Obj;

			uint objectId = Obj?.ID ?? 0;
			if (Internal_OpenGL.Is45OrAbove)
				Internal_OpenGL.GL.VertexArrayVertexBuffer(ID, binding, objectId, Offset, (uint)Stride);
			else
			{
				WithBound(() => Internal_OpenGL.GL.BindVertexBuffer(binding, objectId, Offset, (uint)Stride));
			}

			return binding;
		}

		public void BindElementBuffer(GraphicsBuffer Obj)
		{
			EnsureCurrentOwner();
			Obj?.EnsureOwner(GraphicsContext.Current);
			if (Obj != null && (Obj.BindFlags & BufferBindFlags.Index) == 0)
				throw new InvalidOperationException("The buffer was not created with the Index binding flag.");
			ElementBuffer = Obj;

			uint ObjID = Obj != null ? Obj.ID : 0;

			if (Internal_OpenGL.Is45OrAbove)
				Internal_OpenGL.GL.VertexArrayElementBuffer(ID, ObjID);
			else
				WithBound(() => Internal_OpenGL.GL.BindBuffer(BufferTargetARB.ElementArrayBuffer, ObjID));
		}

		public void AttribEnable(uint AttribIdx, bool Enable = true)
		{
			EnsureCurrentOwner();
			if (Enable)
			{
				if (Internal_OpenGL.Is45OrAbove)
					Internal_OpenGL.GL.EnableVertexArrayAttrib(ID, AttribIdx);
				else WithBound(() => Internal_OpenGL.GL.EnableVertexAttribArray(AttribIdx));
			}
			else
			{
				if (Internal_OpenGL.Is45OrAbove)
					Internal_OpenGL.GL.DisableVertexArrayAttrib(ID, AttribIdx);
				else WithBound(() => Internal_OpenGL.GL.DisableVertexAttribArray(AttribIdx));
			}
		}

		public void AttribFormat(
			uint AttribIdx,
			int Size = 3,
			VertexElementType AttribType = VertexElementType.Float,
			bool Normalized = false,
			uint RelativeOffset = 0
		)
		{
			EnsureCurrentOwner();
			if (Size < 1 || Size > 4) throw new ArgumentOutOfRangeException(nameof(Size));
			if (!Enum.IsDefined(AttribType)) throw new ArgumentOutOfRangeException(nameof(AttribType));
			if (Internal_OpenGL.Is45OrAbove)
				Internal_OpenGL.GL.VertexArrayAttribFormat(
					ID,
					AttribIdx,
					Size,
					(Silk.NET.OpenGL.VertexAttribType)AttribType,
					Normalized,
					RelativeOffset
				);
			else WithBound(() => Internal_OpenGL.GL.VertexAttribFormat(AttribIdx, Size, (GLEnum)AttribType, Normalized, RelativeOffset));
		}

		public void AttribBinding(uint AttribIdx, uint BindingIdx)
		{
			AttribEnable(AttribIdx);

			if (Internal_OpenGL.Is45OrAbove)
				Internal_OpenGL.GL.VertexArrayAttribBinding(ID, AttribIdx, BindingIdx);
			else WithBound(() => Internal_OpenGL.GL.VertexAttribBinding(AttribIdx, BindingIdx));
		}

		private void WithBound(Action action)
		{
			Internal_OpenGL.GL.GetInteger(GetPName.VertexArrayBinding, out int previous);
			Internal_OpenGL.GL.BindVertexArray(ID);
			try { action(); }
			finally { Internal_OpenGL.GL.BindVertexArray((uint)previous); }
		}

		private void ValidateBuffers()
		{
			if (ElementBuffer?.IsDisposed == true)
				throw new ObjectDisposedException(nameof(ElementBuffer));
			foreach (GraphicsBuffer buffer in BufferObjects.Values)
				if (buffer.IsDisposed)
					throw new ObjectDisposedException(nameof(GraphicsBuffer));
		}

		public override void GraphicsDispose()
		{
			Internal_OpenGL.GL.DeleteVertexArrays(new uint[] { ID });
		}

		public static void VertexAttrib(uint Attrib, Vector4 Vec)
		{
			Internal_OpenGL.GL.VertexAttrib4(Attrib, Vec.X, Vec.Y, Vec.Z, Vec.W);
		}

		public static void VertexAttrib(uint Attrib, Color Clr)
		{
			VertexAttrib(Attrib, new Vector4(Clr.R, Clr.G, Clr.B, Clr.A) / 255);
		}
	}
}
