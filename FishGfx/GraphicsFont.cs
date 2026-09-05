using System;
using System.Text;
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
	Rune Character,
	Vector2 AtlasPosition,
	Vector2 AtlasSize,
	Vector2 Offset,
	float Advance
)
{
    public GlyphMetrics(char character, Vector2 atlasPosition, Vector2 atlasSize, Vector2 offset, float advance)
        : this(Rune.TryCreate(character, out Rune rune) ? rune : Rune.ReplacementChar, atlasPosition, atlasSize, offset, advance) { }
}

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
        List<PositionedGlyph> positioned = new(text?.Length ?? 0);
        LayoutInto(text, size, characterSpacing, positioned, out measuredSize);
        return positioned.ToArray();
    }

    internal void LayoutInto(string text, float size, float characterSpacing, List<PositionedGlyph> positioned, out Vector2 measuredSize)
	{
		ArgumentNullException.ThrowIfNull(text);
		ValidateLayout(size, characterSpacing);

		if (text.Length == 0)
		{
			measuredSize = Vector2.Zero;

			positioned.Clear();
            return;
		}

		float scale = size / BaseSize;
		float scaledLineHeight = LineHeight * scale;
		float scaledTabWidth = TabWidth * scale;
		positioned.Clear();
        PrepareLayoutGlyphs(text);
		float cursorX = 0;
		float cursorY = 0;
		float maximumLineAdvance = 0;
		int lineCount = 1;
		Rune previous = default;

		foreach (Rune character in text.EnumerateRunes())
		{
			if (character.Value == '\r')
			{
				continue;
			}

			if (character.Value == '\n')
			{
				maximumLineAdvance = Math.Max(maximumLineAdvance, cursorX);
				cursorX = 0;
				cursorY -= scaledLineHeight;
				lineCount++;
				previous = default;

				continue;
			}

			if (character.Value == '\t')
			{
				cursorX += scaledTabWidth;
				previous = default;

				continue;
			}

			GlyphMetrics glyph = GetGlyphOrFallback(character);

			if (previous.Value != 0)
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

		return;
	}

	public Vector2 Measure(string text, float size)
	{
		return Measure(text, size, 0);
	}

	public Vector2 Measure(string text, float size, float characterSpacing)
	{
        ArgumentNullException.ThrowIfNull(text);
        ValidateLayout(size, characterSpacing);
        if (text.Length == 0) return Vector2.Zero;
        float scale = size / BaseSize, cursor = 0, maximum = 0;
        int lines = 1;
        Rune previous = default;
        foreach (Rune character in text.EnumerateRunes())
        {
            if (character.Value == '\r') continue;
            if (character.Value == '\n') { maximum = Math.Max(maximum, cursor); cursor = 0; lines++; previous = default; continue; }
            if (character.Value == '\t') { cursor += TabWidth * scale; previous = default; continue; }
            if (previous.Value != 0) cursor += GetKerning(previous, character) * scale + characterSpacing;
            cursor += GetAdvance(character) * scale;
            previous = character;
        }
        return new Vector2(Math.Max(maximum, cursor), lines * LineHeight * scale);
	}

    /// <summary>Writes prefix advances and the kerning/spacing before each scalar, indexed by UTF-16 offset.</summary>
    public void MeasureAdvances(string text, float size, float spacing, Span<float> advances, Span<float> leading)
    {
        ArgumentNullException.ThrowIfNull(text);
        ValidateLayout(size, spacing);
        if (advances.Length < text.Length + 1 || leading.Length < text.Length + 1) throw new ArgumentException("Metric buffers are too small.");
        float scale = size / BaseSize, cursor = 0;
        Rune previous = default;
        int offset = 0;
        advances[0] = 0;
        leading[..(text.Length + 1)].Clear();
        foreach (Rune rune in text.EnumerateRunes())
        {
            float before = previous.Value == 0 ? 0 : GetKerning(previous, rune) * scale + spacing;
            if (rune.Value == '\r') { before = 0; }
            else if (rune.Value == '\n') { before = 0; cursor = 0; previous = default; }
            else if (rune.Value == '\t') { before = 0; cursor += TabWidth * scale; previous = default; }
            else { cursor += before + GetAdvance(rune) * scale; previous = rune; }
            leading[offset] = before;
            for (int i = 0; i < rune.Utf16SequenceLength; i++) advances[++offset] = cursor;
        }
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

    public virtual GlyphMetrics? GetGlyph(Rune character) => GetGlyph(character.IsBmp ? (char)character.Value : '?');
    public virtual float GetKerning(Rune first, Rune second) => first.IsBmp && second.IsBmp ? GetKerning((char)first.Value, (char)second.Value) : 0;
    public virtual float GetAdvance(Rune character) => GetGlyphOrFallback(character).Advance;
    protected virtual void PrepareLayoutGlyphs(string text) { }

	public abstract FontAtlas PrepareAtlas(GraphicsContext graphics, string text);

	public abstract void Dispose();

	private GlyphMetrics GetGlyphOrFallback(Rune character)
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
