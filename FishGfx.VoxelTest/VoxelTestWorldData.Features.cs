using System;
using System.Numerics;
using FishGfx.Voxels;

namespace FishGfx.VoxelTest;

internal sealed partial class VoxelTestWorldData
{
	private void OverlayTrees(
		VoxelCell[] cells,
		ChunkCoordinate coordinate,
		VoxelTestMaterialIds materials
	)
	{
		int originX = coordinate.X * VoxelWorld.ChunkSize;
		int originY = coordinate.Y * VoxelWorld.ChunkSize;
		int originZ = coordinate.Z * VoxelWorld.ChunkSize;

		foreach ((int rootX, int rootY, int rootZ) in GetTreeRoots(
			originX - 2,
			originZ - 2,
			originX + VoxelWorld.ChunkSize + 1,
			originZ + VoxelWorld.ChunkSize + 1
		))
		{
			for (int y = rootY; y < rootY + 5; y++)
			{
				SetIfInside(cells, coordinate, rootX, y, rootZ, materials.Wood);
			}

			for (int offsetY = 3; offsetY <= 6; offsetY++)
			{
				for (int offsetZ = -2; offsetZ <= 2; offsetZ++)
				{
					for (int offsetX = -2; offsetX <= 2; offsetX++)
					{
						if (Math.Abs(offsetX) + Math.Abs(offsetZ) < 4)
						{
							SetIfInside(
								cells,
								coordinate,
								rootX + offsetX,
								rootY + offsetY,
								rootZ + offsetZ,
								materials.Leaves
							);
						}
					}
				}
			}
		}
	}

	private void OverlayValidationFeatures(
		VoxelCell[] cells,
		ChunkCoordinate coordinate,
		VoxelTestMaterialIds materials
	)
	{
		for (
			int x = VoxelTestWorldGenerator.GlassMinimumX;
			x <= VoxelTestWorldGenerator.GlassMaximumX;
			x++
		)
		{
			int minimumY = GetSurfaceHeight(x, VoxelTestWorldGenerator.GlassZ) + 1;

			for (int y = minimumY; y < minimumY + VoxelTestWorldGenerator.GlassHeight; y++)
			{
				SetIfInside(
					cells,
					coordinate,
					x,
					y,
					VoxelTestWorldGenerator.GlassZ,
					materials.Glass
				);
			}
		}

		SetIfInside(
			cells,
			coordinate,
			VoxelTestWorldGenerator.BoundaryEditX,
			VoxelTestWorldGenerator.BoundaryEditY,
			VoxelTestWorldGenerator.BoundaryEditZ,
			materials.Glass
		);

		for (int index = 0; index < materials.Placeable.Count; index++)
		{
			(int x, int y, int z) = GetShowcasePosition(index);
			SetIfInside(cells, coordinate, x, y, z, materials.Placeable[index].Id);
		}

		for (int index = 0; index < VoxelTestWorldGenerator.OrientationShowcaseCount; index++)
		{
			(int x, int y, int z) = GetOrientationShowcasePosition(index);
			ushort material = VoxelTestWorldGenerator.GetOrientationShowcaseMaterial(
				materials,
				index
			);
			SetIfInside(cells, coordinate, x, y, z, material);
		}
	}

	private int CalculateShowcaseY()
	{
		int maximumY = int.MinValue;

		for (int z = ShowcaseOriginZ; z <= ShowcaseOriginZ + 6; z++)
		{
			for (int x = ShowcaseOriginX; x <= ShowcaseOriginX + 20; x++)
			{
				maximumY = Math.Max(maximumY, GetSurfaceHeight(x, z));

				if (GetWaterSurface(x, z) is int waterSurface)
				{
					maximumY = Math.Max(maximumY, waterSurface);
				}
			}
		}

		return maximumY + 4;
	}

	private bool IsDry(int worldX, int worldZ, int clearance)
	{
		if (
			worldX < VoxelTestWorldGenerator.WorldMinimum + clearance
				|| worldX >= VoxelTestWorldGenerator.WorldMaximum - clearance
				|| worldZ < VoxelTestWorldGenerator.WorldMinimum + clearance
				|| worldZ >= VoxelTestWorldGenerator.WorldMaximum - clearance
		)
		{
			return false;
		}

		for (int z = worldZ - clearance; z <= worldZ + clearance; z++)
		{
			for (int x = worldX - clearance; x <= worldX + clearance; x++)
			{
				if (GetWaterSurface(x, z).HasValue)
				{
					return false;
				}
			}
		}

		return true;
	}

	private Vector3 FindUnderwaterCameraPosition()
	{
		int bestDepth = 0;
		Vector3 result = default;

		for (int z = 0; z < VoxelTestWorldGenerator.WorldSize; z++)
		{
			for (int x = 0; x < VoxelTestWorldGenerator.WorldSize; x++)
			{
				int waterSurface = waterSurfaces[x, z];

				if (waterSurface == int.MinValue)
				{
					continue;
				}

				int depth = waterSurface - surfaceHeights[x, z];

				if (depth <= bestDepth)
				{
					continue;
				}

				bestDepth = depth;
				result = new Vector3(
					x + VoxelTestWorldGenerator.WorldMinimum + 0.5f,
					surfaceHeights[x, z] + 1.5f,
					z + VoxelTestWorldGenerator.WorldMinimum + 0.5f
				);
			}
		}

		if (bestDepth == 0)
		{
			throw new InvalidOperationException(
				"The validation world does not contain an underwater camera position."
			);
		}

		return result;
	}

	private static void SetIfInside(
		VoxelCell[] cells,
		ChunkCoordinate coordinate,
		int worldX,
		int worldY,
		int worldZ,
		ushort material
	)
	{
		ChunkCoordinate target = ChunkCoordinate.FromWorld(
			worldX,
			worldY,
			worldZ,
			out int localX,
			out int localY,
			out int localZ
		);

		if (target != coordinate)
		{
			return;
		}

		cells[Index(localX, localY, localZ)] = new VoxelCell(material);
	}

	private static bool IntersectsGlassWall(int minimumX, int minimumZ)
	{
		return VoxelTestWorldGenerator.GlassZ >= minimumZ
			&& VoxelTestWorldGenerator.GlassZ < minimumZ + VoxelWorld.ChunkSize
			&& VoxelTestWorldGenerator.GlassMaximumX >= minimumX
			&& VoxelTestWorldGenerator.GlassMinimumX < minimumX + VoxelWorld.ChunkSize;
	}

	private static bool ContainsBoundaryEdit(int minimumX, int minimumZ)
	{
		return VoxelTestWorldGenerator.BoundaryEditX >= minimumX
			&& VoxelTestWorldGenerator.BoundaryEditX < minimumX + VoxelWorld.ChunkSize
			&& VoxelTestWorldGenerator.BoundaryEditZ >= minimumZ
			&& VoxelTestWorldGenerator.BoundaryEditZ < minimumZ + VoxelWorld.ChunkSize;
	}

	private static bool IsInsideWorld(int x, int z)
	{
		return x >= VoxelTestWorldGenerator.WorldMinimum
			&& x < VoxelTestWorldGenerator.WorldMaximum
			&& z >= VoxelTestWorldGenerator.WorldMinimum
			&& z < VoxelTestWorldGenerator.WorldMaximum;
	}

	private static int ToIndex(int coordinate)
	{
		if (
			coordinate < VoxelTestWorldGenerator.WorldMinimum
				|| coordinate >= VoxelTestWorldGenerator.WorldMaximum
		)
		{
			throw new ArgumentOutOfRangeException(nameof(coordinate));
		}

		return coordinate - VoxelTestWorldGenerator.WorldMinimum;
	}

	private static int Index(int x, int y, int z)
	{
		return x + VoxelWorld.ChunkSize * (y + VoxelWorld.ChunkSize * z);
	}

	private static int FloorDivide(int value, int divisor)
	{
		int quotient = Math.DivRem(value, divisor, out int remainder);

		return remainder < 0 ? quotient - 1 : quotient;
	}

	private static uint Hash(int x, int z)
	{
		uint value = unchecked((uint)(x * 0x1f1f1f1f) ^ (uint)(z * 0x6c8e9cf5));
		value ^= value >> 16;
		value *= 0x7feb352d;
		value ^= value >> 15;
		value *= 0x846ca68b;

		return value ^ (value >> 16);
	}
}
