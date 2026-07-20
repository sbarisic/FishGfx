using System;
using System.Numerics;

namespace FishGfx.Voxels;

public static partial class VoxelMesher
{
	private static readonly int[] FrontFaceOrder = { 0, 1, 2, 3, 0, 2 };
	private static readonly int[] BackFaceOrder = { 2, 1, 0, 2, 0, 3 };

	private static bool ShouldEmit(
		ushort materialId,
		VoxelMaterial material,
		ushort neighborId,
		VoxelPalette palette
	)
	{
		if (neighborId == 0)
		{
			return true;
		}

		if (!palette.Contains(neighborId))
		{
			throw new InvalidOperationException($"Chunk contains unknown voxel material ID {neighborId}.");
		}

		if (material.RenderMode == VoxelRenderMode.Transparent)
		{
			if (neighborId == materialId)
			{
				return false;
			}

			// A fully occluding neighbor emits the visible interface face. Emitting the
			// transparent side as well would place two triangles on the same plane.
			// Non-occluding cutouts still need this face behind their discarded texels.
			return !palette[neighborId].OccludesFaces;
		}

		return !palette[neighborId].OccludesFaces;
	}

	private static int WriteFace(
		VoxelChunkSnapshot snapshot,
		VoxelPalette palette,
		VoxelAtlasLayout atlas,
		VoxelMeshingOptions options,
		VoxelMaterial material,
		FaceDefinition face,
		VoxelLightChunkSnapshot lightSnapshot,
		int x,
		int y,
		int z,
		Vector3 blockPosition,
		Span<VoxelVertex> destination
	)
	{
		int tile = material.Tiles[face.Face];

		if (tile >= atlas.TileCount)
		{
			throw new InvalidOperationException(
				$"Voxel material '{material.Name}' references atlas tile {tile}, "
					+ $"but only {atlas.TileCount} tiles exist."
			);
		}

		VoxelAtlasUvBounds uvBounds = atlas.GetTileUvBounds(tile);
		bool animatedSurface = material.Wave.HasValue
			&& snapshot.GetMaterialUnchecked(x, y + 1, z) != snapshot.GetMaterialUnchecked(x, y, z);

		for (int i = 0; i < FrontFaceOrder.Length; i++)
		{
			int cornerIndex = FrontFaceOrder[i];
			Vector3 corner = face.GetCorner(cornerIndex);
			byte ao = options.AmbientOcclusion
				? CalculateAo(snapshot, palette, options, face, corner, x, y, z)
				: byte.MaxValue;
			destination[i] = new VoxelVertex(
				blockPosition + corner,
				ApplyAo(material.Tint, ao),
				MapFaceUv(face.GetUv(cornerIndex), uvBounds),
				face.Normal
			);
			destination[i].WaveParameters = CreateWaveData(material, face, corner, animatedSurface);
			destination[i].PackedLightChannels = SampleCubeLight(
				lightSnapshot,
				face,
				corner,
				material.Light.Emission,
				x,
				y,
				z
			);
		}

		Vector4 frontTangent = CalculateFaceTangent(
			destination[..FrontFaceOrder.Length],
			face.Normal
		);

		for (int index = 0; index < FrontFaceOrder.Length; index++)
		{
			destination[index].Tangent = frontTangent;
		}

		if (!material.DoubleSided)
		{
			return FrontFaceOrder.Length;
		}

		for (int i = 0; i < BackFaceOrder.Length; i++)
		{
			int cornerIndex = BackFaceOrder[i];
			Vector3 corner = face.GetCorner(cornerIndex);
			byte ao = options.AmbientOcclusion
				? CalculateAo(snapshot, palette, options, face, corner, x, y, z)
				: byte.MaxValue;
			int destinationIndex = FrontFaceOrder.Length + i;
			destination[destinationIndex] = new VoxelVertex(
				blockPosition + corner,
				ApplyAo(material.Tint, ao),
				MapFaceUv(face.GetUv(cornerIndex), uvBounds),
				-face.Normal
			);
			destination[destinationIndex].WaveParameters = CreateWaveData(
				material,
				face,
				corner,
				animatedSurface
			);
			destination[destinationIndex].PackedLightChannels = SampleCubeLight(
				lightSnapshot,
				face,
				corner,
				material.Light.Emission,
				x,
				y,
				z
			);
		}

		Span<VoxelVertex> backVertices = destination.Slice(
			FrontFaceOrder.Length,
			BackFaceOrder.Length
		);
		Vector4 backTangent = CalculateFaceTangent(backVertices, -face.Normal);

		for (int index = 0; index < backVertices.Length; index++)
		{
			backVertices[index].Tangent = backTangent;
		}

		return FrontFaceOrder.Length + BackFaceOrder.Length;
	}

	private static Vector4 CalculateFaceTangent(
		ReadOnlySpan<VoxelVertex> vertices,
		Vector3 normal
	)
	{
		Vector3 edge1 = vertices[1].Position - vertices[0].Position;
		Vector3 edge2 = vertices[2].Position - vertices[0].Position;
		Vector2 uv1 = vertices[1].TextureCoordinates - vertices[0].TextureCoordinates;
		Vector2 uv2 = vertices[2].TextureCoordinates - vertices[0].TextureCoordinates;
		float determinant = uv1.X * uv2.Y - uv1.Y * uv2.X;

		if (!float.IsFinite(determinant) || MathF.Abs(determinant) < 0.0000001f)
		{
			return Vector4.Zero;
		}

		float inverse = 1 / determinant;
		Vector3 tangent = (edge1 * uv2.Y - edge2 * uv1.Y) * inverse;
		Vector3 bitangent = (edge2 * uv1.X - edge1 * uv2.X) * inverse;
		tangent -= normal * Vector3.Dot(normal, tangent);
		float lengthSquared = tangent.LengthSquared();

		if (!float.IsFinite(lengthSquared) || lengthSquared < 0.0000001f)
		{
			return Vector4.Zero;
		}

		tangent /= MathF.Sqrt(lengthSquared);
		float handedness = Vector3.Dot(Vector3.Cross(normal, tangent), bitangent) < 0
			? -1
			: 1;
		return new Vector4(tangent, handedness);
	}

	private static Vector4 CreateWaveData(
		VoxelMaterial material,
		FaceDefinition face,
		Vector3 corner,
		bool animatedSurface
	)
	{
		if (
			!material.Wave.HasValue
			|| !animatedSurface
			|| face.Face == VoxelFace.NegativeY
		)
		{
			return Vector4.Zero;
		}

		float influence = face.Face == VoxelFace.PositiveY
			? 1
			: corner.Y < 0.5f
				? 0
				: 1;

		VoxelWaveSettings wave = material.Wave.Value;
		return new Vector4(
			wave.Amplitude,
			MathF.Tau / wave.Wavelength,
			MathF.Tau * wave.Speed,
			influence
		);
	}


	private static Vector3 FaceCenter(FaceDefinition face)
	{
		return (face.Q0 + face.Q1 + face.Q2 + face.Q3) / 4;
	}

	private static Vector2 MapFaceUv(
		Vector2 sourceUv,
		VoxelAtlasUvBounds bounds
	)
	{
		return new Vector2(
			float.Lerp(bounds.MinimumU, bounds.MaximumU, sourceUv.X),
			float.Lerp(bounds.MaximumV, bounds.MinimumV, sourceUv.Y)
		);
	}

	private readonly struct FaceDefinition
	{
		public FaceDefinition(
			VoxelFace face,
			Int3 neighbor,
			Vector3 normal,
			Vector3 tangentA,
			Vector3 tangentB,
			Vector3 q0,
			Vector3 q1,
			Vector3 q2,
			Vector3 q3,
			Vector2 uv0,
			Vector2 uv1,
			Vector2 uv2,
			Vector2 uv3
		)
		{
			Face = face;
			Neighbor = neighbor;
			Normal = normal;
			TangentA = tangentA;
			TangentB = tangentB;
			Q0 = q0;
			Q1 = q1;
			Q2 = q2;
			Q3 = q3;
			UV0 = uv0;
			UV1 = uv1;
			UV2 = uv2;
			UV3 = uv3;
		}

		public VoxelFace Face { get; }
		public Int3 Neighbor { get; }
		public Vector3 Normal { get; }
		public Vector3 TangentA { get; }
		public Vector3 TangentB { get; }
		public Vector3 Q0 { get; }
		public Vector3 Q1 { get; }
		public Vector3 Q2 { get; }
		public Vector3 Q3 { get; }
		public Vector2 UV0 { get; }
		public Vector2 UV1 { get; }
		public Vector2 UV2 { get; }
		public Vector2 UV3 { get; }

		public Vector3 GetCorner(int index)
		{
			return index switch
			{
				0 => Q0,
				1 => Q1,
				2 => Q2,
				3 => Q3,
				_ => throw new ArgumentOutOfRangeException(nameof(index)),
			};
		}

		public Vector2 GetUv(int index)
		{
			return index switch
			{
				0 => UV0,
				1 => UV1,
				2 => UV2,
				3 => UV3,
				_ => throw new ArgumentOutOfRangeException(nameof(index)),
			};
		}
	}

	private readonly struct Int3
	{
		public Int3(int x, int y, int z)
		{
			X = x;
			Y = y;
			Z = z;
		}

		public int X { get; }
		public int Y { get; }
		public int Z { get; }

		public static Int3 FromVector(Vector3 value)
		{
			return new Int3((int)value.X, (int)value.Y, (int)value.Z);
		}

		public static Int3 operator +(Int3 left, Int3 right)
		{
			return new Int3(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
		}

		public static Int3 operator *(Int3 value, int scalar)
		{
			return new Int3(value.X * scalar, value.Y * scalar, value.Z * scalar);
		}
	}
}
