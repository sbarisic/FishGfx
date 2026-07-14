using System;
using System.Collections.Generic;
using Silk.NET.OpenGL;

namespace FishGfx.Graphics;

internal sealed class Framebuffer : GraphicsResource
{
	private readonly SortedDictionary<int, Texture> colorAttachments = new();
	private Texture depthStencilAttachment;

	internal Framebuffer(GraphicsContext owner)
		: base(owner)
	{
		Handle = Internal_OpenGL.Is45OrAbove
			? Internal_OpenGL.GL.CreateFramebuffer()
			: Internal_OpenGL.GL.GenFramebuffer();

		RegisterResource();
	}

	internal void AttachColor(Texture texture, int index)
	{
		EnsureCurrentOwner();
		ArgumentNullException.ThrowIfNull(texture);

		if (index < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(index));
		}

		texture.EnsureOwner(Owner);

		if ((texture.Usage & TextureUsageFlags.ColorAttachment) == 0)
		{
			throw new InvalidOperationException(
				"The texture is not a color attachment."
			);
		}

		FramebufferAttachment attachment =
			FramebufferAttachment.ColorAttachment0 + index;
		AttachTexture(attachment, texture);
		colorAttachments[index] = texture;
	}

	internal void AttachDepthStencil(Texture texture)
	{
		EnsureCurrentOwner();
		ArgumentNullException.ThrowIfNull(texture);
		texture.EnsureOwner(Owner);

		if ((texture.Usage & TextureUsageFlags.DepthStencilAttachment) == 0)
		{
			throw new InvalidOperationException(
				"The texture is not a depth-stencil attachment."
			);
		}

		FramebufferAttachment attachment = HasStencil(texture.Format)
			? FramebufferAttachment.DepthStencilAttachment
			: FramebufferAttachment.DepthAttachment;
		AttachTexture(attachment, texture);
		depthStencilAttachment = texture;
	}

	internal void ConfigureDrawBuffers()
	{
		EnsureCurrentOwner();

		if (colorAttachments.Count == 0)
		{
			WithBound(() =>
			{
				Internal_OpenGL.GL.DrawBuffer(DrawBufferMode.None);
				Internal_OpenGL.GL.ReadBuffer(ReadBufferMode.None);
			});

			return;
		}

		GLEnum[] buffers = new GLEnum[colorAttachments.Count];
		int bufferIndex = 0;

		foreach (int attachmentIndex in colorAttachments.Keys)
		{
			buffers[bufferIndex] = (GLEnum)(
				FramebufferAttachment.ColorAttachment0 + attachmentIndex
			);
			bufferIndex++;
		}

		if (Internal_OpenGL.Is45OrAbove)
		{
			Internal_OpenGL.GL.NamedFramebufferDrawBuffers(Handle, buffers);
		}
		else
		{
			WithBound(() => Internal_OpenGL.GL.DrawBuffers(buffers));
		}
	}

	internal void ValidateComplete()
	{
		EnsureCurrentOwner();

		FramebufferStatus status = Internal_OpenGL.Is45OrAbove
			? (FramebufferStatus)Internal_OpenGL.GL.CheckNamedFramebufferStatus(
				Handle,
				FramebufferTarget.Framebuffer
			)
			: (FramebufferStatus)WithBound(
				() => Internal_OpenGL.GL.CheckFramebufferStatus(
					FramebufferTarget.Framebuffer
				)
			);

		if (status != FramebufferStatus.Complete)
		{
			throw new InvalidOperationException(
				$"Framebuffer is incomplete: {status}."
			);
		}
	}

	internal void Bind(FramebufferTarget target)
	{
		EnsureCurrentOwner();
		Internal_OpenGL.GL.BindFramebuffer(target, Handle);
	}

	internal override void DeleteResource()
	{
		Internal_OpenGL.GL.DeleteFramebuffer(Handle);
	}

	private static bool HasStencil(TextureFormat format)
	{
		return format is
			TextureFormat.Depth24Stencil8 or
			TextureFormat.Depth32FloatStencil8;
	}

	private void AttachTexture(
		FramebufferAttachment attachment,
		Texture texture
	)
	{
		if (Internal_OpenGL.Is45OrAbove)
		{
			Internal_OpenGL.GL.NamedFramebufferTexture(
				Handle,
				attachment,
				texture.Handle,
				0
			);

			return;
		}

		WithBound(() => Internal_OpenGL.GL.FramebufferTexture(
			FramebufferTarget.Framebuffer,
			attachment,
			texture.Handle,
			0
		));
	}

	private void WithBound(Action action)
	{
		WithBound(() =>
		{
			action();

			return true;
		});
	}

	private T WithBound<T>(Func<T> action)
	{
		Internal_OpenGL.GL.GetInteger((GetPName)0x8CAA, out int previousRead);
		Internal_OpenGL.GL.GetInteger((GetPName)0x8CA6, out int previousDraw);

		try
		{
			Internal_OpenGL.GL.BindFramebuffer(
				FramebufferTarget.Framebuffer,
				Handle
			);

			return action();
		}
		finally
		{
			Internal_OpenGL.GL.BindFramebuffer(
				FramebufferTarget.ReadFramebuffer,
				(uint)previousRead
			);
			Internal_OpenGL.GL.BindFramebuffer(
				FramebufferTarget.DrawFramebuffer,
				(uint)previousDraw
			);
		}
	}
}
