using System;
using System.Numerics;

namespace FishGfx.Voxels;

public static partial class VoxelMesher
{
	private static Color SampleCubeLight(
		VoxelLightChunkSnapshot lightSnapshot,
		FaceDefinition face,
		Vector3 corner,
		VoxelBlockLight emission,
		int x,
		int y,
		int z
	)
	{
		if (lightSnapshot == null)
		{
			return new Color(0, 0, 0, byte.MaxValue);
		}

		int signA = CornerSign(corner, face.TangentA);
		int signB = CornerSign(corner, face.TangentB);
		Int3 outside = face.Neighbor;
		Int3 tangentA = Int3.FromVector(face.TangentA) * signA;
		Int3 tangentB = Int3.FromVector(face.TangentB) * signB;
		VoxelLight sample0 = GetLight(lightSnapshot, x, y, z, outside);
		VoxelLight sample1 = GetLight(lightSnapshot, x, y, z, outside + tangentA);
		VoxelLight sample2 = GetLight(lightSnapshot, x, y, z, outside + tangentB);
		VoxelLight sample3 = GetLight(lightSnapshot, x, y, z, outside + tangentA + tangentB);

		return AverageLight(sample0, sample1, sample2, sample3, emission);
	}

	private static Color SampleCustomModelLight(
		VoxelLightChunkSnapshot lightSnapshot,
		Vector3 blockPosition,
		Vector3 localPosition,
		VoxelBlockLight emission
	)
	{
		if (lightSnapshot == null)
		{
			return new Color(0, 0, 0, byte.MaxValue);
		}

		Vector3 clampedLocal = Vector3.Clamp(localPosition, Vector3.Zero, Vector3.One);
		Vector3 samplePosition = blockPosition + clampedLocal - new Vector3(0.5f);
		int x0 = (int)MathF.Floor(samplePosition.X);
		int y0 = (int)MathF.Floor(samplePosition.Y);
		int z0 = (int)MathF.Floor(samplePosition.Z);
		float tx = samplePosition.X - x0;
		float ty = samplePosition.Y - y0;
		float tz = samplePosition.Z - z0;
		float red = 0;
		float green = 0;
		float blue = 0;
		float sky = 0;

		for (int dz = 0; dz <= 1; dz++)
		{
			for (int dy = 0; dy <= 1; dy++)
			{
				for (int dx = 0; dx <= 1; dx++)
				{
					float weight = (dx == 0 ? 1 - tx : tx)
						* (dy == 0 ? 1 - ty : ty)
						* (dz == 0 ? 1 - tz : tz);
					VoxelLight sample = lightSnapshot.GetLightUnchecked(x0 + dx, y0 + dy, z0 + dz);
					red += sample.Block.Red * weight;
					green += sample.Block.Green * weight;
					blue += sample.Block.Blue * weight;
					sky += sample.Sky * weight;
				}
			}
		}

		red = MathF.Max(red, emission.Red);
		green = MathF.Max(green, emission.Green);
		blue = MathF.Max(blue, emission.Blue);

		return new Color(
			EncodeLightLevel(red),
			EncodeLightLevel(green),
			EncodeLightLevel(blue),
			EncodeLightLevel(sky)
		);
	}

	private static VoxelLight GetLight(
		VoxelLightChunkSnapshot snapshot,
		int x,
		int y,
		int z,
		Int3 offset
	)
	{
		return snapshot.GetLightUnchecked(x + offset.X, y + offset.Y, z + offset.Z);
	}

	private static Color AverageLight(
		VoxelLight sample0,
		VoxelLight sample1,
		VoxelLight sample2,
		VoxelLight sample3,
		VoxelBlockLight emission
	)
	{
		return new Color(
			Math.Max(
				EncodeAverage(sample0.Block.Red + sample1.Block.Red + sample2.Block.Red + sample3.Block.Red),
				EncodeLightLevel(emission.Red)
			),
			Math.Max(
				EncodeAverage(sample0.Block.Green + sample1.Block.Green + sample2.Block.Green + sample3.Block.Green),
				EncodeLightLevel(emission.Green)
			),
			Math.Max(
				EncodeAverage(sample0.Block.Blue + sample1.Block.Blue + sample2.Block.Blue + sample3.Block.Blue),
				EncodeLightLevel(emission.Blue)
			),
			EncodeAverage(sample0.Sky + sample1.Sky + sample2.Sky + sample3.Sky)
		);
	}

	private static byte EncodeAverage(int sum)
	{
		return (byte)((sum * 17 + 2) / 4);
	}

	private static byte EncodeLightLevel(float level)
	{
		return (byte)Math.Clamp((int)MathF.Round(level * 17), 0, byte.MaxValue);
	}

	private static byte CalculateAo(
		VoxelChunkSnapshot snapshot,
		VoxelPalette palette,
		VoxelMeshingOptions options,
		FaceDefinition face,
		Vector3 corner,
		int x,
		int y,
		int z
	)
	{
		int signA = CornerSign(corner, face.TangentA);
		int signB = CornerSign(corner, face.TangentB);
		Int3 normal = Int3.FromVector(face.Normal);
		Int3 tangentA = Int3.FromVector(face.TangentA) * signA;
		Int3 tangentB = Int3.FromVector(face.TangentB) * signB;
		bool sideA = Occludes(snapshot, palette, x, y, z, normal + tangentA);
		bool sideB = Occludes(snapshot, palette, x, y, z, normal + tangentB);
		bool cornerBlocked = Occludes(snapshot, palette, x, y, z, normal + tangentA + tangentB);
		int level = sideA && sideB ? 3 : (sideA ? 1 : 0) + (sideB ? 1 : 0) + (cornerBlocked ? 1 : 0);

		return level switch
		{
			0 => byte.MaxValue,
			1 => options.AoLevel1,
			2 => options.AoLevel2,
			_ => options.AoLevel3,
		};
	}

	private static bool Occludes(
		VoxelChunkSnapshot snapshot,
		VoxelPalette palette,
		int x,
		int y,
		int z,
		Int3 offset
	)
	{
		ushort materialId = snapshot.GetMaterialUnchecked(x + offset.X, y + offset.Y, z + offset.Z);

		if (materialId == 0)
		{
			return false;
		}

		if (!palette.Contains(materialId))
		{
			throw new InvalidOperationException($"Chunk contains unknown voxel material ID {materialId}.");
		}

		return palette[materialId].OccludesFaces;
	}

	private static int CornerSign(Vector3 corner, Vector3 tangent)
	{
		float coordinate = tangent.X != 0 ? corner.X : tangent.Y != 0 ? corner.Y : corner.Z;
		return coordinate > 0.5f ? 1 : -1;
	}

	private static Color ApplyAo(Color tint, byte ao)
	{
		return new Color(
			(byte)(tint.R * ao / 255),
			(byte)(tint.G * ao / 255),
			(byte)(tint.B * ao / 255),
			tint.A
		);
	}

}
