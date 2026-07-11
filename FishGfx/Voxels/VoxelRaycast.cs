using System;
using System.Numerics;

namespace FishGfx.Voxels
{
	public readonly struct VoxelRaycastHit
	{
		internal VoxelRaycastHit(
			int x,
			int y,
			int z,
			int normalX,
			int normalY,
			int normalZ,
			float distance,
			Vector3 position,
			VoxelCell voxel
		)
		{
			X = x;
			Y = y;
			Z = z;
			NormalX = normalX;
			NormalY = normalY;
			NormalZ = normalZ;
			Distance = distance;
			Position = position;
			Voxel = voxel;
		}

		public int X { get; }
		public int Y { get; }
		public int Z { get; }
		public int NormalX { get; }
		public int NormalY { get; }
		public int NormalZ { get; }
		public int AdjacentX => X + NormalX;
		public int AdjacentY => Y + NormalY;
		public int AdjacentZ => Z + NormalZ;
		public float Distance { get; }
		public Vector3 Position { get; }
		public VoxelCell Voxel { get; }
		public bool HasSurfaceNormal => NormalX != 0 || NormalY != 0 || NormalZ != 0;
	}

	public static class VoxelRaycast
	{
		public static bool Cast(
			VoxelWorld world,
			Vector3 origin,
			Vector3 direction,
			float maxDistance,
			out VoxelRaycastHit hit
		)
		{
			if (world == null)
				throw new ArgumentNullException(nameof(world));
			if (!IsFinite(origin))
				throw new ArgumentOutOfRangeException(nameof(origin));
			if (!IsFinite(direction) || direction.LengthSquared() <= 0)
				throw new ArgumentOutOfRangeException(nameof(direction));
			if (!float.IsFinite(maxDistance) || maxDistance < 0)
				throw new ArgumentOutOfRangeException(nameof(maxDistance));

			direction = Vector3.Normalize(direction);
			int x = FloorToInt(origin.X, nameof(origin));
			int y = FloorToInt(origin.Y, nameof(origin));
			int z = FloorToInt(origin.Z, nameof(origin));
			VoxelCell voxel = world.GetVoxel(x, y, z);

			if (!voxel.IsAir)
			{
				hit = new VoxelRaycastHit(x, y, z, 0, 0, 0, 0, origin, voxel);
				return true;
			}

			int stepX = Math.Sign(direction.X);
			int stepY = Math.Sign(direction.Y);
			int stepZ = Math.Sign(direction.Z);
			float deltaX = AxisDelta(direction.X);
			float deltaY = AxisDelta(direction.Y);
			float deltaZ = AxisDelta(direction.Z);
			float distanceX = AxisDistance(origin.X, direction.X, x, stepX);
			float distanceY = AxisDistance(origin.Y, direction.Y, y, stepY);
			float distanceZ = AxisDistance(origin.Z, direction.Z, z, stepZ);

			while (true)
			{
				float distance;
				int normalX = 0;
				int normalY = 0;
				int normalZ = 0;

				if (distanceX <= distanceY && distanceX <= distanceZ)
				{
					distance = distanceX;
					distanceX += deltaX;
					x += stepX;
					normalX = -stepX;
				}
				else if (distanceY <= distanceZ)
				{
					distance = distanceY;
					distanceY += deltaY;
					y += stepY;
					normalY = -stepY;
				}
				else
				{
					distance = distanceZ;
					distanceZ += deltaZ;
					z += stepZ;
					normalZ = -stepZ;
				}

				if (distance > maxDistance)
					break;

				voxel = world.GetVoxel(x, y, z);

				if (!voxel.IsAir)
				{
					hit = new VoxelRaycastHit(
						x,
						y,
						z,
						normalX,
						normalY,
						normalZ,
						distance,
						origin + direction * distance,
						voxel
					);
					return true;
				}
			}

			hit = default;
			return false;
		}

		private static float AxisDelta(float direction)
		{
			return direction == 0 ? float.PositiveInfinity : MathF.Abs(1 / direction);
		}

		private static float AxisDistance(float origin, float direction, int voxel, int step)
		{
			if (step == 0)
				return float.PositiveInfinity;

			float boundary = step > 0 ? voxel + 1 : voxel;
			return (boundary - origin) / direction;
		}

		private static int FloorToInt(float value, string parameterName)
		{
			float floor = MathF.Floor(value);

			if (floor < int.MinValue || floor > int.MaxValue)
				throw new ArgumentOutOfRangeException(parameterName);

			return (int)floor;
		}

		private static bool IsFinite(Vector3 value)
		{
			return float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
		}
	}
}
