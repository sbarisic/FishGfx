using System;
using System.Numerics;

namespace FishGfx;

/// <summary>
/// Converts authored display colors to and from the linear values used for
/// lighting and scene compositing. Alpha is always treated as linear coverage.
/// </summary>
public static class ColorSpace
{
	public static float SrgbToLinear(float value)
	{
		value = Math.Clamp(value, 0, 1);
		return value <= 0.04045f
			? value / 12.92f
			: MathF.Pow((value + 0.055f) / 1.055f, 2.4f);
	}

	public static float LinearToSrgb(float value)
	{
		value = Math.Clamp(value, 0, 1);
		return value <= 0.0031308f
			? value * 12.92f
			: 1.055f * MathF.Pow(value, 1 / 2.4f) - 0.055f;
	}

	public static Vector3 SrgbToLinear(Vector3 value)
	{
		return new Vector3(
			SrgbToLinear(value.X),
			SrgbToLinear(value.Y),
			SrgbToLinear(value.Z)
		);
	}

	public static Vector3 LinearToSrgb(Vector3 value)
	{
		return new Vector3(
			LinearToSrgb(value.X),
			LinearToSrgb(value.Y),
			LinearToSrgb(value.Z)
		);
	}

	public static Vector3 SrgbToLinear(Color value)
	{
		return SrgbToLinear((Vector3)value);
	}

	public static Color SrgbToLinearColor(Color value)
	{
		Vector3 linear = SrgbToLinear(value);
		return NewColor(linear.X, linear.Y, linear.Z, value.A / (float)byte.MaxValue);
	}

	public static Color LinearToSrgbColor(Color value)
	{
		Vector3 srgb = LinearToSrgb((Vector3)value);
		return NewColor(srgb.X, srgb.Y, srgb.Z, value.A / (float)byte.MaxValue);
	}

	private static Color NewColor(float red, float green, float blue, float alpha)
	{
		return new Color(
			(byte)Math.Clamp(MathF.Round(red * byte.MaxValue), 0, byte.MaxValue),
			(byte)Math.Clamp(MathF.Round(green * byte.MaxValue), 0, byte.MaxValue),
			(byte)Math.Clamp(MathF.Round(blue * byte.MaxValue), 0, byte.MaxValue),
			(byte)Math.Clamp(MathF.Round(alpha * byte.MaxValue), 0, byte.MaxValue)
		);
	}
}
