using System;
using System.Collections.Generic;

namespace FishGfx.Voxels;

public readonly struct VoxelBlockLight : IEquatable<VoxelBlockLight>
{
	public VoxelBlockLight(byte red, byte green, byte blue)
	{
		if (red > 15)
		{
			throw new ArgumentOutOfRangeException(nameof(red));
		}

		if (green > 15)
		{
			throw new ArgumentOutOfRangeException(nameof(green));
		}

		if (blue > 15)
		{
			throw new ArgumentOutOfRangeException(nameof(blue));
		}

		Red = red;
		Green = green;
		Blue = blue;
	}

	public byte Red { get; }
	public byte Green { get; }
	public byte Blue { get; }
	public bool IsDark => Red == 0 && Green == 0 && Blue == 0;

	public bool Equals(VoxelBlockLight other)
	{
		return Red == other.Red && Green == other.Green && Blue == other.Blue;
	}

	public override bool Equals(object obj) => obj is VoxelBlockLight other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(Red, Green, Blue);
	public static bool operator ==(VoxelBlockLight left, VoxelBlockLight right) => left.Equals(right);
	public static bool operator !=(VoxelBlockLight left, VoxelBlockLight right) => !left.Equals(right);
}

public readonly struct VoxelLight : IEquatable<VoxelLight>
{
	public VoxelLight(VoxelBlockLight block, byte sky)
	{
		if (sky > 15)
		{
			throw new ArgumentOutOfRangeException(nameof(sky));
		}

		Packed = Pack(block.Red, block.Green, block.Blue, sky);
	}

	internal VoxelLight(ushort packed)
	{
		Packed = packed;
	}

	public VoxelBlockLight Block => new VoxelBlockLight(
		(byte)(Packed & 0xf),
		(byte)((Packed >> 4) & 0xf),
		(byte)((Packed >> 8) & 0xf)
	);

	public byte Sky => (byte)((Packed >> 12) & 0xf);
	internal ushort Packed { get; }

	public bool Equals(VoxelLight other) => Packed == other.Packed;
	public override bool Equals(object obj) => obj is VoxelLight other && Equals(other);
	public override int GetHashCode() => Packed;
	public static bool operator ==(VoxelLight left, VoxelLight right) => left.Equals(right);
	public static bool operator !=(VoxelLight left, VoxelLight right) => !left.Equals(right);

	internal static ushort Pack(byte red, byte green, byte blue, byte sky)
	{
		return (ushort)(red | (green << 4) | (blue << 8) | (sky << 12));
	}
}

public readonly struct VoxelMaterialLightSettings : IEquatable<VoxelMaterialLightSettings>
{
	public VoxelMaterialLightSettings(byte opacity, VoxelBlockLight emission = default)
	{
		if (opacity > 15)
		{
			throw new ArgumentOutOfRangeException(nameof(opacity));
		}

		Opacity = opacity;
		Emission = emission;
	}

	public byte Opacity { get; }
	public VoxelBlockLight Emission { get; }

	public bool Equals(VoxelMaterialLightSettings other)
	{
		return Opacity == other.Opacity && Emission == other.Emission;
	}

	public override bool Equals(object obj) =>
		obj is VoxelMaterialLightSettings other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(Opacity, Emission);
	public static bool operator ==(
		VoxelMaterialLightSettings left,
		VoxelMaterialLightSettings right
	) => left.Equals(right);
	public static bool operator !=(
		VoxelMaterialLightSettings left,
		VoxelMaterialLightSettings right
	) => !left.Equals(right);
}
