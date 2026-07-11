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
		private readonly int[,] surfaceHeights;
		private readonly int[,] waterSurfaces;
		private readonly List<(int X, int Y, int Z)> treeBases;

		internal VoxelTestWorldData(
			VoxelWorld world,
			int[,] surfaceHeights,
			int[,] waterSurfaces,
			int lakeCount,
			int waterColumnCount,
			List<(int X, int Y, int Z)> treeBases
		)
		{
			World = world;
			this.surfaceHeights = surfaceHeights;
			this.waterSurfaces = waterSurfaces;
			LakeCount = lakeCount;
			WaterColumnCount = waterColumnCount;
			this.treeBases = treeBases;
			UnderwaterCameraPosition = FindUnderwaterCameraPosition();
		}

		internal VoxelWorld World { get; }
		internal int LakeCount { get; }
		internal int WaterColumnCount { get; }
		internal IReadOnlyList<(int X, int Y, int Z)> TreeBases => treeBases;
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

		private static int ToIndex(int coordinate)
		{
			if (coordinate < VoxelTestWorldGenerator.WorldMinimum || coordinate >= VoxelTestWorldGenerator.WorldMaximum)
				throw new ArgumentOutOfRangeException(nameof(coordinate));

			return coordinate - VoxelTestWorldGenerator.WorldMinimum;
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
	}

	internal static class VoxelTestWorldGenerator
	{
		internal const int WorldMinimum = -64;
		internal const int WorldMaximum = 64;
		internal const int WorldSize = WorldMaximum - WorldMinimum;
		internal const int TerrainBottom = -8;
		internal const int MinimumLakeArea = 24;
		internal const int BoundaryEditX = 15;
		internal const int BoundaryEditY = 18;
		internal const int BoundaryEditZ = 0;

		private static readonly (int X, int Z)[] TreeCandidates =
		{
			(-48, -38),
			(-42, 35),
			(-5, 38),
			(4, -45),
			(43, -28),
			(48, 42),
		};

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
			int[,] heights = CreateHeightField();
			VoxelLakeMap lakes = VoxelLakeAnalyzer.FindEnclosedBasins(heights, MinimumLakeArea);

			if (lakes.BasinCount < 2)
				throw new InvalidOperationException("The deterministic validation terrain must produce at least two lakes.");

			VoxelWorld world = new VoxelWorld();
			int[,] waterSurfaces = new int[WorldSize, WorldSize];

			for (int z = 0; z < WorldSize; z++)
				for (int x = 0; x < WorldSize; x++)
				{
					int worldX = x + WorldMinimum;
					int worldZ = z + WorldMinimum;
					int surface = heights[x, z];
					int? waterSurface = lakes.GetWaterSurface(x, z);
					waterSurfaces[x, z] = waterSurface ?? int.MinValue;

					for (int y = TerrainBottom; y <= surface; y++)
					{
						ushort material;

						if (y == surface)
							material = waterSurface.HasValue ? materials.Dirt : materials.Grass;
						else if (y >= surface - 2)
							material = materials.Dirt;
						else
							material = materials.Stone;

						world.SetVoxel(worldX, y, worldZ, new VoxelCell(material));
					}

					if (waterSurface.HasValue)
						for (int y = surface + 1; y <= waterSurface.Value; y++)
							world.SetVoxel(worldX, y, worldZ, new VoxelCell(materials.Water));
				}

			List<(int X, int Y, int Z)> treeBases = new List<(int, int, int)>();

			foreach ((int preferredX, int preferredZ) in TreeCandidates)
			{
				(int x, int z) = FindDryColumn(lakes, preferredX, preferredZ, clearance: 2);
				int y = heights[x - WorldMinimum, z - WorldMinimum] + 1;
				CreateTree(world, materials, x, y, z);
				treeBases.Add((x, y, z));
			}

			(int glassX, int glassZ) = FindDryRectangle(lakes, preferredX: 36, preferredZ: -12, width: 4);

			for (int x = glassX; x < glassX + 4; x++)
			{
				int baseY = heights[x - WorldMinimum, glassZ - WorldMinimum] + 1;

				for (int y = baseY; y < baseY + 10; y++)
					world.SetVoxel(x, y, glassZ, new VoxelCell(materials.Glass));
			}

			world.SetVoxel(
				BoundaryEditX,
				BoundaryEditY,
				BoundaryEditZ,
				new VoxelCell(materials.Glass)
			);

			VoxelTestWorldData result = new VoxelTestWorldData(
				world,
				heights,
				waterSurfaces,
				lakes.BasinCount,
				lakes.WaterColumnCount,
				treeBases
			);
			ValidateWaterContainment(result, materials.Water);

			return result;
		}

		internal static void ValidateWaterContainment(VoxelTestWorldData data, ushort waterMaterial)
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

					if (data.World.GetVoxel(x, terrainSurface, z).IsAir)
						throw new InvalidOperationException("Every lake column must have a solid floor.");

					for (int y = terrainSurface + 1; y <= waterSurface.Value; y++)
					{
						if (data.World.GetVoxel(x, y, z).MaterialId != waterMaterial)
							throw new InvalidOperationException("A lake column contains a gap or a non-water voxel.");

						ValidateHorizontalNeighbor(x - 1, y, z);
						ValidateHorizontalNeighbor(x + 1, y, z);
						ValidateHorizontalNeighbor(x, y, z - 1);
						ValidateHorizontalNeighbor(x, y, z + 1);
					}
				}

			void ValidateHorizontalNeighbor(int x, int y, int z)
			{
				VoxelCell neighbor = data.World.GetVoxel(x, y, z);

				if (neighbor.IsAir)
					throw new InvalidOperationException("Lake water has a horizontally exposed side.");
			}
		}

		internal static int[,] CreateHeightField()
		{
			int[,] heights = new int[WorldSize, WorldSize];

			for (int z = 0; z < WorldSize; z++)
				for (int x = 0; x < WorldSize; x++)
				{
					int worldX = x + WorldMinimum;
					int worldZ = z + WorldMinimum;
					float broad = MathF.Sin(worldX * 0.075f) * 3.2f
						+ MathF.Cos(worldZ * 0.068f) * 2.7f
						+ MathF.Sin((worldX + worldZ) * 0.043f) * 2.1f;
					float detail = MathF.Sin(worldX * 0.21f + worldZ * 0.13f) * 1.1f
						+ MathF.Cos(worldX * 0.11f - worldZ * 0.19f) * 0.8f;
					float depressions = Depression(worldX, worldZ, -22, -14, 18, 7.5f)
						+ Depression(worldX, worldZ, 25, 18, 16, 6.5f);
					heights[x, z] = Math.Clamp(7 + (int)MathF.Round(broad + detail - depressions), -1, 16);
				}

			return heights;
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

		private static (int X, int Z) FindDryColumn(
			VoxelLakeMap lakes,
			int preferredX,
			int preferredZ,
			int clearance
		)
		{
			for (int radius = 0; radius <= 16; radius++)
				for (int z = preferredZ - radius; z <= preferredZ + radius; z++)
					for (int x = preferredX - radius; x <= preferredX + radius; x++)
					{
						if (Math.Max(Math.Abs(x - preferredX), Math.Abs(z - preferredZ)) != radius)
							continue;
						if (IsDry(lakes, x, z, clearance))
							return (x, z);
					}

			throw new InvalidOperationException("Could not find dry terrain for a validation-world feature.");
		}

		private static (int X, int Z) FindDryRectangle(
			VoxelLakeMap lakes,
			int preferredX,
			int preferredZ,
			int width
		)
		{
			for (int radius = 0; radius <= 24; radius++)
				for (int z = preferredZ - radius; z <= preferredZ + radius; z++)
					for (int x = preferredX - radius; x <= preferredX + radius; x++)
					{
						if (Math.Max(Math.Abs(x - preferredX), Math.Abs(z - preferredZ)) != radius)
							continue;

						bool dry = true;

						for (int offset = 0; offset < width && dry; offset++)
							dry = IsDry(lakes, x + offset, z, clearance: 1);

						if (dry)
							return (x, z);
					}

			throw new InvalidOperationException("Could not find dry terrain for the glass validation wall.");
		}

		private static bool IsDry(VoxelLakeMap lakes, int worldX, int worldZ, int clearance)
		{
			int centerX = worldX - WorldMinimum;
			int centerZ = worldZ - WorldMinimum;

			if (
				centerX < clearance
				|| centerX >= WorldSize - clearance
				|| centerZ < clearance
				|| centerZ >= WorldSize - clearance
			)
				return false;

			for (int z = centerZ - clearance; z <= centerZ + clearance; z++)
				for (int x = centerX - clearance; x <= centerX + clearance; x++)
					if (lakes.GetWaterSurface(x, z).HasValue)
						return false;

			return true;
		}

		private static void CreateTree(
			VoxelWorld world,
			VoxelTestMaterialIds materials,
			int x,
			int y,
			int z
		)
		{
			for (int trunkY = y; trunkY < y + 5; trunkY++)
				world.SetVoxel(x, trunkY, z, new VoxelCell(materials.Dirt));

			for (int offsetY = 3; offsetY <= 6; offsetY++)
				for (int offsetZ = -2; offsetZ <= 2; offsetZ++)
					for (int offsetX = -2; offsetX <= 2; offsetX++)
						if (Math.Abs(offsetX) + Math.Abs(offsetZ) < 4)
							world.SetVoxel(x + offsetX, y + offsetY, z + offsetZ, new VoxelCell(materials.Leaves));
		}
	}
}
