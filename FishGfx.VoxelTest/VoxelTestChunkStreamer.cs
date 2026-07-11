using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using FishGfx.Voxels;

namespace FishGfx.VoxelTest
{
	internal sealed class VoxelTestChunkStreamer
	{
		private readonly VoxelTestWorldData data;
		private readonly VoxelTestMaterialIds materials;
		private readonly int loadRadius;
		private readonly int unloadRadius;
		private readonly int generationBudget;
		private readonly HashSet<(int X, int Z)> loadedHorizontal = new HashSet<(int, int)>();
		private readonly Dictionary<ChunkCoordinate, Dictionary<int, VoxelCell>> overrides =
			new Dictionary<ChunkCoordinate, Dictionary<int, VoxelCell>>();
		private readonly List<(int X, int Z)> generatedThisFrame = new List<(int, int)>();

		internal VoxelTestChunkStreamer(
			VoxelTestWorldData data,
			VoxelTestMaterialIds materials,
			int loadRadius = 7,
			int unloadRadius = 9,
			int generationBudget = 4
		)
		{
			this.data = data ?? throw new ArgumentNullException(nameof(data));
			this.materials = materials;

			if (loadRadius < 0)
				throw new ArgumentOutOfRangeException(nameof(loadRadius));
			if (unloadRadius < loadRadius)
				throw new ArgumentOutOfRangeException(nameof(unloadRadius));
			if (generationBudget <= 0)
				throw new ArgumentOutOfRangeException(nameof(generationBudget));

			this.loadRadius = loadRadius;
			this.unloadRadius = unloadRadius;
			this.generationBudget = generationBudget;
			World = new VoxelWorld();
		}

		internal VoxelWorld World { get; }
		internal int LoadedHorizontalCount => loadedHorizontal.Count;
		internal int PendingHorizontalCount { get; private set; }
		internal int OverrideCount => overrides.Values.Sum(chunk => chunk.Count);
		internal int MaximumResidentHorizontalCount => (unloadRadius * 2 + 1) * (unloadRadius * 2 + 1);
		internal int MaximumResidentChunkCount => MaximumResidentHorizontalCount
			* (FloorChunk(VoxelTestWorldGenerator.WorldMaximumY - 1)
				- FloorChunk(VoxelTestWorldGenerator.WorldMinimumY)
				+ 1);
		internal bool IsSettled => PendingHorizontalCount == 0;
		internal IReadOnlyList<(int X, int Z)> GeneratedThisFrame => generatedThisFrame;

		internal int Update(Vector3 cameraPosition)
		{
			cameraPosition = ClampPosition(cameraPosition);
			ChunkCoordinate center = ChunkCoordinate.FromWorld(
				(int)MathF.Floor(cameraPosition.X),
				0,
				(int)MathF.Floor(cameraPosition.Z),
				out _,
				out _,
				out _
			);

			UnloadDistant(center.X, center.Z);
			List<(int X, int Z, int DistanceSquared)> pending = BuildPending(center.X, center.Z);
			generatedThisFrame.Clear();
			int generated = Math.Min(generationBudget, pending.Count);

			for (int i = 0; i < generated; i++)
			{
				(int x, int z, _) = pending[i];
				LoadHorizontal(x, z);
				generatedThisFrame.Add((x, z));
			}

			PendingHorizontalCount = pending.Count - generated;
			return generated;
		}

		internal bool SetVoxel(int x, int y, int z, VoxelCell value)
		{
			if (!IsInsideWorld(x, y, z))
				return false;

			ChunkCoordinate coordinate = ChunkCoordinate.FromWorld(
				x,
				y,
				z,
				out int localX,
				out int localY,
				out int localZ
			);
			int index = Index(localX, localY, localZ);
			VoxelCell generated = data.GenerateChunk(coordinate, materials)[index];

			if (value == generated)
			{
				if (overrides.TryGetValue(coordinate, out Dictionary<int, VoxelCell> chunkOverrides))
				{
					chunkOverrides.Remove(index);

					if (chunkOverrides.Count == 0)
						overrides.Remove(coordinate);
				}
			}
			else
			{
				if (!overrides.TryGetValue(coordinate, out Dictionary<int, VoxelCell> chunkOverrides))
				{
					chunkOverrides = new Dictionary<int, VoxelCell>();
					overrides.Add(coordinate, chunkOverrides);
				}

				chunkOverrides[index] = value;
			}

			return loadedHorizontal.Contains((coordinate.X, coordinate.Z))
				? World.SetVoxel(x, y, z, value)
				: true;
		}

		internal Vector3 ClampPosition(Vector3 position)
		{
			return new Vector3(
				Math.Clamp(position.X, VoxelTestWorldGenerator.WorldMinimum + 0.001f, VoxelTestWorldGenerator.WorldMaximum - 0.001f),
				Math.Clamp(position.Y, VoxelTestWorldGenerator.WorldMinimumY + 0.001f, VoxelTestWorldGenerator.WorldMaximumY - 0.001f),
				Math.Clamp(position.Z, VoxelTestWorldGenerator.WorldMinimum + 0.001f, VoxelTestWorldGenerator.WorldMaximum - 0.001f)
			);
		}

		private List<(int X, int Z, int DistanceSquared)> BuildPending(int centerX, int centerZ)
		{
			List<(int X, int Z, int DistanceSquared)> result = new List<(int, int, int)>();

			for (int z = centerZ - loadRadius; z <= centerZ + loadRadius; z++)
				for (int x = centerX - loadRadius; x <= centerX + loadRadius; x++)
				{
					if (!IsInsideHorizontalChunkBounds(x, z) || loadedHorizontal.Contains((x, z)))
						continue;

					int offsetX = x - centerX;
					int offsetZ = z - centerZ;
					result.Add((x, z, offsetX * offsetX + offsetZ * offsetZ));
				}

			return result
				.OrderBy(item => item.DistanceSquared)
				.ThenBy(item => item.Z)
				.ThenBy(item => item.X)
				.ToList();
		}

		private void LoadHorizontal(int chunkX, int chunkZ)
		{
			(int minimumY, int maximumY) = data.GetVerticalChunkRange(chunkX, chunkZ);

			foreach (ChunkCoordinate coordinate in overrides.Keys)
				if (coordinate.X == chunkX && coordinate.Z == chunkZ)
				{
					minimumY = Math.Min(minimumY, coordinate.Y);
					maximumY = Math.Max(maximumY, coordinate.Y);
				}

			for (int chunkY = minimumY; chunkY <= maximumY; chunkY++)
			{
				ChunkCoordinate coordinate = new ChunkCoordinate(chunkX, chunkY, chunkZ);
				VoxelCell[] cells = data.GenerateChunk(coordinate, materials);

				if (overrides.TryGetValue(coordinate, out Dictionary<int, VoxelCell> chunkOverrides))
					foreach (KeyValuePair<int, VoxelCell> item in chunkOverrides)
						cells[item.Key] = item.Value;

				World.SetChunk(coordinate, cells);
			}

			loadedHorizontal.Add((chunkX, chunkZ));
		}

		private void UnloadDistant(int centerX, int centerZ)
		{
			(int X, int Z)[] distant = loadedHorizontal
				.Where(item => Math.Max(Math.Abs(item.X - centerX), Math.Abs(item.Z - centerZ)) > unloadRadius)
				.ToArray();

			if (distant.Length == 0)
				return;

			HashSet<(int X, int Z)> distantSet = distant.ToHashSet();
			ChunkCoordinate[] chunks = World.LoadedChunks
				.Where(chunk => distantSet.Contains((chunk.Coordinate.X, chunk.Coordinate.Z)))
				.Select(chunk => chunk.Coordinate)
				.ToArray();

			foreach (ChunkCoordinate coordinate in chunks)
				World.RemoveChunk(coordinate);

			foreach ((int x, int z) in distant)
				loadedHorizontal.Remove((x, z));
		}

		private static bool IsInsideHorizontalChunkBounds(int x, int z)
		{
			return x >= VoxelTestWorldGenerator.MinimumChunkCoordinate
				&& x <= VoxelTestWorldGenerator.MaximumChunkCoordinate
				&& z >= VoxelTestWorldGenerator.MinimumChunkCoordinate
				&& z <= VoxelTestWorldGenerator.MaximumChunkCoordinate;
		}

		private static bool IsInsideWorld(int x, int y, int z)
		{
			return x >= VoxelTestWorldGenerator.WorldMinimum
				&& x < VoxelTestWorldGenerator.WorldMaximum
				&& y >= VoxelTestWorldGenerator.WorldMinimumY
				&& y < VoxelTestWorldGenerator.WorldMaximumY
				&& z >= VoxelTestWorldGenerator.WorldMinimum
				&& z < VoxelTestWorldGenerator.WorldMaximum;
		}

		private static int Index(int x, int y, int z)
		{
			return x + VoxelWorld.ChunkSize * (y + VoxelWorld.ChunkSize * z);
		}

		private static int FloorChunk(int coordinate)
		{
			return ChunkCoordinate.FromWorld(coordinate, 0, 0, out _, out _, out _).X;
		}
	}
}
