using System.Numerics;
using Xunit;

namespace FishGfx.Tests;

public sealed class ColorSpaceTests
{
	[Theory]
	[InlineData(0f, 0f)]
	[InlineData(0.04045f, 0.0031308f)]
	[InlineData(0.5f, 0.21404114f)]
	[InlineData(1f, 1f)]
	public void SrgbTransferFunctionMatchesReferenceValues(float srgb, float expected)
	{
		Assert.InRange(ColorSpace.SrgbToLinear(srgb), expected - 0.00001f, expected + 0.00001f);
	}

	[Theory]
	[InlineData(0f)]
	[InlineData(0.0031308f)]
	[InlineData(0.18f)]
	[InlineData(0.5f)]
	[InlineData(1f)]
	public void TransferFunctionsRoundTrip(float linear)
	{
		float encoded = ColorSpace.LinearToSrgb(linear);
		Assert.InRange(ColorSpace.SrgbToLinear(encoded), linear - 0.00001f, linear + 0.00001f);
	}

	[Fact]
	public void ColorConversionPreservesAlphaAndConvertsRgb()
	{
		Color converted = ColorSpace.SrgbToLinearColor(new Color(128, 64, 255, 73));

		Assert.Equal((byte)73, converted.A);
		Assert.InRange(converted.R, (byte)54, (byte)56);
		Assert.InRange(converted.G, (byte)12, (byte)14);
		Assert.Equal(byte.MaxValue, converted.B);
		Vector3 linear = ColorSpace.SrgbToLinear(new Color(128, 64, 255, 73));
		Assert.InRange(linear.X, 0.215f, 0.217f);
	}
}
