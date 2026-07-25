using Xunit;

namespace FishGfx.ManifoldCad.Tests;

public sealed class WrappedLabelTests
{
	[Fact]
	public void WrapTextKeepsEveryLineWithinTheAvailableWidth()
	{
		IReadOnlyList<string> lines = WrappedLabel.WrapText(
			"Collector 1: Evaluate exact runner: The cubic curve failed validation.",
			20,
			value => value.Length
		);

		Assert.All(lines, line => Assert.InRange(line.Length, 0, 20));
		Assert.Equal(
			"Collector 1: Evaluate exact runner: The cubic curve failed validation.",
			string.Join(' ', lines)
		);
	}

	[Fact]
	public void WrapTextSplitsAWordThatIsWiderThanTheControl()
	{
		IReadOnlyList<string> lines = WrappedLabel.WrapText(
			"0123456789",
			4,
			value => value.Length
		);

		Assert.Equal(new[] { "0123", "4567", "89" }, lines);
	}
}
