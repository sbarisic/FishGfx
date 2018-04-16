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
			Gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
		}

		public static void Line(Vertex2 Start, Vertex2 End) {

		}
	}
}
