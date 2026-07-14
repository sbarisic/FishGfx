using System;
using System.Collections.Generic;
using System.Numerics;
using FishGfx.Voxels;

namespace FishGfx.VoxelTest;

internal sealed partial class VoxelTestWorldData
{
	private const int TreeSpacing = 32;
	private const int DirtDepth = 3;
	private readonly int[,] surfaceHeights;
	private readonly int[,] waterSurfaces;

	internal VoxelTestWorldData(
		int[,] surfaceHeights,
		int[,] waterSurfaces,
		int lakeCount,
		int waterColumnCount
	)
	{
		this.surfaceHeights = surfaceHeights;
		this.waterSurfaces = waterSurfaces;
		LakeCount = lakeCount;
		WaterColumnCount = waterColumnCount;
		UnderwaterCameraPosition = FindUnderwaterCameraPosition();
		ShowcaseOriginX = (int)MathF.Floor(UnderwaterCameraPosition.X) - 10;
		ShowcaseOriginZ = (int)MathF.Floor(UnderwaterCameraPosition.Z) - 8;
		ShowcaseY = CalculateShowcaseY();
		ShowcaseTarget = new Vector3(ShowcaseOriginX + 10, ShowcaseY + 0.5f, ShowcaseOriginZ + 3);
		ShowcaseCameraPosition = ShowcaseTarget + new Vector3(0, 11, -26);
		ShowcaseSouthCameraPosition = ShowcaseTarget + new Vector3(0, 11, 26);
	}

	internal int LakeCount { get; }
	internal int WaterColumnCount { get; }
	internal Vector3 UnderwaterCameraPosition { get; }
	internal Vector3 ShowcaseCameraPosition { get; }
	internal Vector3 ShowcaseSouthCameraPosition { get; }
	internal Vector3 ShowcaseTarget { get; }
	internal int ShowcaseOriginX { get; }
	internal int ShowcaseOriginZ { get; }
	internal int ShowcaseY { get; }

	internal (int X, int Y, int Z) GetShowcasePosition(int index)
	{
		if (index < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(index));
		}

		return (
			ShowcaseOriginX + index % 11 * 2,
			ShowcaseY,
			ShowcaseOriginZ + index / 11 * 3
		);
	}

	internal (int X, int Y, int Z) GetOrientationShowcasePosition(int index)
	{
		if (index < 0 || index >= VoxelTestWorldGenerator.OrientationShowcaseCount)
		{
			throw new ArgumentOutOfRangeException(nameof(index));
		}

		return (ShowcaseOriginX + 1 + index * 3, ShowcaseY, ShowcaseOriginZ + 6);
	}

	internal int GetSurfaceHeight(int worldX, int worldZ)
	{
		return surfaceHeights[ToIndex(worldX), ToIndex(worldZ)];
	}

	internal int? GetWaterSurface(int worldX, int worldZ)
	{
		int surface = waterSurfaces[ToIndex(worldX), ToIndex(worldZ)];
		return surface == int.MinValue ? null : surface;
	}

	internal (int Minimum, int Maximum) GetVerticalChunkRange(int chunkX, int chunkZ)
	{
		int maximumY = VoxelTestWorldGenerator.TerrainBottom;
		int minimumX = chunkX * VoxelWorld.ChunkSize;
		int minimumZ = chunkZ * VoxelWorld.ChunkSize;

		for (int z = minimumZ; z < minimumZ + VoxelWorld.ChunkSize; z++)
		{
			for (int x = minimumX; x < minimumX + VoxelWorld.ChunkSize; x++)
			{
				maximumY = Math.Max(maximumY, GetSurfaceHeight(x, z) + 8);

				if (GetWaterSurface(x, z) is int waterSurface)
				{
					maximumY = Math.Max(maximumY, waterSurface);
				}
			}
		}

		if (IntersectsGlassWall(minimumX, minimumZ))
		{
			for (
				int x = Math.Max(minimumX, VoxelTestWorldGenerator.GlassMinimumX);
				x <= Math.Min(minimumX + VoxelWorld.ChunkSize - 1, VoxelTestWorldGenerator.GlassMaximumX);
				x++
			)
			{
				maximumY = Math.Max(
					maximumY,
					GetSurfaceHeight(x, VoxelTestWorldGenerator.GlassZ) + VoxelTestWorldGenerator.GlassHeight
				);
			}
		}

		if (ContainsBoundaryEdit(minimumX, minimumZ))
		{
			maximumY = Math.Max(maximumY, VoxelTestWorldGenerator.BoundaryEditY);
		}

		if (
			minimumX <= ShowcaseOriginX + 20
			&& minimumX + VoxelWorld.ChunkSize - 1 >= ShowcaseOriginX
			&& minimumZ <= ShowcaseOriginZ + 6
			&& minimumZ + VoxelWorld.ChunkSize - 1 >= ShowcaseOriginZ
		)
		{
			maximumY = Math.Max(maximumY, ShowcaseY);
		}

		return (
			FloorDivide(VoxelTestWorldGenerator.TerrainBottom, VoxelWorld.ChunkSize),
			FloorDivide(maximumY, VoxelWorld.ChunkSize)
		);
	}

	internal VoxelCell[] GenerateChunk(ChunkCoordinate coordinate, VoxelTestMaterialIds materials)
	{
		VoxelCell[] cells = new VoxelCell[VoxelWorld.ChunkVolume];
		int originX = coordinate.X * VoxelWorld.ChunkSize;
		int originY = coordinate.Y * VoxelWorld.ChunkSize;
		int originZ = coordinate.Z * VoxelWorld.ChunkSize;

		for (int localZ = 0; localZ < VoxelWorld.ChunkSize; localZ++)
		{
			for (int localY = 0; localY < VoxelWorld.ChunkSize; localY++)
			{
				for (int localX = 0; localX < VoxelWorld.ChunkSize; localX++)
				{
					int worldX = originX + localX;
					int worldY = originY + localY;
					int worldZ = originZ + localZ;
					ushort material = GetTerrainMaterial(worldX, worldY, worldZ, materials);

					if (material != 0)
					{
						cells[Index(localX, localY, localZ)] = new VoxelCell(material);
					}
				}
			}
		}

		OverlayTrees(cells, coordinate, materials);
		OverlayValidationFeatures(cells, coordinate, materials);

		return cells;
	}

	internal IEnumerable<(int X, int Y, int Z)> GetTreeRoots(
		int minimumX,
		int minimumZ,
		int maximumX,
		int maximumZ
	)
	{
		int minimumCellX = FloorDivide(minimumX, TreeSpacing);
		int maximumCellX = FloorDivide(maximumX, TreeSpacing);
		int minimumCellZ = FloorDivide(minimumZ, TreeSpacing);
		int maximumCellZ = FloorDivide(maximumZ, TreeSpacing);

		for (int cellZ = minimumCellZ; cellZ <= maximumCellZ; cellZ++)
		{
			for (int cellX = minimumCellX; cellX <= maximumCellX; cellX++)
			{
				uint hash = Hash(cellX, cellZ);
				int x = cellX * TreeSpacing + 4 + (int)(hash % 24);
				int z = cellZ * TreeSpacing + 4 + (int)((hash >> 8) % 24);

				if (!IsInsideWorld(x, z) || !IsDry(x, z, clearance: 2))
				{
					continue;
				}

				yield return (x, GetSurfaceHeight(x, z) + 1, z);
			}
		}
	}

	internal ushort GetTerrainMaterial(
		int worldX,
		int worldY,
		int worldZ,
		VoxelTestMaterialIds materials
	)
	{
		if (!IsInsideWorld(worldX, worldZ) || worldY < VoxelTestWorldGenerator.TerrainBottom)
		{
			return 0;
		}

		int surface = GetSurfaceHeight(worldX, worldZ);
		int? waterSurface = GetWaterSurface(worldX, worldZ);

		if (worldY > surface)
		{
			return waterSurface.HasValue && worldY <= waterSurface.Value
				? materials.Water
				: (ushort)0;
		}

		if (worldY == surface)
		{
			return waterSurface.HasValue ? materials.Dirt : materials.Grass;
		}

		return worldY >= surface - DirtDepth
			? materials.Dirt
			: materials.Stone;
	}

}
