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
	public class RenderTexture {
		InternalFormat ColorFmt = InternalFormat.Rgba8;
		TextureTarget TextureTgt = TextureTarget.Texture2d;

		public bool IsGBuffer { get; private set; }

		public int Multisamples { get; private set; }
		public Framebuffer Framebuffer { get; private set; }
		public Texture Color { get; private set; }
		//public Texture Depth { get; private set; }

		Renderbuffer DepthStencil;

		public int Width { get; private set; }
		public int Height { get; private set; }

		public Texture Position { get; private set; }
		public Texture Normal { get; private set; }
		public Texture Depth { get; private set; }

		public RenderTexture(int W, int H, int MSAASamples = 0, bool IsGBuffer = false) {
			Width = W;
			Height = H;

			this.IsGBuffer = IsGBuffer;
			Framebuffer = new Framebuffer();

			Multisamples = MSAASamples;
			if (MSAASamples != 0)
				TextureTgt = TextureTarget.Texture2dMultisample;

			if (IsGBuffer) {
				Color = new Texture(W, H, TextureTgt, 1, ColorFmt);
				Framebuffer.AttachColor(Color, 0);

				Position = new Texture(W, H, TextureTgt, 1, InternalFormat.Rgba32f);
				Framebuffer.AttachColor(Position, 1);

				Normal = new Texture(W, H, TextureTgt, 1, InternalFormat.Rgba32f);
				Framebuffer.AttachColor(Normal, 2);

				Depth = new Texture(W, H, TextureTgt, 1, InternalFormat.Depth24Stencil8);
				Framebuffer.AttachDepth(Depth);

				Framebuffer.DrawBuffers(0, 1, 2);
			} else {
				// TODO: Internal format things
				Color = new Texture(W, H, TextureTgt, 1, ColorFmt, MSAASamples, Multisamples != 0 ? true : false);
				Framebuffer.AttachColor(Color);

				DepthStencil = new Renderbuffer();
				DepthStencil.Storage(InternalFormat.Depth24Stencil8, W, H, MSAASamples);
				Framebuffer.AttachDepth(DepthStencil);

				//Framebuffer.DrawBuffers(0);
			}
		}

		public Texture CreateNewColorAttachment(int Idx) {
			Texture Tex = new Texture(Width, Height, TextureTgt, 1, ColorFmt, Multisamples, Multisamples != 0 ? true : false);
			Framebuffer.AttachColor(Tex, Idx);
			return Tex;
		}

		public void Bind(params int[] DrawBuffers) {
			Framebuffer.DrawBuffers(DrawBuffers);
			Framebuffer.Bind();
		}

		public void Bind() {
			Framebuffer.Bind();
		}

		public void Unbind() {
			Framebuffer.Unbind();
		}
	}
}
