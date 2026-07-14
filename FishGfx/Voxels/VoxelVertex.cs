using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace FishGfx.Voxels;

/// <summary>
/// Packed GPU vertex for voxel geometry. Field order and offsets are part of the shader contract.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct VoxelVertex
{
	public VoxelVertex(
		Vector3 position,
		Color color,
		Vector2 textureCoordinates,
		Vector3 normal
	)
	{
		Position = position;
		Color = color;
		TextureCoordinates = textureCoordinates;
		Normal = normal;
		WaveParameters = Vector4.Zero;
		PackedLightChannels = new Color(0, 0, 0, byte.MaxValue);
	}

	public Vector3 Position;
	public Color Color;
	public Vector2 TextureCoordinates;
	public Vector3 Normal;

	/// <summary>
	/// Amplitude, wave number, angular speed, and vertex influence consumed by the wave shader.
	/// </summary>
	internal Vector4 WaveParameters;

	/// <summary>
	/// Normalized red, green, blue, and sky light channels consumed by the voxel shaders.
	/// </summary>
	internal Color PackedLightChannels;
}
