using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Silk.NET.OpenGL;

namespace FishGfx.Graphics
{
	public unsafe class Framebuffer : GraphicsObject
	{
		Dictionary<FramebufferAttachment, Texture> Textures;
		Dictionary<FramebufferAttachment, Renderbuffer> Renderbuffers;

		int AttachmentCount
		{
			get { return Textures.Count + Renderbuffers.Count; }
		}

		FramebufferTarget Target;

		public bool Multisampled { get; private set; }

		public Framebuffer()
		{
			if (Internal_OpenGL.Is45OrAbove)
				ID = Internal_OpenGL.GL.CreateFramebuffer();
			else
				ID = Internal_OpenGL.GL.GenFramebuffer();

			Textures = new Dictionary<FramebufferAttachment, Texture>();
			Renderbuffers = new Dictionary<FramebufferAttachment, Renderbuffer>();
		}

		void AddAttachment(FramebufferAttachment Attachment, Texture Tex)
		{
			EnsureCurrentOwner();
			if (Tex == null) throw new ArgumentNullException(nameof(Tex));
			Tex.EnsureOwner(GraphicsContext.Current);
			bool depthAttachment = Attachment == FramebufferAttachment.DepthAttachment || Attachment == FramebufferAttachment.DepthStencilAttachment;
			TextureUsageFlags requiredUsage = depthAttachment ? TextureUsageFlags.DepthStencilAttachment : TextureUsageFlags.ColorAttachment;
			if ((Tex.Usage & requiredUsage) == 0)
				throw new InvalidOperationException($"The texture is missing {requiredUsage} usage.");
			bool stencilFormat = Tex.Format == TextureFormat.Depth24Stencil8 || Tex.Format == TextureFormat.Depth32FloatStencil8;
			if (Attachment == FramebufferAttachment.DepthStencilAttachment && !stencilFormat)
				throw new InvalidOperationException("A depth-stencil attachment requires a stencil-capable texture format.");
			ValidateAttachmentCompatibility(Attachment, Tex.Width, Tex.Height, Tex.Multisamples);

			Renderbuffers.Remove(Attachment);
			Textures[Attachment] = Tex;

			if (Internal_OpenGL.Is45OrAbove)
				Internal_OpenGL.GL.NamedFramebufferTexture(ID, Attachment, Tex.ID, 0);
			else
			{
				WithBoundFramebuffer(() => Internal_OpenGL.GL.FramebufferTexture(FramebufferTarget.Framebuffer, Attachment, Tex.ID, 0));
			}
		}

		void AddAttachment(FramebufferAttachment Attachment, Renderbuffer RBuf)
		{
			EnsureCurrentOwner();
			if (RBuf == null) throw new ArgumentNullException(nameof(RBuf));
			RBuf.EnsureOwner(GraphicsContext.Current);
			if (RBuf.Width <= 0 || RBuf.Height <= 0) throw new InvalidOperationException("The renderbuffer must have storage before it is attached.");
			bool depthAttachment = Attachment == FramebufferAttachment.DepthAttachment || Attachment == FramebufferAttachment.DepthStencilAttachment;
			bool depthFormat = RBuf.StorageFormat == RenderbufferFormat.DepthComponent16 || RBuf.StorageFormat == RenderbufferFormat.DepthComponent24 ||
				RBuf.StorageFormat == RenderbufferFormat.DepthComponent32 || RBuf.StorageFormat == RenderbufferFormat.Depth24Stencil8;
			if (depthAttachment != depthFormat) throw new InvalidOperationException("The renderbuffer format does not match the framebuffer attachment.");
			ValidateAttachmentCompatibility(Attachment, RBuf.Width, RBuf.Height, RBuf.Samples);
			Textures.Remove(Attachment);
			Renderbuffers[Attachment] = RBuf;

			if (Internal_OpenGL.Is45OrAbove)
				Internal_OpenGL.GL.NamedFramebufferRenderbuffer(ID, Attachment, RenderbufferTarget.Renderbuffer, RBuf.ID);
			else
			{
				WithBoundFramebuffer(() => Internal_OpenGL.GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, Attachment, RenderbufferTarget.Renderbuffer, RBuf.ID));
			}
		}

		void ValidateAttachmentCompatibility(FramebufferAttachment attachment, int width, int height, int samples)
		{
			foreach (KeyValuePair<FramebufferAttachment, Texture> pair in Textures)
			{
				if (pair.Key == attachment) continue;
				if (pair.Value.Width != width || pair.Value.Height != height || pair.Value.Multisamples != samples)
					throw new InvalidOperationException("Every framebuffer attachment must have identical dimensions and sample counts.");
			}
			foreach (KeyValuePair<FramebufferAttachment, Renderbuffer> pair in Renderbuffers)
			{
				if (pair.Key == attachment) continue;
				if (pair.Value.Width != width || pair.Value.Height != height || pair.Value.Samples != samples)
					throw new InvalidOperationException("Every framebuffer attachment must have identical dimensions and sample counts.");
			}
			Multisampled = samples > 0;
		}

		Texture GetTexture(FramebufferAttachment Attachment)
		{
			if (Textures.ContainsKey(Attachment))
				return Textures[Attachment];

			return null;
		}

		public Texture GetColorTexture(int Color = 0)
		{
			return GetTexture(FramebufferAttachment.ColorAttachment0 + Color);
		}

		public Texture GetDepthTexture()
		{
			return GetTexture(FramebufferAttachment.DepthAttachment) ?? GetTexture(FramebufferAttachment.DepthStencilAttachment);
		}

		public void AttachColor(Texture Tex, int Color = 0)
		{
			AddAttachment(FramebufferAttachment.ColorAttachment0 + Color, Tex);
		}

		public void AttachColor(Renderbuffer RBuf, int Color = 0)
		{
			AddAttachment(FramebufferAttachment.ColorAttachment0 + Color, RBuf);
		}
		public void AttachDepth(Texture Tex, bool HasStencil = false)
		{
			if (Tex == null) throw new ArgumentNullException(nameof(Tex));
			bool hasStencil = HasStencil || Tex.Format == TextureFormat.Depth24Stencil8 || Tex.Format == TextureFormat.Depth32FloatStencil8;
			AddAttachment(hasStencil ? FramebufferAttachment.DepthStencilAttachment : FramebufferAttachment.DepthAttachment, Tex);
		}

		public void AttachDepth(Renderbuffer RBuf)
		{
			if (RBuf == null) throw new ArgumentNullException(nameof(RBuf));
			FramebufferAttachment attachment = RBuf.StorageFormat == RenderbufferFormat.Depth24Stencil8 ? FramebufferAttachment.DepthStencilAttachment : FramebufferAttachment.DepthAttachment;
			AddAttachment(attachment, RBuf);
		}

		public void DrawBuffers(params int[] Indices)
		{
			EnsureCurrentOwner();
			if (Indices == null) throw new ArgumentNullException(nameof(Indices));
			if (Indices.Any(index => index < 0)) throw new ArgumentOutOfRangeException(nameof(Indices));
			if (Indices.Distinct().Count() != Indices.Length) throw new ArgumentException("Draw-buffer indices must be unique.", nameof(Indices));
			GLEnum[] Buffers = new GLEnum[Indices.Length];

			for (int i = 0; i < Indices.Length; i++)
				Buffers[i] = (GLEnum)(Indices[i] + (int)FramebufferAttachment.ColorAttachment0);

			if (Internal_OpenGL.Is45OrAbove)
				Internal_OpenGL.GL.NamedFramebufferDrawBuffers(ID, Buffers);
			else
			{
				WithBoundFramebuffer(() => Internal_OpenGL.GL.DrawBuffers(Buffers));
			}
		}

		public void Clear(Color? Color = null, int ColorAttachment = 0, float? Depth = null, int? Stencil = null)
		{
			EnsureCurrentOwner();
			if (ColorAttachment < 0) throw new ArgumentOutOfRangeException(nameof(ColorAttachment));
			if (Internal_OpenGL.Is45OrAbove)
			{
				if (Color != null) Internal_OpenGL.GL.ClearNamedFramebuffer(ID, GLEnum.Color, ColorAttachment, new float[] { Color.Value.R / 255f, Color.Value.G / 255f, Color.Value.B / 255f, Color.Value.A / 255f });
				if (Depth != null) Internal_OpenGL.GL.ClearNamedFramebuffer(ID, GLEnum.Depth, 0, new float[] { Depth.Value });
				if (Stencil != null) Internal_OpenGL.GL.ClearNamedFramebuffer(ID, GLEnum.Stencil, 0, new int[] { Stencil.Value });
				return;
			}

			WithBoundFramebuffer(() =>
			{
				ClearBufferMask mask = 0;
				if (Color != null)
				{
					Internal_OpenGL.GL.DrawBuffer((DrawBufferMode)((int)DrawBufferMode.ColorAttachment0 + ColorAttachment));
					Internal_OpenGL.GL.ClearColor(Color.Value.R / 255f, Color.Value.G / 255f, Color.Value.B / 255f, Color.Value.A / 255f);
					mask |= ClearBufferMask.ColorBufferBit;
				}
				if (Depth != null) { Internal_OpenGL.GL.ClearDepth(Depth.Value); mask |= ClearBufferMask.DepthBufferBit; }
				if (Stencil != null) { Internal_OpenGL.GL.ClearStencil(Stencil.Value); mask |= ClearBufferMask.StencilBufferBit; }
				if (mask != 0) Internal_OpenGL.GL.Clear(mask);
			});
		}

		public void Blit(bool Color, bool Depth, bool Stencil, Framebuffer Destination = null, bool NearestFilter = true)
		{
			EnsureCurrentOwner();
			Destination?.EnsureOwner(GraphicsContext.Current);
			ClearBufferMask mask = 0;
			if (Color) mask |= ClearBufferMask.ColorBufferBit;
			if (Depth) mask |= ClearBufferMask.DepthBufferBit;
			if (Stencil) mask |= ClearBufferMask.StencilBufferBit;
			if (mask == 0) throw new ArgumentException("At least one buffer must be selected for blitting.");
			if (!NearestFilter && (Depth || Stencil)) throw new InvalidOperationException("Depth and stencil blits require nearest filtering.");

			(int sourceWidth, int sourceHeight) = GetAttachmentSize();
			(int destinationWidth, int destinationHeight) = Destination?.GetAttachmentSize() ??
				(GraphicsContext.Current.Backbuffer.Width, GraphicsContext.Current.Backbuffer.Height);
			Internal_OpenGL.GL.GetInteger((GetPName)0x8CAA, out int previousRead);
			Internal_OpenGL.GL.GetInteger((GetPName)0x8CA6, out int previousDraw);
			try
			{
				Internal_OpenGL.GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, ID);
				Internal_OpenGL.GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, Destination?.ID ?? 0);
				Internal_OpenGL.GL.BlitFramebuffer(0, 0, sourceWidth, sourceHeight, 0, 0, destinationWidth, destinationHeight, mask, NearestFilter ? BlitFramebufferFilter.Nearest : BlitFramebufferFilter.Linear);
			}
			finally
			{
				Internal_OpenGL.GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, (uint)previousRead);
				Internal_OpenGL.GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, (uint)previousDraw);
			}
		}

		(int Width, int Height) GetAttachmentSize()
		{
			Texture texture = Textures.Values.FirstOrDefault();
			if (texture != null) return (texture.Width, texture.Height);
			Renderbuffer renderbuffer = Renderbuffers.Values.FirstOrDefault();
			if (renderbuffer != null && renderbuffer.Width > 0 && renderbuffer.Height > 0) return (renderbuffer.Width, renderbuffer.Height);
			throw new InvalidOperationException("The framebuffer has no sized attachments.");
		}

		void BindFramebuffer(FramebufferTarget Target)
		{
			EnsureCurrentOwner();
			Internal_OpenGL.GL.BindFramebuffer(Target, ID);
#if DEBUG
			FramebufferStatus S;

			if (Internal_OpenGL.Is45OrAbove)
				S = (FramebufferStatus)Internal_OpenGL.GL.CheckNamedFramebufferStatus(ID, Target);
			else
				S = (FramebufferStatus)Internal_OpenGL.GL.CheckFramebufferStatus(Target);

			if (S != FramebufferStatus.Complete)
				throw new InvalidOperationException("Incomplete framebuffer " + S);
#endif

			this.Target = Target;
		}

		void WithBoundFramebuffer(Action action)
		{
			Internal_OpenGL.GL.GetInteger((GetPName)0x8CAA, out int previousRead);
			Internal_OpenGL.GL.GetInteger((GetPName)0x8CA6, out int previousDraw);
			try
			{
				Internal_OpenGL.GL.BindFramebuffer(FramebufferTarget.Framebuffer, ID);
				action();
			}
			finally
			{
				Internal_OpenGL.GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, (uint)previousRead);
				Internal_OpenGL.GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, (uint)previousDraw);
			}
		}

		public override void Bind()
		{
			BindFramebuffer(FramebufferTarget.Framebuffer);
		}

		public void BindRead()
		{
			BindFramebuffer(FramebufferTarget.ReadFramebuffer);
		}

		public void BindDraw()
		{
			BindFramebuffer(FramebufferTarget.DrawFramebuffer);
		}

		public override void Unbind()
		{
			Internal_OpenGL.GL.BindFramebuffer(Target, 0);
		}

		public override void GraphicsDispose()
		{
			Internal_OpenGL.GL.DeleteFramebuffers(new uint[] { ID });
		}
	}
}
