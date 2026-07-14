using FishGfx.SmokeTest;
using Xunit;

namespace FishGfx.Tests;

public class GalleryScreenshotTests
{
	[Theory]
	[InlineData("RenderPass.DrawLine", "renderpass-drawline.png")]
	[InlineData("RenderPass.DrawNinePatch", "renderpass-drawninepatch.png")]
	[InlineData(
		"RenderPass.DrawText (TrueType/SDF)",
		"renderpass-drawtext-truetype-sdf.png"
	)]
	[InlineData("  Multiple --- Separators  ", "multiple-separators.png")]
	public void FileNameForTitleProducesStableSlug(string title, string expected)
	{
		Assert.Equal(expected, GalleryScreenshot.FileNameForTitle(title));
	}

	[Fact]
	public void FileNamesForTitlesPreservesOrderAndRequiresUniqueness()
	{
		string[] names = GalleryScreenshot.FileNamesForTitles(
			new[] { "RenderPass.DrawLine", "RenderPass.DrawCircle" }
		);

		Assert.Equal(
			new[] { "renderpass-drawline.png", "renderpass-drawcircle.png" },
			names
		);
		Assert.Throws<InvalidOperationException>(() =>
			GalleryScreenshot.FileNamesForTitles(
				new[] { "RenderPass.DrawLine", "renderpass drawline" }
			)
		);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("///")]
	public void FileNameForTitleRejectsInvalidTitles(string title)
	{
		Assert.Throws<ArgumentException>(
			() => GalleryScreenshot.FileNameForTitle(title)
		);
	}
}
