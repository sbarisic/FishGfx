using System;
using System.Drawing;
using System.Numerics;
using FishGfx.Formats;
using FishGfx.Graphics;

namespace FishGfx.FishUI;

public sealed partial class FishUIGraphicsBackend
{
	public override global::FishUI.FontRef LoadFont(
		string fileName,
		float size,
		float spacing,
		global::FishUI.FishColor color
	)
	{
		return LoadFont(
			fileName,
			size,
			spacing,
			color,
			global::FishUI.FontStyle.Regular
		);
	}

	public override global::FishUI.FontRef LoadFont(
		string fileName,
		float size,
		float spacing,
		global::FishUI.FishColor color,
		global::FishUI.FontStyle style
	)
	{
		ThrowIfDisposed();

		if (!float.IsFinite(size) || size <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(size));
		}

		if (!float.IsFinite(spacing))
		{
			throw new ArgumentOutOfRangeException(nameof(spacing));
		}

		string path = fileSystem.ResolvePath(fileName);

		if (!fonts.TryGetValue(path, out TrueTypeFont font))
		{
			font = new TrueTypeFont(path);
			fonts.Add(path, font);
		}

		float lineHeight = font.LineHeight * size / font.BaseSize;

		return new global::FishUI.FontRef
		{
			Path = fileName,
			Userdata = font,
			Size = size,
			Spacing = spacing,
			Color = color,
			Style = style,
			LineHeight = lineHeight,
			IsMonospaced = IsMonospaced(font, size),
		};
	}

	public override global::FishUI.ImageRef LoadImage(string fileName)
	{
		ThrowIfDisposed();
		string path = fileSystem.ResolvePath(fileName);

		if (!images.TryGetValue(path, out ImageResource resource))
		{
			using Image loaded = Image.FromFile(path);
			Bitmap bitmap = new(loaded);

			try
			{
				Texture texture = graphics.CreateTextureFromImage(
					bitmap,
					new TextureLoadOptions
					{
						Sampling = new TextureSamplingState(
							TextureFilter.Linear,
							TextureFilter.Linear
						),
					}
				);
				resource = new ImageResource(texture, bitmap);
				images.Add(path, resource);
			}
			catch
			{
				bitmap.Dispose();

				throw;
			}
		}

		return new global::FishUI.ImageRef
		{
			Path = fileName,
			Width = resource.Texture.Width,
			Height = resource.Texture.Height,
			Userdata = resource,
			Userdata2 = resource.Bitmap,
		};
	}

	public override global::FishUI.FishColor GetImageColor(
		global::FishUI.ImageRef image,
		Vector2 position
	)
	{
		ImageResource resource = GetImageResource(image);
		(Vector2 sourcePosition, Vector2 sourceSize) = GetSourceRegion(image);

		if (position.X < 0
			|| position.Y < 0
			|| position.X >= sourceSize.X
			|| position.Y >= sourceSize.Y)
		{
			throw new ArgumentOutOfRangeException(nameof(position));
		}

		int x = checked((int)position.X + (int)sourcePosition.X);
		int y = checked((int)position.Y + (int)sourcePosition.Y);
		System.Drawing.Color color = resource.Bitmap.GetPixel(x, y);

		return new global::FishUI.FishColor(color.R, color.G, color.B, color.A);
	}

	public override void SetImageFilter(global::FishUI.ImageRef image, bool pixelated)
	{
		ImageResource resource = GetImageResource(image);
		TextureFilter filter = pixelated ? TextureFilter.Nearest : TextureFilter.Linear;
		resource.Texture.SetSampling(new TextureSamplingState(filter, filter));
	}

	public override Vector2 MeasureText(global::FishUI.FontRef fontReference, string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return Vector2.Zero;
		}

		TrueTypeFont font = GetFont(fontReference);
		FishUITextLayout layout = FishUITextLayout.Create(
			font,
			text,
			fontReference.Size,
			fontReference.Spacing
		);

		return layout.Size;
	}

	public override global::FishUI.FishUIFontMetrics GetFontMetrics(
		global::FishUI.FontRef fontReference
	)
	{
		TrueTypeFont font = GetFont(fontReference);
		float lineHeight = font.LineHeight * fontReference.Size / font.BaseSize;
		float ascent = lineHeight * 0.8f;

		return new global::FishUI.FishUIFontMetrics(
			lineHeight,
			ascent,
			lineHeight - ascent,
			ascent,
			MeasureText(fontReference, "x").X,
			MeasureText(fontReference, "W").X
		);
	}

	private static ImageResource GetImageResource(global::FishUI.ImageRef image)
	{
		if (image?.Userdata is ImageResource resource)
		{
			return resource;
		}

		throw new ArgumentException(
			"The image was not loaded by this FishGfx FishUI backend.",
			nameof(image)
		);
	}

	private static TrueTypeFont GetFont(global::FishUI.FontRef fontReference)
	{
		if (fontReference?.Userdata is TrueTypeFont font)
		{
			return font;
		}

		throw new ArgumentException(
			"The font was not loaded by this FishGfx FishUI backend.",
			nameof(fontReference)
		);
	}

	private static bool IsMonospaced(TrueTypeFont font, float size)
	{
		return Math.Abs(
			font.Measure("W", size).X - font.Measure("i", size).X
		) < 0.5f;
	}

	private static (Vector2 Position, Vector2 Size) GetSourceRegion(
		global::FishUI.ImageRef image
	)
	{
		ArgumentNullException.ThrowIfNull(image);

		if (!image.IsAtlasRegion)
		{
			return (Vector2.Zero, new Vector2(image.Width, image.Height));
		}

		int x = image.SourceX;
		int y = image.SourceY;
		global::FishUI.ImageRef parent = image.AtlasParent;

		while (parent != null && parent.IsAtlasRegion)
		{
			x += parent.SourceX;
			y += parent.SourceY;
			parent = parent.AtlasParent;
		}

		return (
			new Vector2(x, y),
			new Vector2(image.SourceW, image.SourceH)
		);
	}
}
