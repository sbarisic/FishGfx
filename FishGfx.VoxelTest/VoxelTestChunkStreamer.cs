using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using FishGfx.Graphics;
using FishGfx.Voxels;

namespace FishGfx.VoxelTest;

internal sealed class VoxelTestChunkStreamer
{
	private readonly VoxelTestWorldData data;
	private readonly VoxelTestMaterialIds materials;
	private readonly int loadRadius;
	private readonly int unloadRadius;
	private readonly int generationBudget;
	private const int LightingPromotionBudget = 4;
	private readonly HashSet<(int X, int Z)> loadedHorizontal = new HashSet<(int, int)>();
	private readonly HashSet<(int X, int Z)> litHorizontal = new HashSet<(int, int)>();
	private readonly Dictionary<ChunkCoordinate, Dictionary<int, VoxelCell>> overrides =
		new Dictionary<ChunkCoordinate, Dictionary<int, VoxelCell>>();
	private readonly Dictionary<(int X, int Z), (int MinimumY, int MaximumY)> residentVerticalRanges =
		new Dictionary<(int, int), (int, int)>();
	private readonly List<(int X, int Z)> generatedThisFrame = new List<(int, int)>();
	private readonly List<(int X, int Z)> promotedLightingThisFrame = new List<(int, int)>();
	private VoxelLighting lighting;

	internal VoxelTestChunkStreamer(
		VoxelTestWorldData data,
		VoxelTestMaterialIds materials,
		int loadRadius = 8,
		int unloadRadius = 10,
		int generationBudget = 4
	)
	{
		this.data = data ?? throw new ArgumentNullException(nameof(data));
		this.materials = materials;

		if (loadRadius < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(loadRadius));
		}

		if (unloadRadius < loadRadius)
		{
			throw new ArgumentOutOfRangeException(nameof(unloadRadius));
		}

		if (generationBudget <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(generationBudget));
		}

		this.loadRadius = loadRadius;
		this.unloadRadius = unloadRadius;
		this.generationBudget = generationBudget;
		World = new VoxelWorld();
	}

	internal VoxelWorld World { get; }
	internal int LoadedHorizontalCount => loadedHorizontal.Count;
	internal int LitHorizontalCount => litHorizontal.Count;
	internal int PendingHorizontalCount { get; private set; }
	internal int PendingLightingHorizontalCount { get; private set; }
	internal int OverrideCount => overrides.Values.Sum(chunk => chunk.Count);
	internal int MaximumResidentHorizontalCount => (unloadRadius * 2 + 1) * (unloadRadius * 2 + 1);
	internal int MaximumResidentChunkCount => MaximumResidentHorizontalCount
		* (FloorChunk(VoxelTestWorldGenerator.WorldMaximumY - 1)
			- FloorChunk(VoxelTestWorldGenerator.WorldMinimumY)
			+ 1);
	internal bool IsSettled => PendingHorizontalCount == 0;
	internal IReadOnlyList<(int X, int Z)> GeneratedThisFrame => generatedThisFrame;
	internal IReadOnlyList<(int X, int Z)> PromotedLightingThisFrame => promotedLightingThisFrame;

	internal void AttachLighting(VoxelLighting value)
	{
		if (value == null)
		{
			throw new ArgumentNullException(nameof(value));
		}

		if (lighting != null)
		{
			throw new InvalidOperationException("Voxel lighting is already attached to the streamer.");
		}

		if (loadedHorizontal.Count != 0)
		{
			throw new InvalidOperationException("Voxel lighting must be attached before streaming begins.");
		}

		lighting = value;
	}

	internal int Update(Vector3 cameraPosition)
	{
		return Update(cameraPosition, focus: null);
	}

	internal int Update(
		Camera camera,
		float maxRenderDistance,
		bool cullingEnabled = true
	)
	{
		ArgumentNullException.ThrowIfNull(camera);

		return Update(
			camera.Position,
			new StreamingFocus(camera, maxRenderDistance, cullingEnabled)
		);
	}

	private int Update(
		Vector3 cameraPosition,
		StreamingFocus? focus
	)
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
		List<(int X, int Z, int DistanceSquared, StreamingTier Tier)> pending = BuildPending(
			center.X,
			center.Z,
			focus
		);
		generatedThisFrame.Clear();
		int generated = Math.Min(generationBudget, pending.Count);

		for (int i = 0; i < generated; i++)
		{
			(int x, int z, _, _) = pending[i];
			LoadHorizontal(x, z);
			generatedThisFrame.Add((x, z));
		}

		PendingHorizontalCount = pending.Count - generated;
		PromoteFocusedLighting(center.X, center.Z, focus);
		return generated;
	}

	internal bool SetVoxel(int x, int y, int z, VoxelCell value)
	{
		if (!IsInsideWorld(x, y, z))
		{
			return false;
		}

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
				{
					overrides.Remove(coordinate);
				}
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

		if (!loadedHorizontal.Contains((coordinate.X, coordinate.Z)))
		{
			return true;
		}

		EnsureResidentVerticalRange(coordinate.X, coordinate.Z, coordinate.Y);
		return World.SetVoxel(x, y, z, value);
	}

	internal Vector3 ClampPosition(Vector3 position)
	{
		float horizontalMinimum = VoxelTestWorldGenerator.WorldMinimum + 0.001f;
		float horizontalMaximum = VoxelTestWorldGenerator.WorldMaximum - 0.001f;
		float verticalMinimum = VoxelTestWorldGenerator.WorldMinimumY + 0.001f;
		float verticalMaximum = VoxelTestWorldGenerator.WorldMaximumY - 0.001f;

		return new Vector3(
			Math.Clamp(position.X, horizontalMinimum, horizontalMaximum),
			Math.Clamp(position.Y, verticalMinimum, verticalMaximum),
			Math.Clamp(position.Z, horizontalMinimum, horizontalMaximum)
		);
	}

	private List<(int X, int Z, int DistanceSquared, StreamingTier Tier)> BuildPending(
		int centerX,
		int centerZ,
		StreamingFocus? focus
	)
	{
		List<(int X, int Z, int DistanceSquared, StreamingTier Tier)> result =
			new List<(int, int, int, StreamingTier)>();

		for (int z = centerZ - loadRadius; z <= centerZ + loadRadius; z++)
		{
			for (int x = centerX - loadRadius; x <= centerX + loadRadius; x++)
			{
				if (!IsInsideHorizontalChunkBounds(x, z) || loadedHorizontal.Contains((x, z)))
				{
					continue;
				}

				int offsetX = x - centerX;
				int offsetZ = z - centerZ;
				result.Add((
					x,
					z,
					offsetX * offsetX + offsetZ * offsetZ,
					focus?.GetTier(x, z) ?? StreamingTier.Visible
				));
			}
		}

		return result
			.OrderBy(item => item.Tier)
			.ThenBy(item => item.DistanceSquared)
			.ThenBy(item => item.Z)
			.ThenBy(item => item.X)
			.ToList();
	}

	private void PromoteFocusedLighting(
		int centerX,
		int centerZ,
		StreamingFocus? focus
	)
	{
		promotedLightingThisFrame.Clear();

		if (lighting == null)
		{
			PendingLightingHorizontalCount = 0;
			return;
		}

		List<(int X, int Z, int DistanceSquared, StreamingTier Tier)> pending =
			loadedHorizontal
				.Where(coordinate => !litHorizontal.Contains(coordinate))
				.Select(coordinate =>
				{
					int offsetX = coordinate.X - centerX;
					int offsetZ = coordinate.Z - centerZ;
					return (
						X: coordinate.X,
						Z: coordinate.Z,
						DistanceSquared: offsetX * offsetX + offsetZ * offsetZ,
						Tier: focus?.GetTier(coordinate.X, coordinate.Z)
							?? StreamingTier.Visible
					);
				})
				.Where(item => item.Tier != StreamingTier.Background)
				.OrderBy(item => item.Tier)
				.ThenBy(item => item.DistanceSquared)
				.ThenBy(item => item.Z)
				.ThenBy(item => item.X)
				.ToList();
		int promotionBudget = litHorizontal.Count == 0
			? 1
			: LightingPromotionBudget;
		int promoted = Math.Min(promotionBudget, pending.Count);

		for (int index = 0; index < promoted; index++)
		{
			(int x, int z, _, _) = pending[index];
			PromoteLightingColumn(x, z);
			promotedLightingThisFrame.Add((x, z));
		}

		PendingLightingHorizontalCount = pending.Count - promoted;
	}

	private void PromoteLightingColumn(int chunkX, int chunkZ)
	{
		if (!residentVerticalRanges.TryGetValue(
			(chunkX, chunkZ),
			out (int MinimumY, int MaximumY) range
		))
		{
			throw new InvalidOperationException("A loaded column is missing its vertical range.");
		}

		for (int chunkY = range.MinimumY; chunkY <= range.MaximumY; chunkY++)
		{
			lighting.LoadChunk(
				new ChunkCoordinate(chunkX, chunkY, chunkZ),
				skyExposedAbove: chunkY == range.MaximumY
			);
		}

		litHorizontal.Add((chunkX, chunkZ));
	}

	private readonly struct StreamingFocus
	{
		private readonly Vector3 cameraPosition;
		private readonly ViewFrustum frustum;
		private readonly float maximumDistanceSquared;
		private readonly bool cullingEnabled;

		internal StreamingFocus(
			Camera camera,
			float maximumDistance,
			bool cullingEnabled
		)
		{
			if (!float.IsFinite(maximumDistance) || maximumDistance <= 0)
			{
				throw new ArgumentOutOfRangeException(nameof(maximumDistance));
			}

			cameraPosition = camera.Position;
			frustum = ViewFrustum.FromCamera(camera);
			maximumDistanceSquared = maximumDistance * maximumDistance;
			this.cullingEnabled = cullingEnabled;
		}

		internal StreamingTier GetTier(int chunkX, int chunkZ)
		{
			if (IsVisibleColumn(chunkX, chunkZ))
			{
				return StreamingTier.Visible;
			}

			for (int offsetZ = -1; offsetZ <= 1; offsetZ++)
			{
				for (int offsetX = -1; offsetX <= 1; offsetX++)
				{
					if (IsVisibleColumn(chunkX + offsetX, chunkZ + offsetZ))
					{
						return StreamingTier.Halo;
					}
				}
			}

			return StreamingTier.Background;
		}

		private bool IsVisibleColumn(int chunkX, int chunkZ)
		{
			Vector3 minimum = new Vector3(
				chunkX * VoxelWorld.ChunkSize,
				VoxelTestWorldGenerator.WorldMinimumY,
				chunkZ * VoxelWorld.ChunkSize
			);
			Vector3 maximum = new Vector3(
				minimum.X + VoxelWorld.ChunkSize,
				VoxelTestWorldGenerator.WorldMaximumY,
				minimum.Z + VoxelWorld.ChunkSize
			);
			float closestX = Math.Clamp(cameraPosition.X, minimum.X, maximum.X);
			float closestZ = Math.Clamp(cameraPosition.Z, minimum.Z, maximum.Z);
			float offsetX = closestX - cameraPosition.X;
			float offsetZ = closestZ - cameraPosition.Z;

			if (offsetX * offsetX + offsetZ * offsetZ > maximumDistanceSquared)
			{
				return false;
			}

			return !cullingEnabled
				|| frustum.Intersects(new AxisAlignedBoundingBox(minimum, maximum));
		}
	}

	private void LoadHorizontal(int chunkX, int chunkZ)
	{
		(int minimumY, int maximumY) = data.GetVerticalChunkRange(chunkX, chunkZ);

		foreach (ChunkCoordinate coordinate in overrides.Keys)
		{
			if (coordinate.X == chunkX && coordinate.Z == chunkZ)
			{
				minimumY = Math.Min(minimumY, coordinate.Y);
				maximumY = Math.Max(maximumY, coordinate.Y);
			}
		}

		residentVerticalRanges.Add((chunkX, chunkZ), (minimumY, maximumY));

		for (int chunkY = minimumY; chunkY <= maximumY; chunkY++)
		{
			ChunkCoordinate coordinate = new ChunkCoordinate(chunkX, chunkY, chunkZ);
			VoxelCell[] cells = data.GenerateChunk(coordinate, materials);

			if (overrides.TryGetValue(coordinate, out Dictionary<int, VoxelCell> chunkOverrides))
			{
				foreach (KeyValuePair<int, VoxelCell> item in chunkOverrides)
				{
					cells[item.Key] = item.Value;
				}
			}

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
		{
			return;
		}

		foreach ((int x, int z) in distant)
		{
			if (residentVerticalRanges.Remove((x, z), out (int MinimumY, int MaximumY) range))
			{
				if (litHorizontal.Remove((x, z)))
				{
					for (int chunkY = range.MinimumY; chunkY <= range.MaximumY; chunkY++)
					{
						lighting?.UnloadChunk(new ChunkCoordinate(x, chunkY, z));
					}
				}
			}
		}

		HashSet<(int X, int Z)> distantSet = distant.ToHashSet();
		ChunkCoordinate[] chunks = World.LoadedChunks
			.Where(chunk => distantSet.Contains((chunk.Coordinate.X, chunk.Coordinate.Z)))
			.Select(chunk => chunk.Coordinate)
			.ToArray();

		foreach (ChunkCoordinate coordinate in chunks)
		{
			World.RemoveChunk(coordinate);
		}

		foreach ((int x, int z) in distant)
		{
			loadedHorizontal.Remove((x, z));
		}
	}

	private void EnsureResidentVerticalRange(int chunkX, int chunkZ, int chunkY)
	{
		if (!residentVerticalRanges.TryGetValue((chunkX, chunkZ), out (int MinimumY, int MaximumY) range))
		{
			throw new InvalidOperationException("A loaded horizontal chunk is missing its resident vertical range.");
		}

		if (chunkY >= range.MinimumY && chunkY <= range.MaximumY)
		{
			return;
		}

		int minimumY = Math.Min(range.MinimumY, chunkY);
		int maximumY = Math.Max(range.MaximumY, chunkY);

		bool isLit = litHorizontal.Contains((chunkX, chunkZ));

		if (isLit && maximumY != range.MaximumY)
		{
			lighting?.SetSkyExposedAbove(new ChunkCoordinate(chunkX, range.MaximumY, chunkZ), false);
		}

		for (int y = minimumY; y <= maximumY; y++)
		{
			if (y >= range.MinimumY && y <= range.MaximumY)
			{
				continue;
			}

			if (isLit)
			{
				lighting?.LoadChunk(
					new ChunkCoordinate(chunkX, y, chunkZ),
					skyExposedAbove: y == maximumY
				);
			}
		}

		residentVerticalRanges[(chunkX, chunkZ)] = (minimumY, maximumY);
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

	private enum StreamingTier
	{
		Visible,
		Halo,
		Background,
	}
}
