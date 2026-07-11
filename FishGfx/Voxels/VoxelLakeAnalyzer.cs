using System;
using System.Collections.Generic;

namespace FishGfx.Voxels
{
	internal sealed class VoxelLakeMap
	{
		private readonly int[] waterSurfaces;

		internal VoxelLakeMap(int width, int height, int[] waterSurfaces, int basinCount)
		{
			Width = width;
			Height = height;
			this.waterSurfaces = waterSurfaces;
			BasinCount = basinCount;

			int waterColumnCount = 0;

			for (int i = 0; i < waterSurfaces.Length; i++)
				if (waterSurfaces[i] != int.MinValue)
					waterColumnCount++;

			WaterColumnCount = waterColumnCount;
		}

		internal int Width { get; }
		internal int Height { get; }
		internal int BasinCount { get; }
		internal int WaterColumnCount { get; }

		internal int? GetWaterSurface(int x, int z)
		{
			ValidateCoordinates(x, z);
			int surface = waterSurfaces[Index(x, z)];

			return surface == int.MinValue ? null : surface;
		}

		private int Index(int x, int z) => x + Width * z;

		private void ValidateCoordinates(int x, int z)
		{
			if ((uint)x >= Width)
				throw new ArgumentOutOfRangeException(nameof(x));
			if ((uint)z >= Height)
				throw new ArgumentOutOfRangeException(nameof(z));
		}
	}

	internal static class VoxelLakeAnalyzer
	{
		private static readonly (int X, int Z)[] Neighbors =
		{
			(-1, 0),
			(1, 0),
			(0, -1),
			(0, 1),
		};

		internal static VoxelLakeMap FindEnclosedBasins(int[,] terrainHeights, int minimumArea = 24)
		{
			if (terrainHeights == null)
				throw new ArgumentNullException(nameof(terrainHeights));
			if (minimumArea <= 0)
				throw new ArgumentOutOfRangeException(nameof(minimumArea));

			int width = terrainHeights.GetLength(0);
			int height = terrainHeights.GetLength(1);

			if (width < 3 || height < 3)
				throw new ArgumentException("A terrain height field must be at least 3x3.", nameof(terrainHeights));

			int[,] filledHeights = (int[,])terrainHeights.Clone();
			bool[,] visited = new bool[width, height];
			PriorityQueue<Cell, (int Elevation, int Sequence)> frontier =
				new PriorityQueue<Cell, (int, int)>();
			int sequence = 0;

			for (int x = 0; x < width; x++)
			{
				EnqueueBoundary(x, 0);
				EnqueueBoundary(x, height - 1);
			}

			for (int z = 1; z < height - 1; z++)
			{
				EnqueueBoundary(0, z);
				EnqueueBoundary(width - 1, z);
			}

			while (frontier.TryDequeue(out Cell current, out _))
			{
				int currentHeight = filledHeights[current.X, current.Z];

				foreach ((int offsetX, int offsetZ) in Neighbors)
				{
					int x = current.X + offsetX;
					int z = current.Z + offsetZ;

					if ((uint)x >= width || (uint)z >= height || visited[x, z])
						continue;

					visited[x, z] = true;
					filledHeights[x, z] = Math.Max(terrainHeights[x, z], currentHeight);
					frontier.Enqueue(
						new Cell(x, z),
						(filledHeights[x, z], sequence++)
					);
				}
			}

			bool[,] candidate = new bool[width, height];

			for (int z = 1; z < height - 1; z++)
				for (int x = 1; x < width - 1; x++)
					candidate[x, z] = filledHeights[x, z] > terrainHeights[x, z];

			bool[,] grouped = new bool[width, height];
			int[] waterSurfaces = new int[width * height];
			Array.Fill(waterSurfaces, int.MinValue);
			int basinCount = 0;

			for (int z = 1; z < height - 1; z++)
				for (int x = 1; x < width - 1; x++)
				{
					if (!candidate[x, z] || grouped[x, z])
						continue;

					int waterSurface = filledHeights[x, z];
					List<Cell> basin = CollectBasin(x, z, waterSurface);

					if (basin.Count < minimumArea)
						continue;

					basinCount++;

					foreach (Cell cell in basin)
						waterSurfaces[cell.X + width * cell.Z] = waterSurface;
				}

			return new VoxelLakeMap(width, height, waterSurfaces, basinCount);

			void EnqueueBoundary(int x, int z)
			{
				if (visited[x, z])
					return;

				visited[x, z] = true;
				frontier.Enqueue(new Cell(x, z), (terrainHeights[x, z], sequence++));
			}

			List<Cell> CollectBasin(int startX, int startZ, int waterSurface)
			{
				Queue<Cell> pending = new Queue<Cell>();
				List<Cell> basin = new List<Cell>();
				grouped[startX, startZ] = true;
				pending.Enqueue(new Cell(startX, startZ));

				while (pending.Count > 0)
				{
					Cell current = pending.Dequeue();
					basin.Add(current);

					foreach ((int offsetX, int offsetZ) in Neighbors)
					{
						int x = current.X + offsetX;
						int z = current.Z + offsetZ;

						if (
							(uint)x >= width
							|| (uint)z >= height
							|| grouped[x, z]
							|| !candidate[x, z]
							|| filledHeights[x, z] != waterSurface
						)
							continue;

						grouped[x, z] = true;
						pending.Enqueue(new Cell(x, z));
					}
				}

				return basin;
			}
		}

		private readonly struct Cell
		{
			internal Cell(int x, int z)
			{
				X = x;
				Z = z;
			}

			internal int X { get; }
			internal int Z { get; }
		}
	}
}
