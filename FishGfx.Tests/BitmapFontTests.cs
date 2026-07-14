using System;
using System.IO;
using FishGfx.Formats;
using Xunit;

namespace FishGfx.Tests;

public sealed class BitmapFontTests
{
	[Fact]
	public void OpenSansLoadsWithKerningBlock()
	{
		using BitmapFont font = new(
			Path.Combine(AppContext.BaseDirectory, "data", "fonts", "opensans.fnt")
		);

		Assert.Equal("Open Sans", font.Name);
		Assert.NotNull(font.GetGlyph('A'));
	}
}
