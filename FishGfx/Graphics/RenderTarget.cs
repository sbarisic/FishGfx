using System;
using System.Collections.Generic;
using Silk.NET.OpenGL;

namespace FishGfx.Graphics;

public sealed class RenderTarget : IDisposable
{
	private readonly Framebuffer framebuffer;
	private readonly Texture[] colorAttachments;
	private bool disposed;

	internal RenderTarget(GraphicsContext owner, int width, int height)
	{
		Owner = owner ?? throw new ArgumentNullException(nameof(owner));
		Width = width;
		Height = height;
		SampleCount = 1;
		IsBackbuffer = true;
		colorAttachments = Array.Empty<Texture>();
		ColorAttachments = Array.AsReadOnly(colorAttachments);
	}

	internal RenderTarget(GraphicsContext owner, RenderTargetDescriptor descriptor)
	{
		Owner = owner ?? throw new ArgumentNullException(nameof(owner));
		Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
		Owner.EnsureCurrent();

		ValidateCapabilities(descriptor, owner.Capabilities);

		Width = descriptor.Width;
		Height = descriptor.Height;
		SampleCount = descriptor.SampleCount;
		colorAttachments = new Texture[descriptor.ColorFormats.Count];
		ColorAttachments = Array.AsReadOnly(colorAttachments);
		framebuffer = new Framebuffer(owner);

		try
		{
			CreateAttachments(descriptor);
			framebuffer.ConfigureDrawBuffers();
			framebuffer.ValidateComplete();
		}
		catch
		{
			DisposeResources();

			throw;
		}
	}

	public GraphicsContext Owner { get; }

	public RenderTargetDescriptor Descriptor { get; }

	public IReadOnlyList<Texture> ColorAttachments { get; }

	public Texture DepthStencilAttachment { get; private set; }

	public bool IsBackbuffer { get; }

	public int Width { get; private set; }

	public int Height { get; private set; }

	public int SampleCount { get; }

	public bool IsDisposed => disposed;

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}

		if (IsBackbuffer)
		{
			throw new InvalidOperationException("The context-owned backbuffer cannot be disposed.");
		}

		if (ReferenceEquals(Owner.ActivePass?.Target, this))
		{
			throw new InvalidOperationException("An active render target cannot be disposed.");
		}

		disposed = true;
		DisposeResources();
	}

	internal void Bind(FramebufferTarget target)
	{
		EnsureUsable();

		if (IsBackbuffer)
		{
			Internal_OpenGL.GL.BindFramebuffer(target, 0);
		}
		else
		{
			framebuffer.Bind(target);
		}
	}

	internal void ResizeBackbuffer(int width, int height)
	{
		if (!IsBackbuffer)
		{
			throw new InvalidOperationException("Only the backbuffer can be resized in place.");
		}

		if (width < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(width));
		}

		if (height < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(height));
		}

		Width = width;
		Height = height;
	}

	internal void EnsureUsable()
	{
		if (disposed)
		{
			throw new ObjectDisposedException(nameof(RenderTarget));
		}

		Owner.EnsureCurrent();
	}

	private static void ValidateCapabilities(
		RenderTargetDescriptor descriptor,
		GraphicsCapabilities capabilities
	)
	{
		if (descriptor.Width > capabilities.MaximumTexture2DSize
			|| descriptor.Height > capabilities.MaximumTexture2DSize)
		{
			throw new ArgumentOutOfRangeException(
				nameof(descriptor),
				$"Render-target dimensions exceed the context limit of {capabilities.MaximumTexture2DSize}."
			);
		}

		if (descriptor.SampleCount > capabilities.MaximumSamples)
		{
			throw new ArgumentOutOfRangeException(
				nameof(descriptor),
				$"Sample count exceeds the context limit of {capabilities.MaximumSamples}."
			);
		}

		if (descriptor.ColorFormats.Count > capabilities.MaximumColorAttachments)
		{
			throw new ArgumentOutOfRangeException(
				nameof(descriptor),
				$"Color attachment count exceeds the context limit of {capabilities.MaximumColorAttachments}."
			);
		}
	}

	private void CreateAttachments(RenderTargetDescriptor descriptor)
	{
		for (int index = 0; index < descriptor.ColorFormats.Count; index++)
		{
			Texture colorAttachment = CreateAttachment(
				descriptor.ColorFormats[index],
				TextureUsageFlags.ColorAttachment
			);

			colorAttachments[index] = colorAttachment;
			framebuffer.AttachColor(colorAttachment, index);
		}

		if (!descriptor.DepthStencilFormat.HasValue)
		{
			return;
		}

		DepthStencilAttachment = CreateAttachment(
			descriptor.DepthStencilFormat.Value,
			TextureUsageFlags.DepthStencilAttachment
		);
		framebuffer.AttachDepthStencil(DepthStencilAttachment);
	}

	private Texture CreateAttachment(TextureFormat format, TextureUsageFlags attachmentUsage)
	{
		TextureDimension dimension = SampleCount == 1
			? TextureDimension.Texture2D
			: TextureDimension.Texture2DMultisample;
		TextureUsageFlags usage = TextureUsageFlags.Sampled | attachmentUsage;

		if (SampleCount == 1)
		{
			usage |= TextureUsageFlags.TransferSource | TextureUsageFlags.TransferDestination;
		}

		TextureDescriptor descriptor = new TextureDescriptor(
			Width,
			Height,
			format,
			usage,
			dimension,
			samples: SampleCount,
			fixedSampleLocations: SampleCount > 1
		);

		return Owner.CreateTexture(descriptor);
	}

	private void DisposeResources()
	{
		framebuffer?.Dispose();

		for (int index = 0; index < colorAttachments.Length; index++)
		{
			colorAttachments[index]?.Dispose();
		}

		DepthStencilAttachment?.Dispose();
	}
}
