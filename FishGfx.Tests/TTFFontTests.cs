using System;
using System.IO;
using System.Numerics;
using FishGfx;
using FishGfx.Formats;
using Xunit;

namespace FishGfx.Tests;

public class TTFFontTests
{
	private static string FontPath => Path.Combine(AppContext.BaseDirectory, "data", "fonts", "Aaargh.ttf");
	private static string UnicodeFontPath =>
		Path.Combine(AppContext.BaseDirectory, "data", "fonts", "Consolas-Regular.ttf");

	[Fact]
	public void LoadsFromFileAndBytesWithMetricsAndKerning()
	{
		using TTFFont file = new TTFFont(FontPath);
		using TTFFont memory = new TTFFont(File.ReadAllBytes(FontPath), "Memory Font");
		Assert.Equal("Aaargh", file.FontName);
		Assert.Equal("Memory Font", memory.FontName);
		Assert.True(file.LineHeight > 0);
		Assert.True(file.GetCharInfo('A').Value.XAdvance > 0);
		Assert.True(file.SdfPixelRange > 0);
		Assert.Equal(file.GetKerning('A', 'V'), memory.GetKerning('A', 'V'));
		GfxFont.CharDest[] layout = file.LayoutString("A");
		file.MeasureString(layout, out Vector2 min, out Vector2 max);
		Assert.InRange(min.Y, -file.FontSize * 0.25f, file.LineHeight);
		Assert.InRange(max.Y, 0, file.LineHeight + file.FontSize * 0.25f);
	}

	[Fact]
	public void PreloadsAsciiAndLazilyAddsBmpGlyphs()
	{
		using TTFFont font = new TTFFont(UnicodeFontPath);
		int initial = font.GlyphCount;
		GfxFont.CharOrigin added = default;

		for (char c = '\u00A0'; c < '\u0400' && font.GlyphCount == initial; c++)
			added = font.GetCharInfo(c).Value;
		Assert.True(initial >= 95);
		Assert.True(font.GlyphCount > initial);
		Assert.InRange(added.X, 0, font.AtlasSize);
		Assert.InRange(added.Y, 0, font.AtlasSize);
		Assert.Equal(0, font.GetGlyphBorderMaximum('M'));
		Assert.Equal(0, font.GetGlyphBorderMaximum(' '));
	}

	[Fact]
	public void SmallAtlasGrowsAndFallbackHandlesSurrogates()
	{
		TTFFontOptions options = new TTFFontOptions
		{
			InitialAtlasSize = 64,
			MaximumAtlasSize = 512,
			PreloadPrintableAscii = false,
		};
		using TTFFont font = new TTFFont(FontPath, options);

		foreach (char c in "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789éñøЖΩ")
			font.GetCharInfo(c);
		Assert.True(font.AtlasSize > 64);
		GfxFont.CharOrigin invalid = font.GetCharInfo('\uD800').Value,
			replacement = font.GetCharInfo('\uFFFD').Value;
		Assert.Equal(replacement.X, invalid.X);
		Assert.Equal(replacement.Y, invalid.Y);
	}

	[Fact]
	public void LayoutSupportsScalingLinesTabsAndEmptyStrings()
	{
		using TTFFont font = new TTFFont(FontPath);
		Vector2 normal = font.MeasureString("Hello"),
			multiline = font.MeasureString("Hello\nWorld"),
			tabbed = font.MeasureString("A\tB");
		font.ScaledFontSize = font.FontSize * 2;
		Vector2 scaled = font.MeasureString("Hello");
		Assert.True(multiline.Y > normal.Y);
		Assert.True(tabbed.X > font.MeasureString("AB").X);
		Assert.InRange(scaled.X, normal.X * 1.9f, normal.X * 2.1f);
		Assert.Equal(Vector2.Zero, font.MeasureString(""));
	}

	[Fact]
	public void RejectsInvalidDataAndOptions()
	{
		Assert.Throws<ArgumentException>(() => new TTFFont(new byte[] { 1, 2, 3 }));
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			new TTFFont(File.ReadAllBytes(FontPath), options: new TTFFontOptions { InitialAtlasSize = 100 })
		);
	}

	[Fact]
	public void BitmapFontUsesSharedAtlasContract()
	{
		BMFont font = new BMFont(
			Path.Combine(AppContext.BaseDirectory, "data", "fonts", "proggy.fnt"),
			DoLoadTextures: false
		);
		Assert.IsAssignableFrom<IGfxAtlasFont>(font);
		Assert.Equal(GfxFontRenderMode.Bitmap, ((IGfxAtlasFont)font).RenderMode);
	}
}
