using System;
using System.IO;
using System.Numerics;
using FishGfx.Formats;
using Xunit;

namespace FishGfx.Tests;

public sealed class TrueTypeFontTests
{
	private static string FontPath => Path.Combine(
		AppContext.BaseDirectory,
		"data",
		"fonts",
		"Aaargh.ttf"
	);

	private static string UnicodeFontPath => Path.Combine(
		AppContext.BaseDirectory,
		"data",
		"fonts",
		"Consolas-Regular.ttf"
	);

	[Fact]
	public void LoadsFromFileAndBytesWithMetricsAndKerning()
	{
		using TrueTypeFont file = new(FontPath);
		using TrueTypeFont memory = new(File.ReadAllBytes(FontPath), "Memory Font");

		Assert.Equal("Aaargh", file.Name);
		Assert.Equal("Memory Font", memory.Name);
		Assert.True(file.LineHeight > 0);
		Assert.True(file.GetGlyph('A').Value.Advance > 0);
		Assert.True(file.SdfPixelRange > 0);
		Assert.Equal(file.GetKerning('A', 'V'), memory.GetKerning('A', 'V'));

		PositionedGlyph[] layout = file.Layout("A", file.BaseSize);
		file.MeasureBounds(layout, out Vector2 minimum, out Vector2 maximum);

		Assert.InRange(minimum.Y, -file.BaseSize * 0.25f, file.LineHeight);
		Assert.InRange(maximum.Y, 0, file.LineHeight + file.BaseSize * 0.25f);
	}

	[Fact]
	public void PreloadsAsciiAndLazilyAddsBmpGlyphs()
	{
		using TrueTypeFont font = new(UnicodeFontPath);
		int initial = font.GlyphCount;
		GlyphMetrics added = default;

		for (char character = '\u00A0';
			character < '\u0400' && font.GlyphCount == initial;
			character++)
		{
			added = font.GetGlyph(character).Value;
		}

		Assert.True(initial >= 95);
		Assert.True(font.GlyphCount > initial);
		Assert.InRange(added.AtlasPosition.X, 0, font.AtlasSize);
		Assert.InRange(added.AtlasPosition.Y, 0, font.AtlasSize);
		Assert.Equal(0, font.GetGlyphBorderMaximum('M'));
		Assert.Equal(0, font.GetGlyphBorderMaximum(' '));
	}

	[Fact]
	public void SmallAtlasGrowsAndFallbackHandlesSurrogates()
	{
		TrueTypeFontOptions options = new()
		{
			InitialAtlasSize = 64,
			MaximumAtlasSize = 512,
			PreloadPrintableAscii = false,
		};

		using TrueTypeFont font = new(FontPath, options);

		foreach (char character in "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789éñøЖΩ")
		{
			font.GetGlyph(character);
		}

		Assert.True(font.AtlasSize > 64);

		GlyphMetrics invalid = font.GetGlyph('\uD800').Value;
		GlyphMetrics replacement = font.GetGlyph('\uFFFD').Value;

		Assert.Equal(replacement.AtlasPosition, invalid.AtlasPosition);
	}

	[Fact]
	public void LayoutSupportsExplicitScalingLinesTabsAndEmptyStrings()
	{
		using TrueTypeFont font = new(FontPath);
		Vector2 normal = font.Measure("Hello", font.BaseSize);
		Vector2 multiline = font.Measure("Hello\nWorld", font.BaseSize);
		Vector2 tabbed = font.Measure("A\tB", font.BaseSize);
		Vector2 scaled = font.Measure("Hello", font.BaseSize * 2);

		Assert.True(multiline.Y > normal.Y);
		Assert.True(tabbed.X > font.Measure("AB", font.BaseSize).X);
		Assert.InRange(scaled.X, normal.X * 1.9f, normal.X * 2.1f);
		Assert.Equal(Vector2.Zero, font.Measure("", font.BaseSize));
	}

	[Fact]
	public void RejectsInvalidDataAndOptions()
	{
		Assert.Throws<ArgumentException>(() => new TrueTypeFont(new byte[] { 1, 2, 3 }));
		Assert.Throws<ArgumentOutOfRangeException>(
			() => new TrueTypeFont(
				File.ReadAllBytes(FontPath),
				options: new TrueTypeFontOptions
				{
					InitialAtlasSize = 100,
				}
			)
		);
	}

	[Fact]
	public void BitmapFontUsesSharedGraphicsFontContract()
	{
		using BitmapFont font = new(
			Path.Combine(AppContext.BaseDirectory, "data", "fonts", "proggy.fnt")
		);

		Assert.IsAssignableFrom<GraphicsFont>(font);
		Assert.Equal(FontRenderMode.Bitmap, font.RenderMode);
	}
}
