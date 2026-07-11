using FishGfx.SmokeTest;
using Xunit;

namespace FishGfx.Tests
{
	public class GalleryScreenshotTests
	{
		[Theory]
		[InlineData("Gfx.Line", "gfx-line.png")]
		[InlineData("Gfx.NinePatch", "gfx-ninepatch.png")]
		[InlineData("Gfx.DrawText (TTF/SDF)", "gfx-drawtext-ttf-sdf.png")]
		[InlineData("  Multiple --- Separators  ", "multiple-separators.png")]
		public void FileNameForTitleProducesStableSlug(string title, string expected)
		{
			Assert.Equal(expected, GalleryScreenshot.FileNameForTitle(title));
		}

		[Fact]
		public void FileNamesForTitlesPreservesOrderAndRequiresUniqueness()
		{
			string[] names = GalleryScreenshot.FileNamesForTitles(new[] { "Gfx.Line", "Gfx.Circle" });

			Assert.Equal(new[] { "gfx-line.png", "gfx-circle.png" }, names);
			Assert.Throws<InvalidOperationException>(() =>
				GalleryScreenshot.FileNamesForTitles(new[] { "Gfx.Line", "gfx line" })
			);
		}

		[Theory]
		[InlineData(null)]
		[InlineData("")]
		[InlineData("   ")]
		[InlineData("///")]
		public void FileNameForTitleRejectsInvalidTitles(string title)
		{
			Assert.Throws<ArgumentException>(() => GalleryScreenshot.FileNameForTitle(title));
		}
	}
}
