using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenGL;
using System.Numerics;
using System.Runtime.InteropServices;

namespace FishGfx.Graphics {
	public enum BufferUsage {
		StreamDraw = 35040,
		StreamRead = 35041,
		StreamCopy = 35042,
		StaticDraw = 35044,
		StaticRead = 35045,
		StaticCopy = 35046,
		DynamicDraw = 35048,
		DynamicRead = 35049,
		DynamicCopy = 35050
	}

	public unsafe class BufferObject : GraphicsObject {
		public int Size { get; private set; }
		public int ElementCount { get; private set; }

		BufferTarget Target;

		public BufferObject() {
			if (Internal_OpenGL.Is45OrAbove)
				ID = Gl.CreateBuffer();
			else {
				ID = Gl.GenBuffer();
				Target = BufferTarget.ArrayBuffer;
			}
		}

		public override void Bind() {
			if (Internal_OpenGL.Is45OrAbove)
				throw new Exception("Bind can only be used in non OpenGL 4.5 context");

			Gl.BindBuffer(Target, ID);
		}

		public override void Unbind() {
			if (Internal_OpenGL.Is45OrAbove)
				throw new Exception("Bind can only be used in non OpenGL 4.5 context");

			Gl.BindBuffer(Target, 0);
		}

		public void SetData(uint Size, IntPtr Data, int ElementCount, BufferUsage Usage = BufferUsage.DynamicDraw) {
			this.Size = (int)Size;

			this.ElementCount = ElementCount;

			if (Internal_OpenGL.Is45OrAbove) {
				Gl.InvalidateBufferData(ID);
				//Gl.NamedBufferData(ID, Size, IntPtr.Zero, (OpenGL.BufferUsage)Usage);
				Gl.NamedBufferData(ID, Size, Data, (OpenGL.BufferUsage)Usage);
			} else {
				Bind();
				Gl.BufferData(Target, Size, IntPtr.Zero, (OpenGL.BufferUsage)Usage);
				Gl.BufferData(Target, Size, Data, (OpenGL.BufferUsage)Usage);
				Unbind();
			}
		}

		public void SetData<T>(T[] Data, BufferUsage Usage = BufferUsage.DynamicDraw) where T : struct {
			if (Data == null)
				return;

			GCHandle PinHandle = GCHandle.Alloc(Data, GCHandleType.Pinned);
			SetData((uint)(Marshal.SizeOf(typeof(T)) * Data.Length), PinHandle.AddrOfPinnedObject(), Data.Length, Usage);
			PinHandle.Free();
		}

		public override void GraphicsDispose() {
			// TODO
			/*if (Internal_OpenGL.Is45OrAbove)
				Gl.UnmapNamedBuffer(ID);
			else
				Gl.UnmapBuffer(Target);*/

			Gl.DeleteBuffers(new uint[] { ID });
		}
	}
}
