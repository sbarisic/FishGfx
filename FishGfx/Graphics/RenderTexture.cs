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
	public class RenderTexture : IDisposable
	{
		static readonly Stack<RenderTexture> LegacyStack = new Stack<RenderTexture>();
		static Stack<RenderTexture> RTStack => GraphicsContext.CurrentOrNull?.RenderTargets ?? LegacyStack;

		TextureFormat ColorFmt = TextureFormat.RGBA8Unorm;
		TextureDimension TextureTgt = TextureDimension.Texture2D;

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

		public GraphicsContext Owner { get; }
		public bool IsDisposed { get; private set; }

		public RenderTexture(
			int W,
			int H,
			int MSAASamples = 0,
			bool IsGBuffer = false,
			bool CreateColor = true,
			bool CreateDepthStencil = true
		)
		{
			if (W <= 0 || H <= 0) throw new ArgumentOutOfRangeException(nameof(W), "Render-texture dimensions must be positive.");
			if (MSAASamples < 0 || MSAASamples == 1) throw new ArgumentOutOfRangeException(nameof(MSAASamples), "Use zero samples to disable MSAA or at least two samples to enable it.");
			if (!CreateColor && !CreateDepthStencil && !IsGBuffer) throw new ArgumentException("A render texture must create at least one attachment.");
			Owner = GraphicsContext.Current;
			if (MSAASamples > Owner.Capabilities.MaximumSamples)
				throw new ArgumentOutOfRangeException(nameof(MSAASamples), $"Sample count exceeds the context limit of {Owner.Capabilities.MaximumSamples}.");
			Width = W;
			Height = H;

			this.IsGBuffer = IsGBuffer;
			Multisamples = MSAASamples;
			if (MSAASamples != 0)
			try
			{
				Framebuffer = new Framebuffer();
				if (MSAASamples != 0)
					TextureTgt = TextureDimension.Texture2DMultisample;

				if (IsGBuffer)
				{
					Color = CreateAttachment(ColorFmt, false);
					Framebuffer.AttachColor(Color, 0);
					DrawBuffers.Add(0);

					Position = CreateAttachment(TextureFormat.RGBA32Float, false);
					Framebuffer.AttachColor(Position, 1);
					DrawBuffers.Add(1);

					Normal = CreateAttachment(TextureFormat.RGBA32Float, false);
					Framebuffer.AttachColor(Normal, 2);
					DrawBuffers.Add(2);

					DepthStencil = CreateAttachment(TextureFormat.Depth24Stencil8, true);
					Framebuffer.AttachDepth(DepthStencil, true);
				}
				else
				{
					if (CreateColor)
					{
						Color = CreateAttachment(ColorFmt, false);
						Framebuffer.AttachColor(Color, 0);
						DrawBuffers.Add(0);
					}

					if (CreateDepthStencil)
					{
						DepthStencil = CreateAttachment(TextureFormat.Depth24Stencil8, true);
						Framebuffer.AttachDepth(DepthStencil, true);
					}
				}

				Framebuffer.DrawBuffers(DrawBuffers.ToArray());
			}
			catch
			{
				DisposeResources();
				throw;
			}
		}

		public Texture CreateNewColorAttachment(int Idx, TextureFormat? Fmt = null)
		{
			if (IsDisposed) throw new ObjectDisposedException(nameof(RenderTexture));
			Owner.EnsureCurrent();
			if (Idx < 0) throw new ArgumentOutOfRangeException(nameof(Idx));
			if (DrawBuffers.Contains(Idx))
				throw new InvalidOperationException(string.Format("Color attachment {0} already exists", Idx));

			Texture Tex = CreateAttachment(Fmt ?? ColorFmt, false);
			try
			{
				Framebuffer.AttachColor(Tex, Idx);
				DrawBuffers.Add(Idx);
				Framebuffer.DrawBuffers(DrawBuffers.ToArray());
				return Tex;
			}
			catch { Tex.Dispose(); throw; }
		}

		private Texture CreateAttachment(TextureFormat format, bool depth)
		{
			TextureUsageFlags usage = TextureUsageFlags.Sampled |
				(depth ? TextureUsageFlags.DepthStencilAttachment : TextureUsageFlags.ColorAttachment);
			if (Multisamples == 0) usage |= TextureUsageFlags.TransferSource | TextureUsageFlags.TransferDestination;
			return GraphicsContext.Current.CreateTexture(new TextureDescriptor(
				Width, Height, format, usage, TextureTgt, samples: Multisamples == 0 ? 1 : Multisamples,
				fixedSampleLocations: Multisamples != 0
			));
		}

		internal void BindForPass() => Bind();
		internal void UnbindForPass() => Unbind();

		protected void Bind()
		{
			Framebuffer.DrawBuffers(DrawBuffers.ToArray());
			Framebuffer.Bind();
		}

		protected void Unbind()
		{
			Framebuffer.Unbind();
		}

		public void Push()
		{
			Bind();
			RTStack.Push(this);
		}

		public void Pop()
		{
			if (RTStack.Count == 0 || !ReferenceEquals(RTStack.Peek(), this))
				throw new InvalidOperationException("Render textures must be popped in reverse order.");
			RTStack.Pop();
			Unbind();
			if (RTStack.Count > 0)
				RTStack.Peek().Bind();
		}

		public void Dispose()
		{
			if (IsDisposed)
				return;
			if (RTStack.Contains(this))
				throw new InvalidOperationException("A bound render texture cannot be disposed.");
			IsDisposed = true;
			DisposeResources();
		}

		private void DisposeResources()
		{
			HashSet<GraphicsObject> resources = new HashSet<GraphicsObject>();
			if (Color != null) resources.Add(Color);
			if (Position != null) resources.Add(Position);
			if (Normal != null) resources.Add(Normal);
			if (DepthStencil != null) resources.Add(DepthStencil);
			if (Framebuffer != null) resources.Add(Framebuffer);
			foreach (GraphicsObject resource in resources) resource.Dispose();
		}
	}
}
