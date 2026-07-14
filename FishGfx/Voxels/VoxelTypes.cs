using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace FishGfx.Voxels
{
	public enum VoxelRenderMode
	{
		Opaque,
		Cutout,
		Transparent,
	}

	public enum VoxelFace
	{
		PositiveX,
		NegativeX,
		PositiveY,
		NegativeY,
		PositiveZ,
		NegativeZ,
	}

	/// <summary>
	/// Describes the directional skylight used when shading lit voxel geometry.
	/// </summary>
	public readonly struct VoxelSunSettings : IEquatable<VoxelSunSettings>
	{
		public VoxelSunSettings(
			Vector3 direction,
			Color color,
			float intensity,
			float ambientLight
		)
		{
			float lengthSquared = direction.LengthSquared();
			if (!IsFinite(direction) || !float.IsFinite(lengthSquared) || lengthSquared <= 0)
				throw new ArgumentOutOfRangeException(nameof(direction));
			if (!float.IsFinite(intensity) || intensity < 0)
				throw new ArgumentOutOfRangeException(nameof(intensity));
			if (!float.IsFinite(ambientLight) || ambientLight < 0 || ambientLight > 1)
				throw new ArgumentOutOfRangeException(nameof(ambientLight));

			Direction = Vector3.Normalize(direction);
			Color = color;
			Intensity = intensity;
			AmbientLight = ambientLight;
		}

		public Vector3 Direction { get; }
		public Color Color { get; }
		public float Intensity { get; }
		public float AmbientLight { get; }

		public bool Equals(VoxelSunSettings other)
		{
			return Direction == other.Direction
				&& Color == other.Color
				&& Intensity == other.Intensity
				&& AmbientLight == other.AmbientLight;
		}

		public override bool Equals(object obj) => obj is VoxelSunSettings other && Equals(other);
		public override int GetHashCode() => HashCode.Combine(Direction, Color, Intensity, AmbientLight);
		public static bool operator ==(VoxelSunSettings left, VoxelSunSettings right) => left.Equals(right);
		public static bool operator !=(VoxelSunSettings left, VoxelSunSettings right) => !left.Equals(right);

		internal void Validate(string parameterName)
		{
			if (
				!IsFinite(Direction)
				|| !float.IsFinite(Direction.LengthSquared())
				|| Direction.LengthSquared() <= 0
				|| !float.IsFinite(Intensity)
				|| Intensity < 0
				|| !float.IsFinite(AmbientLight)
				|| AmbientLight < 0
				|| AmbientLight > 1
			)
				throw new ArgumentException("Voxel sun settings are invalid.", parameterName);
		}

		private static bool IsFinite(Vector3 value)
		{
			return float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
		}
	}

	public readonly struct ChunkCoordinate : IEquatable<ChunkCoordinate>
	{
		public ChunkCoordinate(int x, int y, int z)
		{
			X = x;
			Y = y;
			Z = z;
		}

		public int X { get; }
		public int Y { get; }
		public int Z { get; }
		public Vector3 WorldOrigin => new Vector3(X, Y, Z) * VoxelWorld.ChunkSize;

		public static ChunkCoordinate FromWorld(int x, int y, int z, out int localX, out int localY, out int localZ)
		{
			int chunkX = FloorDivide(x, VoxelWorld.ChunkSize, out localX);
			int chunkY = FloorDivide(y, VoxelWorld.ChunkSize, out localY);
			int chunkZ = FloorDivide(z, VoxelWorld.ChunkSize, out localZ);

			return new ChunkCoordinate(chunkX, chunkY, chunkZ);
		}

		public bool Equals(ChunkCoordinate other) => X == other.X && Y == other.Y && Z == other.Z;
		public override bool Equals(object obj) => obj is ChunkCoordinate other && Equals(other);
		public override int GetHashCode() => HashCode.Combine(X, Y, Z);
		public override string ToString() => $"({X}, {Y}, {Z})";

		public static ChunkCoordinate operator +(ChunkCoordinate left, ChunkCoordinate right)
		{
			return new ChunkCoordinate(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
		}

		public static bool operator ==(ChunkCoordinate left, ChunkCoordinate right) => left.Equals(right);
		public static bool operator !=(ChunkCoordinate left, ChunkCoordinate right) => !left.Equals(right);

		private static int FloorDivide(int value, int divisor, out int remainder)
		{
			int quotient = Math.DivRem(value, divisor, out remainder);

			if (remainder < 0)
			{
				remainder += divisor;
				quotient--;
			}

			return quotient;
		}
	}

	public readonly struct VoxelCell : IEquatable<VoxelCell>
	{
		public static readonly VoxelCell Air = new VoxelCell(0);

		public VoxelCell(ushort materialId)
		{
			MaterialId = materialId;
		}

		public ushort MaterialId { get; }
		public bool IsAir => MaterialId == 0;

		public bool Equals(VoxelCell other) => MaterialId == other.MaterialId;
		public override bool Equals(object obj) => obj is VoxelCell other && Equals(other);
		public override int GetHashCode() => MaterialId;

		public static bool operator ==(VoxelCell left, VoxelCell right) => left.Equals(right);
		public static bool operator !=(VoxelCell left, VoxelCell right) => !left.Equals(right);
	}

	public readonly struct VoxelFaceTiles
	{
		public VoxelFaceTiles(int uniformTile)
			: this(uniformTile, uniformTile, uniformTile, uniformTile, uniformTile, uniformTile)
		{
		}

		public VoxelFaceTiles(
			int positiveX,
			int negativeX,
			int positiveY,
			int negativeY,
			int positiveZ,
			int negativeZ
		)
		{
			PositiveX = Validate(positiveX, nameof(positiveX));
			NegativeX = Validate(negativeX, nameof(negativeX));
			PositiveY = Validate(positiveY, nameof(positiveY));
			NegativeY = Validate(negativeY, nameof(negativeY));
			PositiveZ = Validate(positiveZ, nameof(positiveZ));
			NegativeZ = Validate(negativeZ, nameof(negativeZ));
		}

		public int PositiveX { get; }
		public int NegativeX { get; }
		public int PositiveY { get; }
		public int NegativeY { get; }
		public int PositiveZ { get; }
		public int NegativeZ { get; }

		public int this[VoxelFace face] => face switch
		{
			VoxelFace.PositiveX => PositiveX,
			VoxelFace.NegativeX => NegativeX,
			VoxelFace.PositiveY => PositiveY,
			VoxelFace.NegativeY => NegativeY,
			VoxelFace.PositiveZ => PositiveZ,
			VoxelFace.NegativeZ => NegativeZ,
			_ => throw new ArgumentOutOfRangeException(nameof(face)),
		};

		private static int Validate(int tile, string name)
		{
			if (tile < 0)
				throw new ArgumentOutOfRangeException(name);

			return tile;
		}
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public struct VoxelVertex
	{
		public VoxelVertex(Vector3 position, Color color, Vector2 uv, Vector3 normal)
		{
			Position = position;
			Color = color;
			UV = uv;
			Normal = normal;
			Wave = Vector4.Zero;
			PackedLight = new Color(0, 0, 0, byte.MaxValue);
		}

		public Vector3 Position;
		public Color Color;
		public Vector2 UV;
		public Vector3 Normal;
		internal Vector4 Wave;
		internal Color PackedLight;
	}
}
