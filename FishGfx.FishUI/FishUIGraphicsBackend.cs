using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using FishGfx.Formats;
using FishGfx.Graphics;

namespace FishGfx.FishUI;

public sealed partial class FishUIGraphicsBackend : global::FishUI.SimpleFishUIGfx, IDisposable
{
	private readonly RenderWindow window;
	private readonly GraphicsContext graphics;
	private readonly RootedFishUIFileSystem fileSystem;
	private readonly Dictionary<string, ImageResource> images = new(
		StringComparer.OrdinalIgnoreCase
	);
	private readonly Dictionary<string, TrueTypeFont> fonts = new(
		StringComparer.OrdinalIgnoreCase
	);
	private RenderPass pass;
	private RenderView renderView;
	private RenderState renderState;
	private IDisposable stateScope;
	private IDisposable viewScope;
	private IDisposable scissorScope;
	private bool isDrawing;
	private bool disposed;

	public FishUIGraphicsBackend(RenderWindow window, string resourceRoot = null)
	{
		this.window = window ?? throw new ArgumentNullException(nameof(window));
		graphics = window.Graphics;
		fileSystem = new RootedFishUIFileSystem(resourceRoot);
	}

	public string ResourceRoot => fileSystem.RootDirectory;

	public int LastFrameDrawCallCount { get; private set; }

	public RootedFishUIFileSystem FileSystem => fileSystem;

	public IDisposable UseRenderPass(
		RenderPass renderPass,
		RenderView view,
		RenderState state
	)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(renderPass);

		if (pass != null)
		{
			throw new InvalidOperationException("A FishUI render pass is already bound.");
		}

		pass = renderPass;
		renderView = view;
		renderState = state;

		return new BindingScope(this);
	}

	public override void Init()
	{
		ThrowIfDisposed();
	}

	public override int GetWindowWidth()
	{
		return window.Width;
	}

	public override int GetWindowHeight()
	{
		return window.Height;
	}

	public override void FocusWindow()
	{
		window.Focus();
	}

	public override void BeginDrawing(float deltaTime)
	{
		ThrowIfDisposed();

		if (pass == null)
		{
			throw new InvalidOperationException(
				"UseRenderPass must bind an active pass before FishUI draws."
			);
		}

		if (isDrawing)
		{
			throw new InvalidOperationException("FishUI drawing has already begun.");
		}

		LastFrameDrawCallCount = 0;
		stateScope = pass.PushState(renderState);

		try
		{
			viewScope = pass.PushView(renderView);
			isDrawing = true;
		}
		catch
		{
			stateScope.Dispose();
			stateScope = null;

			throw;
		}
	}

	public override void EndDrawing()
	{
		if (!isDrawing)
		{
			return;
		}

		try
		{
			scissorScope?.Dispose();
			scissorScope = null;
		}
		finally
		{
			try
			{
				viewScope?.Dispose();
				viewScope = null;
			}
			finally
			{
				stateScope?.Dispose();
				stateScope = null;
				isDrawing = false;
			}
		}
	}

	public override void BeginScissor(Vector2 position, Vector2 size)
	{
		RenderPass activePass = RequireDrawing();
		Vector2 converted = FishUIConversions.ToFishGfxRectanglePosition(
			position,
			size,
			GetWindowHeight()
		);
		AxisAlignedBoundingBox rectangle = AxisAlignedBoundingBox.FromPositionAndSize(
			new Vector3(converted, 0),
			new Vector3(size, 0)
		);
		RenderState clipped = renderState with
		{
			ScissorRectangle = rectangle,
		};

		scissorScope?.Dispose();
		scissorScope = activePass.PushState(clipped);
	}

	public override void EndScissor()
	{
		scissorScope?.Dispose();
		scissorScope = null;
	}

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}

		if (isDrawing)
		{
			EndDrawing();
		}

		pass = null;

		foreach (TrueTypeFont font in fonts.Values)
		{
			font.Dispose();
		}

		fonts.Clear();

		foreach (ImageResource image in images.Values)
		{
			image.Dispose();
		}

		images.Clear();
		disposed = true;
	}

	private RenderPass RequireDrawing()
	{
		ThrowIfDisposed();

		if (!isDrawing || pass == null)
		{
			throw new InvalidOperationException("FishUI drawing is not active.");
		}

		return pass;
	}

	private void ReleaseBinding()
	{
		if (isDrawing)
		{
			EndDrawing();
		}

		pass = null;
		renderView = default;
		renderState = default;
	}

	private void ThrowIfDisposed()
	{
		if (disposed)
		{
			throw new ObjectDisposedException(nameof(FishUIGraphicsBackend));
		}
	}

	private sealed class BindingScope : IDisposable
	{
		private FishUIGraphicsBackend owner;

		internal BindingScope(FishUIGraphicsBackend owner)
		{
			this.owner = owner;
		}

		public void Dispose()
		{
			FishUIGraphicsBackend current = owner;
			owner = null;
			current?.ReleaseBinding();
		}
	}

	private sealed class ImageResource : IDisposable
	{
		internal ImageResource(Texture texture, Bitmap bitmap)
		{
			Texture = texture;
			Bitmap = bitmap;
		}

		internal Texture Texture { get; }

		internal Bitmap Bitmap { get; }

		public void Dispose()
		{
			Texture.Dispose();
			Bitmap.Dispose();
		}
	}
}
