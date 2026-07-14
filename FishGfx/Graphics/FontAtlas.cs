using System;

namespace FishGfx.Graphics;

public sealed class FontAtlas : IDisposable
{
	private bool disposed;

	internal FontAtlas(
		GraphicsContext owner,
		Texture texture,
		FontRenderMode renderMode,
		float sdfPixelRange
	)
	{
		Owner = owner ?? throw new ArgumentNullException(nameof(owner));
		Texture = texture ?? throw new ArgumentNullException(nameof(texture));
		Texture.EnsureOwner(owner);
		RenderMode = renderMode;
		SdfPixelRange = sdfPixelRange;
	}

	public GraphicsContext Owner { get; }

	public Texture Texture { get; }

	public FontRenderMode RenderMode { get; }

	public float SdfPixelRange { get; }

	public int Width => Texture.Width;

	public int Height => Texture.Height;

	public bool IsDisposed => disposed;

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}

		disposed = true;
		Texture.Dispose();
	}
}
