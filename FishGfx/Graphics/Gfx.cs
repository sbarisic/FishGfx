using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenGL;

using System.Numerics;

namespace FishGfx.Graphics {
	public static class Gfx {
		public static void Clear() {
			Gl.ClearColor(69 / 255.0f, 112 / 255.0f, 56 / 255.0f, 1.0f);
			Gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
		}

		public static void Line(Vertex2 Start, Vertex2 End) {
			// TODO
		}

		public static void Line(CommandList List, Vertex2 Start, Vertex2 End) {
			// TODO
		}
	}
}
