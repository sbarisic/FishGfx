using System;
using System.Numerics;
using Silk.NET.OpenGL;

namespace FishGfx.Graphics;

public sealed partial class RenderPass : IDisposable
{
	private readonly RenderFrame frame;
	private readonly GraphicsContext context;
	private readonly RenderTarget target;
	private readonly RenderUniformState uniforms;
	private GraphicsQuery activeQuery;
	private RenderState state;
	private int scopeDepth;
	private bool disposed;

	internal RenderPass(
		RenderFrame frame,
		GraphicsContext context,
		RenderTarget target,
		RenderPassDescriptor descriptor
	)
	{
		this.frame = frame ?? throw new ArgumentNullException(nameof(frame));
		this.context = context ?? throw new ArgumentNullException(nameof(context));
		this.target = target ?? throw new ArgumentNullException(nameof(target));

		ArgumentNullException.ThrowIfNull(descriptor);
		ValidateDescriptor(descriptor);

		state = descriptor.State;
		uniforms = new RenderUniformState(descriptor.View, descriptor.Time);

		try
		{
			context.BindingCache.Invalidate();
			target.Bind(FramebufferTarget.Framebuffer);
			Internal_OpenGL.GL.Viewport(0, 0, (uint)target.Width, (uint)target.Height);
			ClearForLoadActions(descriptor);
			context.StateApplier.Apply(state);
		}
		catch
		{
			Internal_OpenGL.GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
			context.StateApplier.Invalidate();

			throw;
		}
	}

	public GraphicsContext Context => context;

	public RenderTarget Target => target;

	public RenderView View => uniforms.View;

	public RenderState State => state;

	internal RenderUniformState Uniforms => uniforms;

	public IDisposable PushState(RenderState replacement)
	{
		EnsureActive();
		replacement.Validate();

		RenderState previous = state;
		state = replacement;
		context.StateApplier.Apply(replacement);

		return new PassScope(this, () =>
		{
			state = previous;
			context.StateApplier.Apply(previous);
		});
	}

	public IDisposable PushModel(Matrix4x4 model)
	{
		EnsureActive();

		Matrix4x4 previous = uniforms.Model;
		uniforms.Model = model;

		return new PassScope(this, () => uniforms.Model = previous);
	}

	public IDisposable PushView(RenderView view)
	{
		EnsureActive();

		RenderView previous = uniforms.View;
		uniforms.View = view;

		return new PassScope(this, () => uniforms.View = previous);
	}

	public IDisposable BeginQuery(GraphicsQuery query)
	{
		EnsureActive();
		ArgumentNullException.ThrowIfNull(query);
		query.EnsureOwner(context);

		if (activeQuery != null)
		{
			throw new InvalidOperationException("Only one graphics query can be active in a render pass.");
		}

		query.Begin();
		activeQuery = query;

		return new PassScope(this, () =>
		{
			try
			{
				query.End();
			}
			finally
			{
				activeQuery = null;
			}
		});
	}

	public void Clear(
		Color color,
		bool clearColor = true,
		bool clearDepth = true,
		bool clearStencil = true,
		float depth = 1,
		int stencil = 0
	)
	{
		EnsureActive();
		ValidateClearDepth(depth);
		ClearBuffers(color, clearColor, clearDepth, clearStencil, depth, stencil);
		context.StateApplier.Apply(state);
	}

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}

		EnsureActive();

		if (scopeDepth != 0)
		{
			throw new InvalidOperationException("All render-pass scopes must be closed before the pass is disposed.");
		}

		disposed = true;

		try
		{
			Internal_OpenGL.GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
		}
		finally
		{
			context.BindingCache.Invalidate();
			frame.EndPass(this);
		}
	}

	internal void EnsureActive()
	{
		context.EnsureCurrent();

		if (disposed || !ReferenceEquals(context.ActivePass, this))
		{
			throw new InvalidOperationException("The render pass is not active.");
		}
	}

	private static void ValidateDescriptor(RenderPassDescriptor descriptor)
	{
		descriptor.State.Validate();
		ValidateClearDepth(descriptor.ClearDepth);

		if (!Enum.IsDefined(descriptor.ColorLoadAction)
			|| !Enum.IsDefined(descriptor.DepthLoadAction)
			|| !Enum.IsDefined(descriptor.StencilLoadAction))
		{
			throw new ArgumentOutOfRangeException(nameof(descriptor));
		}
	}

	private static void ValidateClearDepth(float depth)
	{
		if (!float.IsFinite(depth) || depth < 0 || depth > 1)
		{
			throw new ArgumentOutOfRangeException(nameof(depth));
		}
	}

	private void ClearForLoadActions(RenderPassDescriptor descriptor)
	{
		bool clearColor = descriptor.ColorLoadAction == RenderLoadAction.Clear;
		bool clearDepth = descriptor.DepthLoadAction == RenderLoadAction.Clear;
		bool clearStencil = descriptor.StencilLoadAction == RenderLoadAction.Clear;

		if (!clearColor && !clearDepth && !clearStencil)
		{
			return;
		}

		Internal_OpenGL.GL.Disable(EnableCap.ScissorTest);
		context.StateApplier.Invalidate();

		ClearBuffers(
			descriptor.ClearColor,
			clearColor,
			clearDepth,
			clearStencil,
			descriptor.ClearDepth,
			descriptor.ClearStencil
		);
	}

	private void ClearBuffers(
		Color color,
		bool clearColor,
		bool clearDepth,
		bool clearStencil,
		float depth,
		int stencil
	)
	{
		ClearBufferMask mask = 0;

		if (clearColor)
		{
			Internal_OpenGL.GL.ColorMask(true, true, true, true);
			Internal_OpenGL.GL.ClearColor(
				color.R / 255f,
				color.G / 255f,
				color.B / 255f,
				color.A / 255f
			);
			mask |= ClearBufferMask.ColorBufferBit;
		}

		if (clearDepth)
		{
			Internal_OpenGL.GL.DepthMask(true);
			Internal_OpenGL.GL.ClearDepth(depth);
			mask |= ClearBufferMask.DepthBufferBit;
		}

		if (clearStencil)
		{
			Internal_OpenGL.GL.StencilMask(uint.MaxValue);
			Internal_OpenGL.GL.ClearStencil(stencil);
			mask |= ClearBufferMask.StencilBufferBit;
		}

		if (mask != 0)
		{
			Internal_OpenGL.GL.Clear(mask);
			context.StateApplier.Invalidate();
		}
	}

	private int OpenScope()
	{
		EnsureActive();

		scopeDepth++;

		return scopeDepth;
	}

	private void CloseScope(int depth, Action restore)
	{
		EnsureActive();

		if (depth != scopeDepth)
		{
			throw new InvalidOperationException("Render-pass scopes must be disposed in reverse order.");
		}

		try
		{
			restore();
		}
		finally
		{
			scopeDepth--;
		}
	}

	private sealed class PassScope : IDisposable
	{
		private readonly RenderPass pass;
		private readonly Action restore;
		private readonly int depth;
		private bool disposed;

		internal PassScope(RenderPass pass, Action restore)
		{
			this.pass = pass;
			this.restore = restore;
			depth = pass.OpenScope();
		}

		public void Dispose()
		{
			if (disposed)
			{
				return;
			}

			pass.CloseScope(depth, restore);
			disposed = true;
		}
	}
}
