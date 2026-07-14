using System;
using System.Collections.Generic;
using System.Numerics;
using FishGfx.FishUI;
using FishGfx.Graphics;
using Xunit;

namespace FishGfx.Tests;

public sealed class GraphicsFontTests
{
	[Fact]
	public void LayoutHandlesControlsBeforeGlyphLookupAndBreaksTabKerning()
	{
		RecordingFont font = new();

		PositionedGlyph[] glyphs = font.Layout("A\tV\nA", 16);

		Assert.Equal(new[] { 'A', 'V', 'A' }, font.RequestedCharacters);
		Assert.Empty(font.KerningPairs);
		Assert.Equal(3, glyphs.Length);
		Assert.Equal(50, glyphs[1].Position.X);
	}

	[Fact]
	public void LayoutTakesAnExplicitSizeWithoutMutatingFontState()
	{
		RecordingFont font = new();

		PositionedGlyph normal = Assert.Single(font.Layout("A", 16));
		PositionedGlyph scaled = Assert.Single(font.Layout("A", 32));

		Assert.Equal(normal.Size * 2, scaled.Size);
		Assert.Equal(16, font.BaseSize);
	}

	[Fact]
	public void MeasureIncludesTabsBlankLinesAndTrailingNewlines()
	{
		RecordingFont font = new();

		Assert.Equal(new Vector2(80, 16), font.Measure("\t\t", 16));
		Assert.Equal(new Vector2(0, 32), font.Measure("\n", 16));
		Assert.Equal(new Vector2(10, 32), font.Measure("A\n", 16));
		Assert.Equal(new Vector2(10, 48), font.Measure("A\n\n", 16));
		Assert.Empty(font.Layout("\t\n", 16));
	}

	[Fact]
	public void MeasureUsesTheSameLineAdvanceAsLayout()
	{
		RecordingFont font = new();

		PositionedGlyph[] glyphs = font.Layout("AA", 16);
		Vector2 measured = font.Measure("AA", 16);
		PositionedGlyph finalGlyph = glyphs[^1];

		Assert.Equal(17, finalGlyph.Position.X);
		Assert.Equal(finalGlyph.Position.X + finalGlyph.Advance, measured.X);
		Assert.Equal(font.LineHeight, measured.Y);
	}

	[Fact]
	public void CharacterSpacingIsAppliedOnlyBetweenAdjacentGlyphs()
	{
		RecordingFont font = new();

		PositionedGlyph[] glyphs = font.Layout("AA\tA\nAA", 16, 3);

		Assert.Equal(20, glyphs[1].Position.X);
		Assert.Equal(70, glyphs[2].Position.X);
		Assert.Equal(20, glyphs[4].Position.X);
		Assert.Equal(new Vector2(80, 32), font.Measure("AA\tA\nAA", 16, 3));
		Assert.Equal(new Vector2(10, 16), font.Measure("A", 16, 3));
		Assert.Throws<ArgumentOutOfRangeException>(
			() => font.Measure("A", 16, float.NaN)
		);
	}

	[Fact]
	public void FishUITextLayoutScalesFontAndSpacingTogether()
	{
		RecordingFont font = new();

		FishUITextLayout normal = FishUITextLayout.Create(
			font,
			"AAA",
			16,
			2
		);
		FishUITextLayout scaled = FishUITextLayout.Create(
			font,
			"AAA",
			16,
			2,
			2
		);
		PositionedGlyph[] renderedGlyphs = font.Layout(
			"AAA",
			normal.FontSize,
			normal.CharacterSpacing
		);
		PositionedGlyph finalGlyph = renderedGlyphs[^1];

		Assert.Equal(new Vector2(48, 16), normal.Size);
		Assert.Equal(16, normal.FontSize);
		Assert.Equal(2, normal.CharacterSpacing);
		Assert.Equal(normal.Size * 2, scaled.Size);
		Assert.Equal(32, scaled.FontSize);
		Assert.Equal(4, scaled.CharacterSpacing);
		Assert.Equal(finalGlyph.Position.X + finalGlyph.Advance, normal.Size.X);
	}

	private sealed class RecordingFont : GraphicsFont
	{
		internal List<char> RequestedCharacters { get; } = new();

		internal List<(char First, char Second)> KerningPairs { get; } = new();

		public override string Name => "Recording";

		public override float BaseSize => 16;

		public override float LineHeight => 16;

		public override float TabWidth => 40;

		public override FontRenderMode RenderMode => FontRenderMode.Bitmap;

		public override float SdfPixelRange => 0;

		public override GlyphMetrics? GetGlyph(char character)
		{
			RequestedCharacters.Add(character);

			return new GlyphMetrics(
				character,
				Vector2.Zero,
				new Vector2(8, 12),
				Vector2.Zero,
				10
			);
		}

		public override float GetKerning(char first, char second)
		{
			KerningPairs.Add((first, second));

			return 7;
		}

		public override FontAtlas PrepareAtlas(GraphicsContext graphics, string text)
		{
			throw new NotSupportedException();
		}

		public override void Dispose()
		{
		}
	}
}
