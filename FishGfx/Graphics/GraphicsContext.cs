using System;
using System.Collections.Generic;
using Silk.NET.OpenGL;

namespace FishGfx.Graphics;

public sealed partial class GraphicsContext : IDisposable
{
	[ThreadStatic]
	private static GraphicsContext current;

	private readonly Queue<GraphicsResourceRegistration> deletionQueue = new();
	private readonly HashSet<GraphicsResourceRegistration> resources = new();
	private readonly int ownerThreadId;
	private RenderFrame activeFrame;
	private bool disposed;

	internal GraphicsContext(RenderWindow window)
	{
		Window = window ?? throw new ArgumentNullException(nameof(window));
		ownerThreadId = Environment.CurrentManagedThreadId;

		MakeCurrent();
		Capabilities = ReadCapabilities();
		Backbuffer = new RenderTarget(
			this,
			window.FramebufferWidth,
			window.FramebufferHeight
		);
		Renderer = new ImmediateRenderer(this);
	}

	public static GraphicsContext Current => current
		?? throw new InvalidOperationException("No FishGfx graphics context is current on this thread.");

	public RenderWindow Window { get; }

	public GraphicsCapabilities Capabilities { get; }

	public RenderTarget Backbuffer { get; }

	public bool IsDisposed => disposed;

	public bool IsFrameActive => activeFrame != null;

	internal RenderPass ActivePass => activeFrame?.ActivePass;

	internal OpenGlRenderStateApplier StateApplier { get; } = new OpenGlRenderStateApplier();

	internal OpenGlBindingCache BindingCache { get; } = new OpenGlBindingCache();

	internal ImmediateRenderer Renderer { get; private set; }

	public RenderFrame BeginFrame()
	{
		EnsureCurrent();

		if (activeFrame != null)
		{
			throw new InvalidOperationException("A render frame is already active for this context.");
		}

		CollectGarbage();
		activeFrame = new RenderFrame(this);

		return activeFrame;
	}

	public void MakeCurrent()
	{
		EnsureOwnerThread();

		if (disposed)
		{
			throw new ObjectDisposedException(nameof(GraphicsContext));
		}

		Window.MakeNativeCurrent();
		current = this;
	}

	public void InvalidateStateCache()
	{
		EnsureCurrent();
		StateApplier.Invalidate();
		BindingCache.Invalidate();
	}

	internal void BindVertexArray(uint handle)
	{
		EnsureCurrent();
		BindingCache.BindVertexArray(handle);
	}

	internal void BindBuffer(BufferTargetARB target, uint handle)
	{
		EnsureCurrent();
		BindingCache.BindBuffer(target, handle);
	}

	public void CollectGarbage()
	{
		EnsureCurrent();

		while (TryDequeueDeletion(out GraphicsResourceRegistration registration))
		{
			try
			{
				registration.DeleteOnOwnerContext();
			}
			finally
			{
				lock (resources)
				{
					resources.Remove(registration);
				}
			}
		}
	}

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}

		EnsureOwnerThread();
		MakeCurrent();
		activeFrame?.Dispose();
		Renderer.Dispose();
		Renderer = null;

		GraphicsResourceRegistration[] outstanding;

		lock (resources)
		{
			outstanding = new GraphicsResourceRegistration[resources.Count];
			resources.CopyTo(outstanding);
		}

		foreach (GraphicsResourceRegistration registration in outstanding)
		{
			registration.DisposeResource();
		}

		CollectGarbage();
		disposed = true;

		if (ReferenceEquals(current, this))
		{
			current = null;
		}
	}

	internal void EndFrame(RenderFrame frame)
	{
		if (!ReferenceEquals(activeFrame, frame))
		{
			throw new InvalidOperationException("The render frame is not active on this context.");
		}

		activeFrame = null;
	}

	internal void ResizeBackbuffer(int width, int height)
	{
		Backbuffer.ResizeBackbuffer(width, height);
	}

	internal void EnsureCurrent()
	{
		if (disposed)
		{
			throw new ObjectDisposedException(nameof(GraphicsContext));
		}

		EnsureOwnerThread();

		if (!ReferenceEquals(current, this))
		{
			throw new InvalidOperationException("This graphics context is not current on its owning thread.");
		}
	}

	internal GraphicsResourceRegistration Register(GraphicsResource resource)
	{
		ArgumentNullException.ThrowIfNull(resource);

		if (!ReferenceEquals(resource.Owner, this))
		{
			throw new InvalidOperationException("The graphics resource belongs to another context.");
		}

		GraphicsResourceRegistration registration =
			GraphicsResourceRegistration.Create(this, resource);

		lock (resources)
		{
			if (!resources.Add(registration))
			{
				throw new InvalidOperationException("The graphics resource is already registered.");
			}
		}

		return registration;
	}

	internal void EnqueueDeletion(GraphicsResourceRegistration registration)
	{
		ArgumentNullException.ThrowIfNull(registration);

		lock (deletionQueue)
		{
			deletionQueue.Enqueue(registration);
		}
	}

	private GraphicsCapabilities ReadCapabilities()
	{
		Internal_OpenGL.GL.GetInteger(GetPName.MaxTextureSize, out int maximumTextureSize);
		Internal_OpenGL.GL.GetInteger(GetPName.MaxCubeMapTextureSize, out int maximumCubeTextureSize);
		Internal_OpenGL.GL.GetInteger((GetPName)0x8D57, out int maximumSamples);
		Internal_OpenGL.GL.GetInteger(GetPName.MaxColorAttachments, out int maximumColorAttachments);

		bool supportsAnisotropy = Array.IndexOf(
			Internal_OpenGL.Extensions,
			"GL_EXT_texture_filter_anisotropic"
		) >= 0 || Array.IndexOf(
			Internal_OpenGL.Extensions,
			"GL_ARB_texture_filter_anisotropic"
		) >= 0;
		float maximumAnisotropy = 1;

		if (supportsAnisotropy)
		{
			Internal_OpenGL.GL.GetFloat((GLEnum)0x84FF, out maximumAnisotropy);
		}

		return new GraphicsCapabilities(
			new OpenGlVersion(Internal_OpenGL.MajorVersion, Internal_OpenGL.MinorVersion),
			Internal_OpenGL.Renderer,
			Internal_OpenGL.Extensions,
			maximumTextureSize,
			maximumCubeTextureSize,
			maximumSamples,
			maximumColorAttachments,
			maximumAnisotropy
		);
	}

	private void EnsureOwnerThread()
	{
		if (Environment.CurrentManagedThreadId != ownerThreadId)
		{
			throw new InvalidOperationException("Graphics contexts may only be used from their owning thread.");
		}
	}

	private bool TryDequeueDeletion(
		out GraphicsResourceRegistration registration
	)
	{
		lock (deletionQueue)
		{
			if (deletionQueue.Count == 0)
			{
				registration = null;

				return false;
			}

			registration = deletionQueue.Dequeue();

			return true;
		}
	}
}
