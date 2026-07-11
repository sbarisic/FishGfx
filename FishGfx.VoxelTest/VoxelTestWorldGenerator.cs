using System;
using System.Collections.Generic;
using System.Numerics;
using FishGfx.Voxels;

namespace FishGfx.VoxelTest
{
	internal struct VoxelTestMaterialIds
	{
		internal ushort Stone;
		internal ushort Dirt;
		internal ushort Grass;
		internal ushort Leaves;
		internal ushort Glass;
		internal ushort Water;
	}

	internal sealed class VoxelTestWorldData
	{
		private const int TreeSpacing = 32;
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
		}

		internal int LakeCount { get; }
		internal int WaterColumnCount { get; }
		internal Vector3 UnderwaterCameraPosition { get; }

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
				for (int x = minimumX; x < minimumX + VoxelWorld.ChunkSize; x++)
					maximumY = Math.Max(maximumY, GetSurfaceHeight(x, z));

			maximumY += 8;

			if (IntersectsGlassWall(minimumX, minimumZ))
				for (
					int x = Math.Max(minimumX, VoxelTestWorldGenerator.GlassMinimumX);
					x <= Math.Min(minimumX + VoxelWorld.ChunkSize - 1, VoxelTestWorldGenerator.GlassMaximumX);
					x++
				)
					maximumY = Math.Max(
						maximumY,
						GetSurfaceHeight(x, VoxelTestWorldGenerator.GlassZ) + VoxelTestWorldGenerator.GlassHeight
					);
			if (ContainsBoundaryEdit(minimumX, minimumZ))
				maximumY = Math.Max(maximumY, VoxelTestWorldGenerator.BoundaryEditY);

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
				for (int localY = 0; localY < VoxelWorld.ChunkSize; localY++)
					for (int localX = 0; localX < VoxelWorld.ChunkSize; localX++)
					{
						int worldX = originX + localX;
						int worldY = originY + localY;
						int worldZ = originZ + localZ;
						ushort material = GetTerrainMaterial(worldX, worldY, worldZ, materials);

						if (material != 0)
							cells[Index(localX, localY, localZ)] = new VoxelCell(material);
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
				for (int cellX = minimumCellX; cellX <= maximumCellX; cellX++)
				{
					uint hash = Hash(cellX, cellZ);
					int x = cellX * TreeSpacing + 4 + (int)(hash % 24);
					int z = cellZ * TreeSpacing + 4 + (int)((hash >> 8) % 24);

					if (!IsInsideWorld(x, z) || !IsDry(x, z, clearance: 2))
						continue;

					yield return (x, GetSurfaceHeight(x, z) + 1, z);
				}
		}

		private ushort GetTerrainMaterial(
			int worldX,
			int worldY,
			int worldZ,
			VoxelTestMaterialIds materials
		)
		{
			if (!IsInsideWorld(worldX, worldZ) || worldY < VoxelTestWorldGenerator.TerrainBottom)
				return 0;

			int surface = GetSurfaceHeight(worldX, worldZ);
			int? waterSurface = GetWaterSurface(worldX, worldZ);

			if (worldY <= surface)
			{
				if (worldY == surface)
					return waterSurface.HasValue ? materials.Dirt : materials.Grass;
				if (worldY >= surface - 20)
					return materials.Dirt;

				return materials.Stone;
			}

			return waterSurface.HasValue && worldY <= waterSurface.Value ? materials.Water : (ushort)0;
		}

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
					SetIfInside(cells, coordinate, rootX, y, rootZ, materials.Dirt);

				for (int offsetY = 3; offsetY <= 6; offsetY++)
					for (int offsetZ = -2; offsetZ <= 2; offsetZ++)
						for (int offsetX = -2; offsetX <= 2; offsetX++)
							if (Math.Abs(offsetX) + Math.Abs(offsetZ) < 4)
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

		private void OverlayValidationFeatures(
			VoxelCell[] cells,
			ChunkCoordinate coordinate,
			VoxelTestMaterialIds materials
		)
		{
			for (int x = VoxelTestWorldGenerator.GlassMinimumX; x <= VoxelTestWorldGenerator.GlassMaximumX; x++)
			{
				int minimumY = GetSurfaceHeight(x, VoxelTestWorldGenerator.GlassZ) + 1;

				for (int y = minimumY; y < minimumY + VoxelTestWorldGenerator.GlassHeight; y++)
					SetIfInside(
						cells,
						coordinate,
						x,
						y,
						VoxelTestWorldGenerator.GlassZ,
						materials.Glass
					);
			}

			SetIfInside(
				cells,
				coordinate,
				VoxelTestWorldGenerator.BoundaryEditX,
				VoxelTestWorldGenerator.BoundaryEditY,
				VoxelTestWorldGenerator.BoundaryEditZ,
				materials.Glass
			);
		}

		private bool IsDry(int worldX, int worldZ, int clearance)
		{
			if (
				worldX < VoxelTestWorldGenerator.WorldMinimum + clearance
				|| worldX >= VoxelTestWorldGenerator.WorldMaximum - clearance
				|| worldZ < VoxelTestWorldGenerator.WorldMinimum + clearance
				|| worldZ >= VoxelTestWorldGenerator.WorldMaximum - clearance
			)
				return false;

			for (int z = worldZ - clearance; z <= worldZ + clearance; z++)
				for (int x = worldX - clearance; x <= worldX + clearance; x++)
					if (GetWaterSurface(x, z).HasValue)
						return false;

			return true;
		}

		private Vector3 FindUnderwaterCameraPosition()
		{
			int bestDepth = 0;
			Vector3 result = default;

			for (int z = 0; z < VoxelTestWorldGenerator.WorldSize; z++)
				for (int x = 0; x < VoxelTestWorldGenerator.WorldSize; x++)
				{
					int waterSurface = waterSurfaces[x, z];

					if (waterSurface == int.MinValue)
						continue;

					int depth = waterSurface - surfaceHeights[x, z];

					if (depth <= bestDepth)
						continue;

					bestDepth = depth;
					result = new Vector3(
						x + VoxelTestWorldGenerator.WorldMinimum + 0.5f,
						surfaceHeights[x, z] + 1.5f,
						z + VoxelTestWorldGenerator.WorldMinimum + 0.5f
					);
				}

			if (bestDepth == 0)
				throw new InvalidOperationException("The validation world does not contain an underwater camera position.");

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
				return;

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
			if (coordinate < VoxelTestWorldGenerator.WorldMinimum || coordinate >= VoxelTestWorldGenerator.WorldMaximum)
				throw new ArgumentOutOfRangeException(nameof(coordinate));

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

	internal static class VoxelTestWorldGenerator
	{
		internal const int WorldMinimum = -640;
		internal const int WorldMaximum = 640;
		internal const int WorldSize = WorldMaximum - WorldMinimum;
		internal const int TerrainBottom = -80;
		internal const int WorldMinimumY = -80;
		internal const int WorldMaximumY = 192;
		internal const int MinimumChunkCoordinate = WorldMinimum / VoxelWorld.ChunkSize;
		internal const int MaximumChunkCoordinate = WorldMaximum / VoxelWorld.ChunkSize - 1;
		internal const int MinimumSurfaceHeight = -10;
		internal const int MaximumSurfaceHeight = 160;
		internal const int MinimumLakeArea = 24;
		internal const int BoundaryEditX = 15;
		internal const int BoundaryEditY = 180;
		internal const int BoundaryEditZ = 0;
		internal const int GlassMinimumX = 36;
		internal const int GlassMaximumX = 39;
		internal const int GlassZ = -12;
		internal const int GlassHeight = 20;

		internal static VoxelPalette CreatePalette(out VoxelTestMaterialIds ids)
		{
			VoxelPaletteBuilder builder = new VoxelPaletteBuilder();
			ids = new VoxelTestMaterialIds
			{
				Stone = builder.Add(new VoxelMaterial("Stone", VoxelRenderMode.Opaque, new VoxelFaceTiles(0))),
				Dirt = builder.Add(new VoxelMaterial("Dirt", VoxelRenderMode.Opaque, new VoxelFaceTiles(1))),
				Grass = builder.Add(
					new VoxelMaterial(
						"Grass",
						VoxelRenderMode.Opaque,
						new VoxelFaceTiles(1, 1, 2, 1, 1, 1)
					)
				),
				Leaves = builder.Add(
					new VoxelMaterial(
						"Leaves",
						VoxelRenderMode.Cutout,
						new VoxelFaceTiles(3),
						occludesFaces: false,
						doubleSided: true
					)
				),
				Glass = builder.Add(
					new VoxelMaterial(
						"Glass",
						VoxelRenderMode.Transparent,
						new VoxelFaceTiles(4),
						occludesFaces: false,
						doubleSided: true
					)
				),
				Water = builder.Add(
					new VoxelMaterial(
						"Water",
						VoxelRenderMode.Transparent,
						new VoxelFaceTiles(5),
						occludesFaces: false
					)
				),
			};

			return builder.Build();
		}

		internal static VoxelTestWorldData Generate(VoxelTestMaterialIds materials)
		{
			_ = materials;
			int[,] heights = CreateHeightField();
			VoxelLakeMap lakes = VoxelLakeAnalyzer.FindEnclosedBasins(heights, MinimumLakeArea);

			if (lakes.BasinCount < 2)
				throw new InvalidOperationException("The deterministic validation terrain must produce at least two lakes.");

			int[,] waterSurfaces = new int[WorldSize, WorldSize];

			for (int z = 0; z < WorldSize; z++)
				for (int x = 0; x < WorldSize; x++)
					waterSurfaces[x, z] = lakes.GetWaterSurface(x, z) ?? int.MinValue;

			VoxelTestWorldData result = new VoxelTestWorldData(
				heights,
				waterSurfaces,
				lakes.BasinCount,
				lakes.WaterColumnCount
			);
			ValidateWaterContainment(result);

			return result;
		}

		internal static int[,] CreateHeightField()
		{
			int[,] heights = new int[WorldSize, WorldSize];

			for (int z = 0; z < WorldSize; z++)
				for (int x = 0; x < WorldSize; x++)
				{
					int worldX = x + WorldMinimum;
					int worldZ = z + WorldMinimum;
					float broad = MathF.Sin(worldX * 0.012f) * 48
						+ MathF.Cos(worldZ * 0.0105f) * 39
						+ MathF.Sin((worldX + worldZ) * 0.0065f) * 28;
					float detail = MathF.Sin(worldX * 0.038f + worldZ * 0.024f) * 12
						+ MathF.Cos(worldX * 0.021f - worldZ * 0.033f) * 9;
					float depressions = Depression(worldX, worldZ, -120, -90, 88, 62)
						+ Depression(worldX, worldZ, 145, 110, 76, 55);
					heights[x, z] = Math.Clamp(
						55 + (int)MathF.Round(broad + detail - depressions),
						MinimumSurfaceHeight,
						MaximumSurfaceHeight
					);
				}

			return heights;
		}

		internal static void ValidateWaterContainment(VoxelTestWorldData data)
		{
			if (data == null)
				throw new ArgumentNullException(nameof(data));

			for (int z = WorldMinimum; z < WorldMaximum; z++)
				for (int x = WorldMinimum; x < WorldMaximum; x++)
				{
					int? waterSurface = data.GetWaterSurface(x, z);

					if (!waterSurface.HasValue)
						continue;

					if (x == WorldMinimum || x == WorldMaximum - 1 || z == WorldMinimum || z == WorldMaximum - 1)
						throw new InvalidOperationException("Lake water cannot occupy a world-boundary column.");

					int terrainSurface = data.GetSurfaceHeight(x, z);

					for (int y = terrainSurface + 1; y <= waterSurface.Value; y++)
					{
						ValidateHorizontalNeighbor(x - 1, y, z);
						ValidateHorizontalNeighbor(x + 1, y, z);
						ValidateHorizontalNeighbor(x, y, z - 1);
						ValidateHorizontalNeighbor(x, y, z + 1);
					}
				}

			void ValidateHorizontalNeighbor(int x, int y, int z)
			{
				int terrainSurface = data.GetSurfaceHeight(x, z);
				int? waterSurface = data.GetWaterSurface(x, z);

				if (terrainSurface < y && (!waterSurface.HasValue || waterSurface.Value < y))
					throw new InvalidOperationException("Lake water has a horizontally exposed side.");
			}
		}

		private static float Depression(
			int x,
			int z,
			float centerX,
			float centerZ,
			float radius,
			float depth
		)
		{
			float offsetX = x - centerX;
			float offsetZ = z - centerZ;
			float normalizedDistanceSquared = (offsetX * offsetX + offsetZ * offsetZ) / (radius * radius);

			if (normalizedDistanceSquared >= 1)
				return 0;

			float influence = 1 - normalizedDistanceSquared;
			return depth * influence * influence;
		}
	}
}
