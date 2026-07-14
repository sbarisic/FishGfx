using System;
using System.Collections.Generic;

namespace FishGfx.Voxels;

public sealed partial class VoxelLighting
{
	private void CommitIncrementalTransaction(
		IncrementalTransaction incremental,
		ref List<(ChunkCoordinate Coordinate, long Revision)> invalidated
	)
	{
		foreach (IncrementalWorkingChunk working in incremental.Chunks.Values)
		{
			if (!residents.TryGetValue(working.Coordinate, out ResidentChunk resident)
				|| !ReferenceEquals(resident, working.Resident))
			{
				continue;
			}

			resident.MaterialSignatures = working.MaterialSignatures;
			resident.PublishedDirectSky = working.DirectSky;
			resident.PublishedSkyExposedAbove = working.SkyExposedAbove;
			resident.HasPublishedSkyExposure = true;
			resident.PublishedLights = working.Lights;
		}

		RemoveCapturedTombstones(incremental.RemovedTombstones);

		List<ChunkCoordinate> orderedTargets =
			new List<ChunkCoordinate>(incremental.InvalidationTargets);
		orderedTargets.Sort(CompareCoordinates);
		foreach (ChunkCoordinate coordinate in orderedTargets)
		{
			if (residents.TryGetValue(coordinate, out ResidentChunk resident)
				&& resident.PublishedLights != null)
			{
				resident.Revision++;
				(invalidated ??= new List<(ChunkCoordinate, long)>()).Add(
					(coordinate, resident.Revision)
				);
			}
		}
	}

	private void RemoveCapturedTombstones(
		Dictionary<ChunkCoordinate, RemovedChunkTombstone> captured
	)
	{
		foreach (KeyValuePair<ChunkCoordinate, RemovedChunkTombstone> item in captured)
		{
			if (removedTombstones.TryGetValue(
				item.Key,
				out RemovedChunkTombstone current
			) && ReferenceEquals(current, item.Value))
			{
				removedTombstones.Remove(item.Key);
			}
		}
	}

	private void RequeueDiscardedIncrementalTransaction(
		IncrementalTransaction incremental
	)
	{
		foreach (IncrementalWorkingChunk working in incremental.Chunks.Values)
		{
			ChunkCoordinate coordinate = working.Coordinate;
			if (!residents.TryGetValue(coordinate, out ResidentChunk resident)
				|| !ReferenceEquals(resident, working.Resident))
			{
				continue;
			}

			if (resident.PublishedLights == null)
			{
				addedChunks.Add(coordinate);
			}
			else
			{
				dirtyWorldChunks.Add(coordinate);
				dirtyWorldCells.Remove(coordinate);
			}
			if (!resident.HasPublishedSkyExposure
				|| resident.PublishedSkyExposedAbove != resident.SkyExposedAbove)
			{
				skyChangedChunks.Add(coordinate);
			}
		}

		foreach (ChunkCoordinate coordinate in incremental.RemovedCoordinates)
		{
			removedChunks.Add(coordinate);
		}
	}

	private bool HasPendingIncrementalChanges()
	{
		return dirtyWorldChunks.Count != 0
			|| dirtyWorldCells.Count != 0
			|| addedChunks.Count != 0
			|| removedChunks.Count != 0
			|| skyChangedChunks.Count != 0;
	}

	private long GetPendingExactCellCount()
	{
		long count = 0;
		foreach (HashSet<int> cells in dirtyWorldCells.Values)
		{
			count += cells.Count;
		}

		return count;
	}

	private void ClearPendingIncrementalChanges()
	{
		dirtyWorldChunks.Clear();
		dirtyWorldCells.Clear();
		addedChunks.Clear();
		removedChunks.Clear();
		skyChangedChunks.Clear();
	}
}
