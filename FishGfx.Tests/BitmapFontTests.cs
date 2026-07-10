using FishGfx.Formats;
using System;
using System.IO;
using Xunit;

namespace FishGfx.Tests;

public class BitmapFontTests {
	[Fact]
	public void OpenSansLoadsWithKerningBlock() {
		BMFont font = new BMFont(Path.Combine(AppContext.BaseDirectory, "data", "fonts", "opensans.fnt"), DoLoadTextures: false);
		Assert.Equal("Open Sans", font.FontName);
		Assert.NotNull(font.GetCharInfo('A'));
	}
}
