using OpenGL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace FishGfx.Graphics {
	public unsafe class Framebuffer : GraphicsObject {
		// TODO: Unify
		Dictionary<FramebufferAttachment, Texture> Textures;
		Dictionary<FramebufferAttachment, Renderbuffer> Renderbuffers;

		int AttachmentCount {
			get {
				return Textures.Count + Renderbuffers.Count;
			}
		}

		FramebufferTarget Target;

		public bool Multisampled { get; private set; }

		public Framebuffer() {
			if (Internal_OpenGL.Is45OrAbove)
				ID = Gl.CreateFramebuffer();
			else
				ID = Gl.GenFramebuffer();

			Textures = new Dictionary<FramebufferAttachment, Texture>();
			Renderbuffers = new Dictionary<FramebufferAttachment, Renderbuffer>();
		}

		void AddAttachment(FramebufferAttachment Attachment, Texture Tex) {
			if (AttachmentCount == 0)
				Multisampled = Tex.Multisampled;
			else if (Textures.Count > 0 && Tex.Multisampled != Multisampled)
				throw new InvalidOperationException("Every attachment has to have the same multisampling");

			if (Textures.ContainsKey(Attachment))
				Textures.Remove(Attachment);

			Textures.Add(Attachment, Tex);

			if (Internal_OpenGL.Is45OrAbove)
				Gl.NamedFramebufferTexture(ID, Attachment, Tex.ID, 0);
			else {
				Bind();
				Gl.FramebufferTexture(FramebufferTarget.Framebuffer, Attachment, Tex.ID, 0);
				Unbind();
			}
		}

		void AddAttachment(FramebufferAttachment Attachment, Renderbuffer RBuf) {
			// TODO: Check MSAA

			if (Internal_OpenGL.Is45OrAbove)
				Gl.NamedFramebufferRenderbuffer(ID, Attachment, RenderbufferTarget.Renderbuffer, RBuf.ID);
			else {
				Bind();
				Gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, Attachment, RenderbufferTarget.Renderbuffer, RBuf.ID);
				Unbind();
			}
		}

		Texture GetTexture(FramebufferAttachment Attachment) {
			if (Textures.ContainsKey(Attachment))
				return Textures[Attachment];

			return null;
		}

		public Texture GetColorTexture(int Color = 0) {
			return GetTexture(FramebufferAttachment.ColorAttachment0 + Color);
		}

		public Texture GetDepthTexture() {
			return GetTexture(FramebufferAttachment.DepthAttachment);
		}

		public void AttachColor(Texture Tex, int Color = 0) {
			AddAttachment(FramebufferAttachment.ColorAttachment0 + Color, Tex);
		}

		public void AttachColor(Renderbuffer RBuf, int Color = 0) {
			AddAttachment(FramebufferAttachment.ColorAttachment0 + Color, RBuf);
		}

		public void AttachDepth(Texture Tex) {
			AddAttachment(FramebufferAttachment.DepthAttachment, Tex);
		}

		public void AttachDepth(Renderbuffer RBuf) {
			AddAttachment(FramebufferAttachment.DepthAttachment, RBuf);
		}

		public void DrawBuffers(params int[] Indices) {
			for (int i = 0; i < Indices.Length; i++)
				Indices[i] += (int)FramebufferAttachment.ColorAttachment0;

			if (Internal_OpenGL.Is45OrAbove)
				Gl.NamedFramebufferDrawBuffers(ID, Indices.Length, Indices);
			else {
				Bind();
				Gl.DrawBuffers(Indices);
				Unbind();
			}
		}

		public void Clear(Color? Color = null, int ColorAttachment = 0, float? Depth = null, int? Stencil = null) {
			if (Color != null)
				Gl.ClearNamedFramebuffer(ID, OpenGL.Buffer.Color, ColorAttachment, new float[] {
					Color.Value.R / 255.0f, Color.Value.G / 255.0f, Color.Value.B / 255.0f, Color.Value.A / 255.0f });

			if (Depth != null)
				Gl.ClearNamedFramebuffer(ID, OpenGL.Buffer.Depth, 0, new float[] { Depth.Value });

			if (Stencil != null)
				Gl.ClearNamedFramebuffer(ID, OpenGL.Buffer.Stencil, 0, new int[] { Stencil.Value });
		}

		public void Blit(bool NearestFilter = true, bool ClearStencil = false) {
			// TODO: DSA check 'nd shit

			BindRead();
			Gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, 0);
			//Gl.DrawBuffer(DrawBufferMode.Back);

			ClearBufferMask ClearMask = ClearBufferMask.DepthBufferBit;
			if (ClearStencil)
				ClearMask |= ClearBufferMask.StencilBufferBit;

			Texture Color0 = GetTexture(FramebufferAttachment.ColorAttachment0);
			BlitFramebufferFilter Filter = NearestFilter ? BlitFramebufferFilter.Nearest : BlitFramebufferFilter.Linear;
			//Gl.BlitNamedFramebuffer(ID, Target?.ID ?? 0, 0, 0, Color0.Width, Color0.Height, 0, 0, Color0.Width, Color0.Height, ClearMask, Filter);

			Gl.BlitFramebuffer(0, 0, Color0.Width, Color0.Height, 0, 0, Color0.Width, Color0.Height, ClearMask, Filter);
		}

		void BindFramebuffer(FramebufferTarget Target) {
#if DEBUG
			FramebufferStatus S;

			if (Internal_OpenGL.Is45OrAbove)
				S = Gl.CheckNamedFramebufferStatus(ID, Target);
			else
				S = Gl.CheckFramebufferStatus(Target);


			if (S != FramebufferStatus.FramebufferComplete)
				throw new InvalidOperationException("Incomplete framebuffer " + S);
#endif

			this.Target = Target;
			Gl.BindFramebuffer(Target, ID);
		}

		public override void Bind() {
			BindFramebuffer(FramebufferTarget.Framebuffer);
		}

		public void BindRead() {
			BindFramebuffer(FramebufferTarget.ReadFramebuffer);
		}

		public void BindDraw() {
			BindFramebuffer(FramebufferTarget.DrawFramebuffer);
		}

		public override void Unbind() {
			Gl.BindFramebuffer(Target, 0);
		}

		public override void GraphicsDispose() {
			Gl.DeleteFramebuffers(new uint[] { ID });
		}
	}
}
