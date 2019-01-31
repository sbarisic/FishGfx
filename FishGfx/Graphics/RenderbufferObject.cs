using FishGfx;
using OpenGL;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FishGfx.Graphics {
	public class Renderbuffer : GraphicsObject {
		public Renderbuffer() {
			if (Internal_OpenGL.Is45OrAbove)
				ID = Gl.CreateRenderbuffer();
			else
				ID = Gl.GenRenderbuffer();
		}

		public void Storage(InternalFormat Fmt, int W, int H, int Samples = 0) {
			Bind();

			if (Samples > 0)
				Gl.NamedRenderbufferStorageMultisample(ID, Samples, Fmt, W, H);
			else
				Gl.NamedRenderbufferStorage(ID, Fmt, W, H);

			Unbind();
		}

		public override void Bind() {
			Gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, ID);
		}

		public override void Unbind() {
			Gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);
		}

		public override void GraphicsDispose() {
			Gl.DeleteRenderbuffers(ID);
		}
	}
}
