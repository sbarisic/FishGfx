using System;
using System.Collections.Generic;
using System.Numerics;
using FishGfx.Graphics;

namespace FishGfx;

public enum FontRenderMode
{
	Bitmap,
	SignedDistanceField,
}

public readonly record struct GlyphMetrics(
	char Character,
	Vector2 AtlasPosition,
	Vector2 AtlasSize,
	Vector2 Offset,
	float Advance
);

public readonly record struct PositionedGlyph(
	GlyphMetrics Glyph,
	Vector2 Position,
	Vector2 Size,
	float Advance
);

public abstract class GraphicsFont : IDisposable
{
	public abstract string Name { get; }

	public abstract float BaseSize { get; }

	public abstract float LineHeight { get; }

	public abstract float TabWidth { get; }

	public abstract FontRenderMode RenderMode { get; }

	public abstract float SdfPixelRange { get; }

	public PositionedGlyph[] Layout(string text, float size)
	{
		return Layout(text, size, 0);
	}

	public PositionedGlyph[] Layout(
		string text,
		float size,
		float characterSpacing
	)
	{
		return LayoutAndMeasure(text, size, characterSpacing, out _);
	}

	internal PositionedGlyph[] LayoutAndMeasure(
		string text,
		float size,
		float characterSpacing,
		out Vector2 measuredSize
	)
	{
		ArgumentNullException.ThrowIfNull(text);
		ValidateLayout(size, characterSpacing);

		if (text.Length == 0)
		{
			measuredSize = Vector2.Zero;

			return Array.Empty<PositionedGlyph>();
		}

		float scale = size / BaseSize;
		float scaledLineHeight = LineHeight * scale;
		float scaledTabWidth = TabWidth * scale;
		List<PositionedGlyph> positioned = new(text.Length);
		float cursorX = 0;
		float cursorY = 0;
		float maximumLineAdvance = 0;
		int lineCount = 1;
		char previous = '\0';

		foreach (char character in text)
		{
			if (character == '\r')
			{
				continue;
			}

			if (character == '\n')
			{
				maximumLineAdvance = Math.Max(maximumLineAdvance, cursorX);
				cursorX = 0;
				cursorY -= scaledLineHeight;
				lineCount++;
				previous = '\0';

				continue;
			}

			if (character == '\t')
			{
				cursorX += scaledTabWidth;
				previous = '\0';

				continue;
			}

			GlyphMetrics glyph = GetGlyphOrFallback(character);

			if (previous != '\0')
			{
				cursorX += GetKerning(previous, character) * scale;
				cursorX += characterSpacing;
			}

			Vector2 position = new(
				cursorX + glyph.Offset.X * scale,
				cursorY - (glyph.Offset.Y + glyph.AtlasSize.Y) * scale
			);
			Vector2 glyphSize = glyph.AtlasSize * scale;
			float advance = glyph.Advance * scale;
			positioned.Add(
				new PositionedGlyph(
					glyph,
					position,
					glyphSize,
					advance
				)
			);
			cursorX += advance;
			previous = character;
		}

		maximumLineAdvance = Math.Max(maximumLineAdvance, cursorX);
		float verticalOffset = lineCount * scaledLineHeight;

		for (int index = 0; index < positioned.Count; index++)
		{
			PositionedGlyph glyph = positioned[index];
			positioned[index] = glyph with
			{
				Position = glyph.Position + new Vector2(0, verticalOffset),
			};
		}

		measuredSize = new Vector2(
			maximumLineAdvance,
			lineCount * scaledLineHeight
		);

		return positioned.ToArray();
	}

	public Vector2 Measure(string text, float size)
	{
		return Measure(text, size, 0);
	}

	public Vector2 Measure(string text, float size, float characterSpacing)
	{
		LayoutAndMeasure(text, size, characterSpacing, out Vector2 measuredSize);

		return measuredSize;
	}

	public Vector2 Measure(IReadOnlyList<PositionedGlyph> glyphs)
	{
		MeasureBounds(glyphs, out Vector2 minimum, out Vector2 maximum);

		return maximum - minimum;
	}

	public void MeasureBounds(
		IReadOnlyList<PositionedGlyph> glyphs,
		out Vector2 minimum,
		out Vector2 maximum
	)
	{
		ArgumentNullException.ThrowIfNull(glyphs);

		if (glyphs.Count == 0)
		{
			minimum = Vector2.Zero;
			maximum = Vector2.Zero;

			return;
		}

		minimum = glyphs[0].Position;
		maximum = glyphs[0].Position + new Vector2(
			Math.Max(glyphs[0].Size.X, glyphs[0].Advance),
			glyphs[0].Size.Y
		);

		for (int index = 1; index < glyphs.Count; index++)
		{
			PositionedGlyph glyph = glyphs[index];
			minimum = Vector2.Min(minimum, glyph.Position);
			Vector2 extent = new(
				Math.Max(glyph.Size.X, glyph.Advance),
				glyph.Size.Y
			);
			maximum = Vector2.Max(maximum, glyph.Position + extent);
		}
	}

	public abstract GlyphMetrics? GetGlyph(char character);

	public virtual float GetKerning(char first, char second)
	{
		return 0;
	}

	public abstract FontAtlas PrepareAtlas(GraphicsContext graphics, string text);

	public abstract void Dispose();

	private GlyphMetrics GetGlyphOrFallback(char character)
	{
		GlyphMetrics? glyph = GetGlyph(character) ?? GetGlyph('?');

		if (glyph == null)
		{
			throw new InvalidOperationException(
				$"Font '{Name}' contains neither '{character}' nor a '?' fallback glyph."
			);
		}

		return glyph.Value;
	}

	private void ValidateLayout(float size, float characterSpacing)
	{
		if (!float.IsFinite(size) || size <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(size));
		}

		if (!float.IsFinite(characterSpacing))
		{
			throw new ArgumentOutOfRangeException(nameof(characterSpacing));
		}

		if (!float.IsFinite(BaseSize) || BaseSize <= 0)
		{
			throw new InvalidOperationException(
				"The font base size must be finite and positive."
			);
		}

		if (!float.IsFinite(LineHeight) || LineHeight <= 0)
		{
			throw new InvalidOperationException(
				"The font line height must be finite and positive."
			);
		}

		if (!float.IsFinite(TabWidth) || TabWidth < 0)
		{
			throw new InvalidOperationException(
				"The font tab width must be finite and non-negative."
			);
		}
	}
}
