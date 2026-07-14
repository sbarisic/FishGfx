using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Numerics;
using FishGfx.Graphics;
using FishGfx.Graphics.Drawables;

namespace FishGfx.AdvGraphics;

public sealed class ParallaxSprite : IRenderable, IDisposable
{
	private readonly GraphicsContext graphics;
	private readonly List<Texture> layers = new();
	private readonly ReadOnlyCollection<Texture> readOnlyLayers;
	private readonly Sprite sprite;
	private bool disposed;

	public ParallaxSprite(
		GraphicsContext graphics,
		ShaderProgram shader,
		Camera camera
	)
	{
		this.graphics = graphics ?? throw new ArgumentNullException(nameof(graphics));
		Camera = camera ?? throw new ArgumentNullException(nameof(camera));
		readOnlyLayers = layers.AsReadOnly();
		sprite = new Sprite(graphics, shader);
	}

	public IReadOnlyList<Texture> Layers => readOnlyLayers;

	public Camera Camera { get; set; }

	public float LayerScrollScale { get; set; } = 0.8f;

	public float TextureScale { get; set; } = 3;

	public void AddLayer(Texture texture)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(texture);
		texture.EnsureOwner(graphics);
		layers.Add(texture);
	}

	public void AddLayers(IEnumerable<Texture> textures)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(textures);

		foreach (Texture texture in textures)
		{
			AddLayer(texture);
		}
	}

	public void Render(RenderPass pass)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(pass);

		if (!float.IsFinite(LayerScrollScale) || LayerScrollScale <= 0)
		{
			throw new InvalidOperationException("LayerScrollScale must be finite and positive.");
		}

		if (!float.IsFinite(TextureScale) || TextureScale <= 0)
		{
			throw new InvalidOperationException("TextureScale must be finite and positive.");
		}

		Vector2 cameraPosition = new(Camera.Position.X, Camera.Position.Y);

		for (int index = 0; index < layers.Count; index++)
		{
			Texture texture = layers[index];
			sprite.Texture = texture;
			sprite.Scale = texture.Size * TextureScale;

			float horizontalScale = MathF.Pow(LayerScrollScale, index + 1);
			Vector2 scrollPosition = cameraPosition * new Vector2(horizontalScale, 1);
			RenderTiled(pass, scrollPosition, cameraPosition);
		}
	}

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}

		disposed = true;
		sprite.Dispose();
	}

	private void RenderTiled(
		RenderPass pass,
		Vector2 scrollPosition,
		Vector2 cameraPosition
	)
	{
		float difference = cameraPosition.X - scrollPosition.X;
		int tileOffset = (int)(difference / sprite.Scale.X);

		for (int index = 0; index < 2; index++)
		{
			Vector2 offset = new(sprite.Scale.X * (tileOffset + index), 0);
			sprite.Position = scrollPosition + offset;
			sprite.Render(pass);
		}
	}

	private void ThrowIfDisposed()
	{
		if (disposed)
		{
			throw new ObjectDisposedException(nameof(ParallaxSprite));
		}
	}
}
