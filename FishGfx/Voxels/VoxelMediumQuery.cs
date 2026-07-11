using System;
using System.Numerics;

namespace FishGfx.Voxels
{
	public static class VoxelMediumQuery
	{
		public static VoxelCell GetVoxel(VoxelWorld world, Vector3 position)
		{
			if (world == null)
				throw new ArgumentNullException(nameof(world));
			if (!IsFinite(position))
				throw new ArgumentOutOfRangeException(nameof(position));

			return world.GetVoxel(
				FloorToInt(position.X, nameof(position)),
				FloorToInt(position.Y, nameof(position)),
				FloorToInt(position.Z, nameof(position))
			);
		}

		public static bool IsInsideMaterial(VoxelWorld world, Vector3 position, ushort materialId)
		{
			return GetVoxel(world, position).MaterialId == materialId;
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
