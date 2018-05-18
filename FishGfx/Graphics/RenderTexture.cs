using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenGL;
using System.Drawing;
using System.Drawing.Imaging;
using FishGfx;

namespace FishGfx.Graphics {
	public class RenderTexture {
		InternalFormat ColorFmt = InternalFormat.Rgba16f;

		public Framebuffer Framebuffer { get; private set; }
		public Texture Color { get; private set; }
		public Texture Depth { get; private set; }

		public int Width { get; private set; }
		public int Height { get; private set; }

		public RenderTexture(int W, int H) {
			Width = W;
			Height = H;

			// TODO: Internal format things
			Color = new Texture(W, H, TextureTarget.Texture2d, 1, ColorFmt);
			Depth = new Texture(W, H, TextureTarget.Texture2d, 1, InternalFormat.Depth24Stencil8);

			Framebuffer = new Framebuffer();
			Framebuffer.AttachColorTexture(Color);
			Framebuffer.AttachDepthTexture(Depth);
		}

		public Texture CreateNewColorAttachment(int Idx) {
			Texture Tex = new Texture(Width, Height, TextureTarget.Texture2d, 1, ColorFmt);
			Framebuffer.AttachColorTexture(Tex, Idx);
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
