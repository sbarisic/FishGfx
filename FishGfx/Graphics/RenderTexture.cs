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
		static Stack<RenderTexture> RTStack = new Stack<RenderTexture>();

		TextureInternalFmt ColorFmt = TextureInternalFmt.Rgba8;
		TextureTarget TextureTgt = TextureTarget.Texture2d;

		public bool IsGBuffer { get; private set; }

		public int Multisamples { get; private set; }
		public Framebuffer Framebuffer { get; private set; }
		public Texture Color { get; private set; }
		//public Texture Depth { get; private set; }

		//Renderbuffer DepthStencil;

		public int Width { get; private set; }
		public int Height { get; private set; }

		public Texture Position { get; private set; }
		public Texture Normal { get; private set; }
		public Texture DepthStencil { get; private set; }

		List<int> DrawBuffers = new List<int>();

		public RenderTexture(int W, int H, int MSAASamples = 0, bool IsGBuffer = false, bool CreateColor = true, bool CreateDepthStencil = true) {
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
				DrawBuffers.Add(0);

				Position = new Texture(W, H, TextureTgt, 1, TextureInternalFmt.Rgba32f);
				Framebuffer.AttachColor(Position, 1);
				DrawBuffers.Add(1);

				Normal = new Texture(W, H, TextureTgt, 1, TextureInternalFmt.Rgba32f);
				Framebuffer.AttachColor(Normal, 2);
				DrawBuffers.Add(2);

				DepthStencil = new Texture(W, H, TextureTgt, 1, TextureInternalFmt.Depth24Stencil8);
				Framebuffer.AttachDepth(DepthStencil, true);
			} else {
				if (CreateColor) {
					Color = new Texture(W, H, TextureTgt, 1, ColorFmt, MSAASamples, Multisamples != 0 ? true : false);
					Framebuffer.AttachColor(Color, 0);
					DrawBuffers.Add(0);
				}

				if (CreateDepthStencil) {
					//DepthStencil = new Renderbuffer();
					//DepthStencil.Storage(InternalFormat.Depth24Stencil8, W, H, MSAASamples);
					DepthStencil = new Texture(W, H, TextureTgt, 1, TextureInternalFmt.Depth24Stencil8, MSAASamples, Multisamples != 0 ? true : false);
					Framebuffer.AttachDepth(DepthStencil, true);
				}
			}

			Framebuffer.DrawBuffers(DrawBuffers.ToArray());
		}

		public Texture CreateNewColorAttachment(int Idx, TextureInternalFmt? Fmt = null) {
			if (DrawBuffers.Contains(Idx))
				throw new Exception(string.Format("Color attachment {0} already exists", Idx));

			Texture Tex = new Texture(Width, Height, TextureTgt, 1, Fmt ?? ColorFmt, Multisamples, Multisamples != 0 ? true : false);
			Framebuffer.AttachColor(Tex, Idx);
			DrawBuffers.Add(Idx);
			Framebuffer.DrawBuffers(DrawBuffers.ToArray());
			return Tex;
		}

		protected void Bind() {
			Framebuffer.DrawBuffers(DrawBuffers.ToArray());
			Framebuffer.Bind();
		}

		protected void Unbind() {
			Framebuffer.Unbind();
		}

		public void Push() {
			Bind();
			RTStack.Push(this);
		}

		public void Pop() {
			RTStack.Pop();
			Unbind();

			if (RTStack.Count > 0)
				RTStack.Peek().Bind();
		}
	}
}
