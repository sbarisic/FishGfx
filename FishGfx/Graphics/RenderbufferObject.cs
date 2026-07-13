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
		public int Width { get; private set; }
		public int Height { get; private set; }
		public int Samples { get; private set; }
		public RenderbufferFormat StorageFormat { get; private set; }
		public Renderbuffer()
		{
			if (Internal_OpenGL.Is45OrAbove)
				ID = Internal_OpenGL.GL.CreateRenderbuffer();
			else
				ID = Internal_OpenGL.GL.GenRenderbuffer();
		}

		public void Storage(RenderbufferFormat Fmt, int W, int H, int Samples = 0)
		{
			EnsureCurrentOwner();
			if (W <= 0 || H <= 0) throw new ArgumentOutOfRangeException(nameof(W), "Renderbuffer dimensions must be positive.");
			if (!Enum.IsDefined(Fmt)) throw new ArgumentOutOfRangeException(nameof(Fmt));
			if (Samples == 1 || Samples < 0) throw new ArgumentOutOfRangeException(nameof(Samples), "Use zero samples for single-sampled storage, or at least two samples for multisampled storage.");
			if (Samples > GraphicsContext.Current.Capabilities.MaximumSamples)
				throw new ArgumentOutOfRangeException(nameof(Samples), $"Sample count exceeds the context limit of {GraphicsContext.Current.Capabilities.MaximumSamples}.");
			Width = W;
			Height = H;
			this.Samples = Samples;
			StorageFormat = Fmt;

			if (Internal_OpenGL.Is45OrAbove)
			{
				if (Samples > 0) Internal_OpenGL.GL.NamedRenderbufferStorageMultisample(ID, Samples, (InternalFormat)Fmt, W, H);
				else Internal_OpenGL.GL.NamedRenderbufferStorage(ID, (InternalFormat)Fmt, W, H);
			}
			else
			{
				Internal_OpenGL.GL.GetInteger((GetPName)0x8CA7, out int previous);
				try
				{
					Internal_OpenGL.GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, ID);
					if (Samples > 0) Internal_OpenGL.GL.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, (uint)Samples, (GLEnum)Fmt, (uint)W, (uint)H);
					else Internal_OpenGL.GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, (GLEnum)Fmt, (uint)W, (uint)H);
				}
				finally
				{
					Internal_OpenGL.GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, (uint)previous);
				}
			}
		}

		public override void Bind()
		{
			EnsureCurrentOwner();
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
