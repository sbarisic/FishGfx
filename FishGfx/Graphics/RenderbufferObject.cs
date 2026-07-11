using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FishGfx;
using Silk.NET.OpenGL;

namespace FishGfx.Graphics
{
	public class Renderbuffer : GraphicsObject
	{
		public Renderbuffer()
		{
			if (Internal_OpenGL.Is45OrAbove)
				ID = Internal_OpenGL.GL.CreateRenderbuffer();
			else
				ID = Internal_OpenGL.GL.GenRenderbuffer();
		}

		public void Storage(RenderbufferFormat Fmt, int W, int H, int Samples = 0)
		{
			Bind();

			if (Samples > 0)
				Internal_OpenGL.GL.NamedRenderbufferStorageMultisample(ID, Samples, (InternalFormat)Fmt, W, H);
			else
				Internal_OpenGL.GL.NamedRenderbufferStorage(ID, (InternalFormat)Fmt, W, H);

			Unbind();
		}

		public override void Bind()
		{
			Internal_OpenGL.GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, ID);
		}

		public override void Unbind()
		{
			Internal_OpenGL.GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);
		}

		public override void GraphicsDispose()
		{
			Internal_OpenGL.GL.DeleteRenderbuffers(ID);
		}
	}
}
