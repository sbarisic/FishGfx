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

		public BufferObject() {
			ID = Gl.CreateBuffer();
		}

		public void SetData(uint Size, IntPtr Data, BufferUsage Usage = BufferUsage.DynamicDraw) {
			this.Size = (int)Size;

			Gl.NamedBufferData(ID, Size, Data, (OpenGL.BufferUsage)Usage);
		}

		public void SetData<T>(T[] Data, BufferUsage Usage = BufferUsage.DynamicDraw) where T : struct {
			if (Data == null)
				return;

			ElementCount = Data.Length;
			GCHandle PinHandle = GCHandle.Alloc(Data, GCHandleType.Pinned);
			SetData((uint)(Marshal.SizeOf(typeof(T)) * Data.Length), PinHandle.AddrOfPinnedObject(), Usage);
			PinHandle.Free();
		}

		public override void GraphicsDispose() {
			Gl.UnmapNamedBuffer(ID);
			Gl.DeleteBuffers(new uint[] { ID });
		}
	}
}
