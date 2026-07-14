using System;

namespace FishGfx.Voxels;

public readonly struct VoxelFogSettings : IEquatable<VoxelFogSettings>
{
	public static readonly VoxelFogSettings Disabled = new VoxelFogSettings(Color.Black, 0, 1);

	public VoxelFogSettings(Color color, float density, float lightMultiplier = 1)
	{
		if (!float.IsFinite(density) || density < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(density));
		}

		if (!float.IsFinite(lightMultiplier) || lightMultiplier < 0 || lightMultiplier > 1)
		{
			throw new ArgumentOutOfRangeException(nameof(lightMultiplier));
		}

		Color = color;
		Density = density;
		LightMultiplier = lightMultiplier;
	}

	public Color Color { get; }
	public float Density { get; }
	public float LightMultiplier { get; }
	public bool Enabled => Density > 0;

	public bool Equals(VoxelFogSettings other)
	{
		return Color == other.Color
			&& Density.Equals(other.Density)
			&& LightMultiplier.Equals(other.LightMultiplier);
	}

	public override bool Equals(object obj) => obj is VoxelFogSettings other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(Color, Density, LightMultiplier);

	public static bool operator ==(VoxelFogSettings left, VoxelFogSettings right) => left.Equals(right);
	public static bool operator !=(VoxelFogSettings left, VoxelFogSettings right) => !left.Equals(right);

	internal float CalculateFactor(float distance)
	{
		if (!float.IsFinite(distance) || distance < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(distance));
		}

		return Enabled ? 1 - MathF.Exp(-Density * distance) : 0;
	}
}
