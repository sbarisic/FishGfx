using System;
using System.Collections.Generic;
using FishGfx;
using FishGfx.Graphics;
using Xunit;

namespace FishGfx.Tests;

public class GfxFontTests
{
	[Fact]
	public void LayoutHandlesControlsBeforeGlyphLookupAndBreaksTabKerning()
	{
		RecordingFont font = new RecordingFont();

		GfxFont.CharDest[] characters = font.LayoutString("A\tV\nA");

		Assert.Equal(new[] { 'A', 'V', 'A' }, font.RequestedCharacters);
		Assert.Empty(font.KerningPairs);
		Assert.Equal(3, characters.Length);
		Assert.Equal(50, characters[1].X);
	}

	[Fact]
	public void DrawTextRestoresScaleWhenAtlasPreparationFails()
	{
		RecordingFont font = new RecordingFont { ScaledFontSize = 12, ThrowOnPrepare = true };

		Assert.Throws<InvalidOperationException>(() =>
			Gfx.DrawText(font, default, "failure", Color.White, FontSize: 24)
		);
		Assert.Equal(12, font.ScaledFontSize);
	}

	private sealed class RecordingFont : GfxFont, IGfxAtlasFont
	{
		internal List<char> RequestedCharacters { get; } = new List<char>();
		internal List<(char First, char Second)> KerningPairs { get; } = new List<(char, char)>();
		internal bool ThrowOnPrepare { get; init; }

		public override string FontName => "Recording";
		public override int LineHeight => 16;
		public override int FontSize => 16;
		public override int TabSize => 40;
		public Texture AtlasTexture => null;
		public GfxFontRenderMode RenderMode => GfxFontRenderMode.Bitmap;
		public float SdfPixelRange => 0;

		internal RecordingFont()
		{
			ScaledFontSize = FontSize;
		}

		public override CharOrigin? GetCharInfo(char character)
		{
			RequestedCharacters.Add(character);
			return new CharOrigin { Char = character, Owner = this, W = 8, H = 12, XAdvance = 10 };
		}

		public override int GetKerning(char first, char second)
		{
			KerningPairs.Add((first, second));
			return 7;
		}

		public void PrepareText(string text)
		{
			if (ThrowOnPrepare)
				throw new InvalidOperationException("Expected test failure.");
		}
	}
}
