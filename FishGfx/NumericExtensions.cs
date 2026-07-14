using System;

namespace FishGfx;

public static class NumericExtensions
{
	public static float Clamp(this float value, float minimum, float maximum)
	{
		return Math.Clamp(value, minimum, maximum);
	}
}
