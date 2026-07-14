using System;
using System.Numerics;
using System.Text;
using FishGfx.Graphics;
using FishGfx.Graphics.Drawables;

namespace FishGfx.Game;

public sealed partial class DevConsole : IRenderable, IDisposable
{
	private readonly Tilemap tilemap;
	private readonly GraphicsFont font;
	private Vector2 position;
	private bool disposed;

	public DevConsole(
		GraphicsContext graphics,
		Texture fontTileset,
		ShaderProgram shader,
		int characterSize,
		int width,
		int height,
		int bufferHeight
	)
		: this(characterSize, width, height, bufferHeight)
	{
		tilemap = new Tilemap(
			graphics,
			shader,
			fontTileset,
			characterSize,
			width,
			height
		);
	}

	public DevConsole(
		GraphicsFont font,
		int characterSize,
		int width,
		int height,
		int bufferHeight
	)
		: this(characterSize, width, height, bufferHeight)
	{
		this.font = font ?? throw new ArgumentNullException(nameof(font));
	}

	public Vector2 Position
	{
		get => position;
		set
		{
			position = value;

			if (tilemap != null)
			{
				tilemap.Position = value;
			}
		}
	}

	public void HandleKeyChanged(object sender, KeyEventArgs args)
	{
		ArgumentNullException.ThrowIfNull(args);

		if (!args.IsPressed)
		{
			return;
		}

		if (args.Key == Key.F1)
		{
			IsEnabled = !IsEnabled;
		}

		if (!IsEnabled)
		{
			return;
		}

		switch (args.Key)
		{
			case Key.Enter:
			case Key.NumpadEnter:
				PutCharacter('\n');
				break;

			case Key.Backspace:
				PutCharacter('\b');
				break;

			case Key.Up:
				SetViewScroll(ViewScroll + 1);
				break;

			case Key.Down:
				SetViewScroll(ViewScroll - 1);
				break;
		}
	}

	public void Render(RenderPass pass)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(pass);

		if (!IsEnabled)
		{
			return;
		}

		if (isDirty)
		{
			RefreshTilemap();
			isDirty = false;
		}

		pass.FillRectangle(
			Position.X,
			Position.Y,
			CharacterSize * Width,
			CharacterSize * Height,
			BackgroundColor
		);

		if (tilemap != null)
		{
			tilemap.Render(pass);
		}
		else
		{
			RenderFontBuffer(pass);
		}
	}

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}

		disposed = true;
		tilemap?.Dispose();
	}

	private void RefreshTilemap()
	{
		if (tilemap == null)
		{
			return;
		}

		for (int y = 0; y < tilemap.Height; y++)
		{
			for (int x = 0; x < tilemap.Width; x++)
			{
				char character = GetCharacter(x, y + ViewScroll);
				tilemap.SetTile(x, y, character, GetColor(x, y + ViewScroll));
			}
		}
	}

	private void RenderFontBuffer(RenderPass pass)
	{
		float scale = CharacterSize / font.BaseSize;
		float cellWidth = Math.Max(
			1,
			(font.GetGlyph('M')?.Advance ?? CharacterSize * 0.6f) * scale
		);

		for (int y = 0; y < Height; y++)
		{
			RenderFontRow(pass, y, cellWidth);
		}
	}

	private void RenderFontRow(RenderPass pass, int y, float cellWidth)
	{
		int x = 0;

		while (x < Width)
		{
			Color color = GetColor(x, y + ViewScroll);
			int start = x;
			StringBuilder run = new();

			while (x < Width && GetColor(x, y + ViewScroll) == color)
			{
				char character = GetCharacter(x, y + ViewScroll);
				run.Append(character == '\0' ? ' ' : character);
				x++;
			}

			string text = run.ToString();

			if (string.IsNullOrWhiteSpace(text))
			{
				continue;
			}

			Vector2 textPosition = Position + new Vector2(
				start * cellWidth,
				(Height - y - 1) * CharacterSize
			);
			pass.DrawText(font, textPosition, text, color, CharacterSize);
		}
	}

	private void ThrowIfDisposed()
	{
		if (disposed)
		{
			throw new ObjectDisposedException(nameof(DevConsole));
		}
	}
}
