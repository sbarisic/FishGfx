using System;
using Silk.NET.OpenGL;

namespace FishGfx.Graphics;

public sealed class RenderFrame : IDisposable
{
	private readonly GraphicsContext context;
	private bool disposed;
	private bool presented;

	internal RenderFrame(GraphicsContext context)
	{
		this.context = context ?? throw new ArgumentNullException(nameof(context));
	}

	public bool IsPresented => presented;

	internal RenderPass ActivePass { get; private set; }

	public RenderPass BeginPass(RenderTarget target, RenderPassDescriptor descriptor)
	{
		EnsureUsable();
		ArgumentNullException.ThrowIfNull(target);
		ArgumentNullException.ThrowIfNull(descriptor);

		if (ActivePass != null)
		{
			throw new InvalidOperationException("A render pass is already active for this frame.");
		}

		if (!ReferenceEquals(target.Owner, context))
		{
			throw new InvalidOperationException("The render target belongs to another graphics context.");
		}

		target.EnsureUsable();
		ActivePass = new RenderPass(this, context, target, descriptor);

		return ActivePass;
	}

	public void ResolveColor(
		RenderTarget source,
		int sourceAttachment,
		RenderTarget destination,
		int destinationAttachment
	)
	{
		EnsureUsable();
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(destination);

		if (ActivePass != null)
		{
			throw new InvalidOperationException("Color attachments cannot be resolved during an active render pass.");
		}

		ValidateResolve(source, sourceAttachment, destination, destinationAttachment);
		Internal_OpenGL.GL.GetInteger((GetPName)0x8CAA, out int previousRead);
		Internal_OpenGL.GL.GetInteger((GetPName)0x8CA6, out int previousDraw);

		try
		{
			source.Bind(FramebufferTarget.ReadFramebuffer);
			destination.Bind(FramebufferTarget.DrawFramebuffer);
			Internal_OpenGL.GL.ReadBuffer(
				(ReadBufferMode)((int)ReadBufferMode.ColorAttachment0 + sourceAttachment)
			);

			DrawBufferMode drawBuffer = destination.IsBackbuffer
				? DrawBufferMode.Back
				: (DrawBufferMode)((int)DrawBufferMode.ColorAttachment0 + destinationAttachment);

			Internal_OpenGL.GL.DrawBuffer(drawBuffer);
			Internal_OpenGL.GL.BlitFramebuffer(
				0,
				0,
				source.Width,
				source.Height,
				0,
				0,
				destination.Width,
				destination.Height,
				ClearBufferMask.ColorBufferBit,
				BlitFramebufferFilter.Nearest
			);
		}
		finally
		{
			Internal_OpenGL.GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, (uint)previousRead);
			Internal_OpenGL.GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, (uint)previousDraw);
		}
	}

	public void ResolveDepth(RenderTarget source, RenderTarget destination)
	{
		EnsureUsable();
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(destination);

		if (ActivePass != null)
		{
			throw new InvalidOperationException(
				"Depth attachments cannot be resolved during an active render pass."
			);
		}

		ValidateDepthResolve(source, destination);
		Internal_OpenGL.GL.GetInteger((GetPName)0x8CAA, out int previousRead);
		Internal_OpenGL.GL.GetInteger((GetPName)0x8CA6, out int previousDraw);

		try
		{
			source.Bind(FramebufferTarget.ReadFramebuffer);
			destination.Bind(FramebufferTarget.DrawFramebuffer);
			Internal_OpenGL.GL.BlitFramebuffer(
				0,
				0,
				source.Width,
				source.Height,
				0,
				0,
				destination.Width,
				destination.Height,
				ClearBufferMask.DepthBufferBit,
				BlitFramebufferFilter.Nearest
			);
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

	public void Present()
	{
		EnsureUsable();

		if (ActivePass != null)
		{
			throw new InvalidOperationException("Close the active render pass before presenting the frame.");
		}

		context.CollectGarbage();
		context.Window.SwapNativeBuffers();
		presented = true;
	}

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}

		ActivePass?.Dispose();
		disposed = true;
		context.EndFrame(this);
	}

	internal void EndPass(RenderPass pass)
	{
		if (!ReferenceEquals(ActivePass, pass))
		{
			throw new InvalidOperationException("The render pass is not active for this frame.");
		}

		ActivePass = null;
	}

	private void EnsureUsable()
	{
		if (disposed)
		{
			throw new ObjectDisposedException(nameof(RenderFrame));
		}

		context.EnsureCurrent();

		if (presented)
		{
			throw new InvalidOperationException("A presented frame cannot be used again.");
		}
	}

	private void ValidateResolve(
		RenderTarget source,
		int sourceAttachment,
		RenderTarget destination,
		int destinationAttachment
	)
	{
		if (!ReferenceEquals(source.Owner, context) || !ReferenceEquals(destination.Owner, context))
		{
			throw new InvalidOperationException("Both render targets must belong to this frame's graphics context.");
		}

		source.EnsureUsable();
		destination.EnsureUsable();

		if (source.IsBackbuffer || source.SampleCount <= 1)
		{
			throw new InvalidOperationException("The resolve source must be a multisampled render target.");
		}

		if (destination.SampleCount != 1)
		{
			throw new InvalidOperationException("The resolve destination must be single-sampled.");
		}

		if (source.Width != destination.Width || source.Height != destination.Height)
		{
			throw new InvalidOperationException("Color resolves require matching render-target dimensions.");
		}

		if (sourceAttachment < 0 || sourceAttachment >= source.ColorAttachments.Count)
		{
			throw new ArgumentOutOfRangeException(nameof(sourceAttachment));
		}

		if (destination.IsBackbuffer)
		{
			if (destinationAttachment != 0)
			{
				throw new ArgumentOutOfRangeException(nameof(destinationAttachment));
			}

			return;
		}

		if (destinationAttachment < 0 || destinationAttachment >= destination.ColorAttachments.Count)
		{
			throw new ArgumentOutOfRangeException(nameof(destinationAttachment));
		}

		if (source.ColorAttachments[sourceAttachment].Format
			!= destination.ColorAttachments[destinationAttachment].Format)
		{
			throw new InvalidOperationException("Color resolves require matching attachment formats.");
		}
	}

	private void ValidateDepthResolve(
		RenderTarget source,
		RenderTarget destination
	)
	{
		if (!ReferenceEquals(source.Owner, context)
			|| !ReferenceEquals(destination.Owner, context))
		{
			throw new InvalidOperationException(
				"Both render targets must belong to this frame's graphics context."
			);
		}

		source.EnsureUsable();
		destination.EnsureUsable();

		if (source.IsBackbuffer || source.SampleCount <= 1)
		{
			throw new InvalidOperationException(
				"The depth resolve source must be a multisampled render target."
			);
		}

		if (destination.IsBackbuffer || destination.SampleCount != 1)
		{
			throw new InvalidOperationException(
				"The depth resolve destination must be a single-sampled texture target."
			);
		}

		if (source.Width != destination.Width
			|| source.Height != destination.Height)
		{
			throw new InvalidOperationException(
				"Depth resolves require matching render-target dimensions."
			);
		}

		if (source.DepthStencilAttachment == null
			|| destination.DepthStencilAttachment == null)
		{
			throw new InvalidOperationException(
				"Both render targets require depth attachments."
			);
		}

		if (source.DepthStencilAttachment.Format
			!= destination.DepthStencilAttachment.Format)
		{
			throw new InvalidOperationException(
				"Depth resolves require matching attachment formats."
			);
		}
	}
}
