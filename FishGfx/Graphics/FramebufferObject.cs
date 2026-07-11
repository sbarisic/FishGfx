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
		// TODO: Unify
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
			if (AttachmentCount == 0)
				Multisampled = Tex.Multisampled;
			else if (Textures.Count > 0 && Tex.Multisampled != Multisampled)
				throw new InvalidOperationException("Every attachment has to have the same multisampling");

			if (Textures.ContainsKey(Attachment))
				Textures.Remove(Attachment);

			Textures.Add(Attachment, Tex);

			if (Internal_OpenGL.Is45OrAbove)
				Internal_OpenGL.GL.NamedFramebufferTexture(ID, Attachment, Tex.ID, 0);
			else
			{
				Bind();
				Internal_OpenGL.GL.FramebufferTexture(FramebufferTarget.Framebuffer, Attachment, Tex.ID, 0);
				Unbind();
			}
		}

		void AddAttachment(FramebufferAttachment Attachment, Renderbuffer RBuf)
		{
			// TODO: Check MSAA

			if (Internal_OpenGL.Is45OrAbove)
				Internal_OpenGL.GL.NamedFramebufferRenderbuffer(
					ID,
					Attachment,
					RenderbufferTarget.Renderbuffer,
					RBuf.ID
				);
			else
			{
				Bind();
				Internal_OpenGL.GL.FramebufferRenderbuffer(
					FramebufferTarget.Framebuffer,
					Attachment,
					RenderbufferTarget.Renderbuffer,
					RBuf.ID
				);
				Unbind();
			}
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
			return GetTexture(FramebufferAttachment.DepthAttachment);
		}

		public void AttachColor(Texture Tex, int Color = 0)
		{
			AddAttachment(FramebufferAttachment.ColorAttachment0 + Color, Tex);
		}

		public void AttachColor(Renderbuffer RBuf, int Color = 0)
		{
			AddAttachment(FramebufferAttachment.ColorAttachment0 + Color, RBuf);
		}

		// TODO: Automatically detect stencil
		public void AttachDepth(Texture Tex, bool HasStencil = false)
		{
			FramebufferAttachment Attachment = FramebufferAttachment.DepthAttachment;

			if (HasStencil)
				Attachment = FramebufferAttachment.DepthStencilAttachment;

			AddAttachment(Attachment, Tex);
		}

		public void AttachDepth(Renderbuffer RBuf)
		{
			AddAttachment(FramebufferAttachment.DepthAttachment, RBuf);
		}

		public void DrawBuffers(params int[] Indices)
		{
			GLEnum[] Buffers = new GLEnum[Indices.Length];
			for (int i = 0; i < Indices.Length; i++)
				Buffers[i] = (GLEnum)(Indices[i] + (int)FramebufferAttachment.ColorAttachment0);

			if (Internal_OpenGL.Is45OrAbove)
				Internal_OpenGL.GL.NamedFramebufferDrawBuffers(ID, Buffers);
			else
			{
				Bind();
				Internal_OpenGL.GL.DrawBuffers(Buffers);
				Unbind();
			}
		}

		public void Clear(Color? Color = null, int ColorAttachment = 0, float? Depth = null, int? Stencil = null)
		{
			if (Color != null)
				Internal_OpenGL.GL.ClearNamedFramebuffer(
					ID,
					GLEnum.Color,
					ColorAttachment,
					new float[]
					{
						Color.Value.R / 255.0f,
						Color.Value.G / 255.0f,
						Color.Value.B / 255.0f,
						Color.Value.A / 255.0f,
					}
				);

			if (Depth != null)
				Internal_OpenGL.GL.ClearNamedFramebuffer(ID, GLEnum.Depth, 0, new float[] { Depth.Value });

			if (Stencil != null)
				Internal_OpenGL.GL.ClearNamedFramebuffer(ID, GLEnum.Stencil, 0, new int[] { Stencil.Value });
		}

		public void Blit(
			bool Color,
			bool Depth,
			bool Stencil,
			Framebuffer Destination = null,
			bool NearestFilter = true
		)
		{
			// TODO: DSA check 'nd shit

			BindRead();
			Internal_OpenGL.GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, Destination?.ID ?? 0);
			//Internal_OpenGL.GL.DrawBuffer(DrawBufferMode.Back);

			ClearBufferMask BlitMask = 0;

			if (Color)
				BlitMask |= ClearBufferMask.ColorBufferBit;

			if (Depth)
				BlitMask |= ClearBufferMask.DepthBufferBit;

			if (Stencil)
				BlitMask |= ClearBufferMask.StencilBufferBit;

			if (BlitMask == 0)
				throw new InvalidOperationException();

			Texture Color0 = Textures.First().Value; //etTexture(FramebufferAttachment.ColorAttachment0);
			BlitFramebufferFilter Filter = NearestFilter ? BlitFramebufferFilter.Nearest : BlitFramebufferFilter.Linear;

			// Internal_OpenGL.GL.BlitNamedFramebuffer(
			//     ID, Target?.ID ?? 0, 0, 0, Color0.Width, Color0.Height,
			//     0, 0, Color0.Width, Color0.Height, ClearMask, Filter);
			Internal_OpenGL.GL.BlitFramebuffer(
				0,
				0,
				Color0.Width,
				Color0.Height,
				0,
				0,
				Color0.Width,
				Color0.Height,
				BlitMask,
				Filter
			);
		}

		void BindFramebuffer(FramebufferTarget Target)
		{
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
			Internal_OpenGL.GL.BindFramebuffer(Target, ID);
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
