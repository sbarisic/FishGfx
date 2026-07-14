using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;

namespace FishGfx;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Color : IEquatable<Color>
{
	public static Color Transparent { get; } = new(0, 0, 0, 0);

	public static Color White { get; } = new(255, 255, 255, 255);

	public static Color Black { get; } = new(0, 0, 0, 255);

	public static Color Red { get; } = new(255, 0, 0);

	public static Color Green { get; } = new(0, 255, 0);

	public static Color Blue { get; } = new(0, 0, 255);

	public static Color Yellow { get; } = new(255, 255, 0);

	public static Color Cyan { get; } = new(0, 255, 255);

	public static Color Magenta { get; } = new(255, 0, 255);

	public static Color Orange { get; } = new(230, 140, 0);

	public static Color Amber { get; } = new(137, 49, 1);

	public static Color Apple { get; } = new(169, 27, 13);

	public static Color Pine { get; } = new(35, 79, 30);

	public static Color Coal { get; } = new(11, 10, 8);

	public static Color Sky { get; } = new(98, 197, 218);

	public byte R;

	public byte G;

	public byte B;

	public byte A;

	public Color(byte red, byte green, byte blue, byte alpha)
	{
		R = red;
		G = green;
		B = blue;
		A = alpha;
	}

	public Color(byte red, byte green, byte blue)
		: this(red, green, blue, byte.MaxValue)
	{
	}

	public Color(int packedValue)
		: this(0, 0, 0, 0)
	{
		PackedValue = packedValue;
	}

	public Color(float red, float green, float blue)
		: this(ToByte(red), ToByte(green), ToByte(blue))
	{
	}

	public Color(double red, double green, double blue)
		: this((float)red, (float)green, (float)blue)
	{
	}

	public int PackedValue
	{
		get
		{
			return A << 24 | B << 16 | G << 8 | R;
		}
		set
		{
			A = (byte)(value >> 24 & 0xFF);
			B = (byte)(value >> 16 & 0xFF);
			G = (byte)(value >> 8 & 0xFF);
			R = (byte)(value & 0xFF);
		}
	}

	public static Color ClampToPalette(Color color, IEnumerable<Color> palette)
	{
		ArgumentNullException.ThrowIfNull(palette);

		bool found = false;
		long bestDistance = long.MaxValue;
		Color best = default;

		foreach (Color candidate in palette)
		{
			long red = color.R - candidate.R;
			long green = color.G - candidate.G;
			long blue = color.B - candidate.B;
			long alpha = color.A - candidate.A;
			long distance = red * red + green * green + blue * blue + alpha * alpha;

			if (!found || distance < bestDistance)
			{
				found = true;
				bestDistance = distance;
				best = candidate;
			}
		}

		if (!found)
		{
			throw new ArgumentException("The palette cannot be empty.", nameof(palette));
		}

		return best;
	}

	public bool Equals(Color other)
	{
		return R == other.R && G == other.G && B == other.B && A == other.A;
	}

	public override bool Equals(object obj)
	{
		return obj is Color other && Equals(other);
	}

	public override int GetHashCode()
	{
		return PackedValue;
	}

	public override string ToString()
	{
		return $"({R} {G} {B} {A})";
	}

	public static bool operator ==(Color left, Color right)
	{
		return left.Equals(right);
	}

	public static bool operator !=(Color left, Color right)
	{
		return !left.Equals(right);
	}

	public static Color operator *(Color color, float scale)
	{
		return (Vector3)color * scale;
	}

	public static Color operator *(Color left, Color right)
	{
		Vector3 leftVector = left;
		Vector3 rightVector = right;

		return new Color(
			leftVector.X * rightVector.X,
			leftVector.Y * rightVector.Y,
			leftVector.Z * rightVector.Z
		);
	}

	public static implicit operator System.Drawing.Color(Color color)
	{
		return System.Drawing.Color.FromArgb(color.A, color.R, color.G, color.B);
	}

	public static implicit operator Color(System.Drawing.Color color)
	{
		return new Color(color.R, color.G, color.B, color.A);
	}

	public static implicit operator Vector3(Color color)
	{
		return new Vector3(color.R, color.G, color.B) / byte.MaxValue;
	}

	public static implicit operator Vector4(Color color)
	{
		return new Vector4(color.R, color.G, color.B, color.A) / byte.MaxValue;
	}

	public static implicit operator Color(Vector3 value)
	{
		return new Color(ToByte(value.X), ToByte(value.Y), ToByte(value.Z));
	}

	private static byte ToByte(float value)
	{
		return (byte)(Math.Clamp(value, 0, 1) * byte.MaxValue);
	}
}
