using System;
using System.Collections.Generic;
using System.Numerics;
using FishGfx.Voxels;

namespace FishGfx.VoxelTest
{
	internal sealed class VoxelTestWorldData
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
				throw new ArgumentOutOfRangeException(nameof(index));

			return (
				ShowcaseOriginX + index % 11 * 2,
				ShowcaseY,
				ShowcaseOriginZ + index / 11 * 3
			);
		}

		internal (int X, int Y, int Z) GetOrientationShowcasePosition(int index)
		{
			if (index < 0 || index >= VoxelTestWorldGenerator.OrientationShowcaseCount)
				throw new ArgumentOutOfRangeException(nameof(index));

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
			if (
				minimumX <= ShowcaseOriginX + 20
				&& minimumX + VoxelWorld.ChunkSize - 1 >= ShowcaseOriginX
				&& minimumZ <= ShowcaseOriginZ + 6
				&& minimumZ + VoxelWorld.ChunkSize - 1 >= ShowcaseOriginZ
			)
				maximumY = Math.Max(maximumY, ShowcaseY);

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

		internal ushort GetTerrainMaterial(
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

			if (worldY > surface)
				return waterSurface.HasValue && worldY <= waterSurface.Value
					? materials.Water
					: (ushort)0;

			if (worldY == surface)
				return waterSurface.HasValue ? materials.Dirt : materials.Grass;

			return worldY >= surface - DirtDepth
				? materials.Dirt
				: materials.Stone;
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
					SetIfInside(cells, coordinate, rootX, y, rootZ, materials.Wood);

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

			for (int index = 0; index < materials.Placeable.Count; index++)
			{
				(int x, int y, int z) = GetShowcasePosition(index);
				SetIfInside(cells, coordinate, x, y, z, materials.Placeable[index].Id);
			}

			for (int index = 0; index < VoxelTestWorldGenerator.OrientationShowcaseCount; index++)
			{
				(int x, int y, int z) = GetOrientationShowcasePosition(index);
				ushort material = VoxelTestWorldGenerator.GetOrientationShowcaseMaterial(materials, index);
				SetIfInside(cells, coordinate, x, y, z, material);
			}
		}

		private int CalculateShowcaseY()
		{
			int maximumY = int.MinValue;

			for (int z = ShowcaseOriginZ; z <= ShowcaseOriginZ + 6; z++)
				for (int x = ShowcaseOriginX; x <= ShowcaseOriginX + 20; x++)
				{
					maximumY = Math.Max(maximumY, GetSurfaceHeight(x, z));

					if (GetWaterSurface(x, z) is int waterSurface)
						maximumY = Math.Max(maximumY, waterSurface);
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
		internal const int OrientationShowcaseCount = 7;
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

		internal static ushort GetOrientationShowcaseMaterial(VoxelTestMaterialIds materials, int index)
		{
			if (materials == null)
				throw new ArgumentNullException(nameof(materials));

			return index switch
			{
				0 => materials.Grass,
				1 => materials.Wood,
				2 => materials.CraftingTable,
				3 => materials.Barrel,
				4 => materials.Campfire,
				5 => materials.Torch,
				6 => materials.Foliage,
				_ => throw new ArgumentOutOfRangeException(nameof(index)),
			};
		}

		internal static VoxelPalette CreatePalette(VoxelTestModelAssets models, out VoxelTestMaterialIds ids)
		{
			if (models == null)
				throw new ArgumentNullException(nameof(models));

			VoxelPaletteBuilder builder = new VoxelPaletteBuilder();
			ids = new VoxelTestMaterialIds();
			ids.Stone = Add(builder, ids, "Stone", Opaque("Stone", 0));
			ids.Dirt = Add(builder, ids, "Dirt", Opaque("Dirt", 1));
			ids.StoneBrick = Add(builder, ids, "Stone Brick", Opaque("Stone Brick", 2));
			ids.Sand = Add(builder, ids, "Sand", Opaque("Sand", 3));
			ids.Bricks = Add(builder, ids, "Bricks", Opaque("Bricks", 4));
			ids.Plank = Add(builder, ids, "Plank", Opaque("Plank", 5));
			ids.EndStoneBrick = Add(builder, ids, "End Stone Brick", Opaque("End Stone Brick", 6));
			ids.Ice = Add(
				builder,
				ids,
				"Ice",
				new VoxelMaterial(
					"Ice",
					VoxelRenderMode.Transparent,
					new VoxelFaceTiles(7),
					occludesFaces: false,
					doubleSided: true
				)
			);
			ids.Test = Add(builder, ids, "Test", Opaque("Test", 8));
			ids.Leaves = Add(
				builder,
				ids,
				"Leaf",
				new VoxelMaterial(
					"Leaf",
					VoxelRenderMode.Transparent,
					new VoxelFaceTiles(9),
					occludesFaces: false
				)
			);
			ids.Water = Add(
				builder,
				ids,
				"Water",
				new VoxelMaterial(
					"Water",
					VoxelRenderMode.Transparent,
					new VoxelFaceTiles(10),
					occludesFaces: false,
					doubleSided: true
				)
			);
			ids.Glass = Add(
				builder,
				ids,
				"Glass",
				new VoxelMaterial(
					"Glass",
					VoxelRenderMode.Transparent,
					new VoxelFaceTiles(11),
					occludesFaces: false,
					doubleSided: true
				)
			);
			ids.Glowstone = Add(builder, ids, "Glowstone", Opaque("Glowstone", 12));
			ids.Test2 = Add(builder, ids, "Test 2", Opaque("Test 2", 13));
			ids.Grass = Add(
				builder,
				ids,
				"Grass",
				new VoxelMaterial(
					"Grass",
					VoxelRenderMode.Opaque,
					new VoxelFaceTiles(241, 241, 240, 1, 241, 241)
				)
			);
			ids.Wood = Add(
				builder,
				ids,
				"Wood",
				new VoxelMaterial(
					"Wood",
					VoxelRenderMode.Opaque,
					new VoxelFaceTiles(242, 242, 243, 243, 242, 242)
				)
			);
			ids.CraftingTable = Add(
				builder,
				ids,
				"Crafting Table",
				new VoxelMaterial(
					"Crafting Table",
					VoxelRenderMode.Opaque,
					new VoxelFaceTiles(245, 245, 244, 247, 246, 246)
				)
			);
			ids.Barrel = Add(builder, ids, "Barrel", Custom("Barrel", VoxelRenderMode.Opaque, models.Barrel, true));
			ids.Campfire = Add(builder, ids, "Campfire", Custom("Campfire", VoxelRenderMode.Cutout, models.Campfire, false));
			ids.Torch = Add(builder, ids, "Torch", Custom("Torch", VoxelRenderMode.Cutout, models.Torch, false));
			ids.Foliage = Add(
				builder,
				ids,
				"Foliage",
				new VoxelMaterial(
					"Foliage",
					VoxelRenderMode.Cutout,
					new VoxelFaceTiles(0),
					occludesFaces: false,
					models: models.Foliage
				)
			);
			ids.Gravel = Add(builder, ids, "Gravel", Opaque("Gravel", 21));

			return builder.Build();
		}

		private static VoxelMaterial Opaque(string name, int raylibTile)
		{
			return new VoxelMaterial(
				name,
				VoxelRenderMode.Opaque,
				new VoxelFaceTiles(raylibTile)
			);
		}

		private static VoxelMaterial Custom(
			string name,
			VoxelRenderMode mode,
			VoxelModel model,
			bool occludesFaces
		)
		{
			return new VoxelMaterial(
				name,
				mode,
				new VoxelFaceTiles(0),
				occludesFaces: occludesFaces,
				models: new VoxelModelSet(model)
			);
		}

		private static ushort Add(
			VoxelPaletteBuilder builder,
			VoxelTestMaterialIds ids,
			string name,
			VoxelMaterial material
		)
		{
			ushort id = builder.Add(material);
			ids.Add(id, name);
			return id;
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
