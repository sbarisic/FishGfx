using System;
using System.Collections.Generic;

namespace FishGfx.Voxels;

public sealed partial class VoxelLighting
{
	private IncrementalTransaction CreateIncrementalTransaction()
	{
		Dictionary<ChunkCoordinate, IncrementalSourceChunk> sources =
			new Dictionary<ChunkCoordinate, IncrementalSourceChunk>(residents.Count);
		foreach (KeyValuePair<ChunkCoordinate, ResidentChunk> item in residents)
		{
			sources.Add(item.Key, new IncrementalSourceChunk(item.Value));
		}

		IncrementalTransaction incremental = new IncrementalTransaction(sources);

		incremental.AddedCoordinates.AddRange(addedChunks);
		incremental.AddedCoordinates.Sort(CompareCoordinates);
		foreach (ChunkCoordinate coordinate in incremental.AddedCoordinates)
		{
			if (!sources.TryGetValue(coordinate, out IncrementalSourceChunk source)
				|| source.PublishedLights != null)
			{
				continue;
			}

			VoxelChunkContent content = world.CaptureChunkContent(coordinate);
			int work = GetMaterialPreparationWork(content);
			incremental.AddedMaterialContents.Add(coordinate, content);
			incremental.AddedPreparationWork.Add(coordinate, work);
			incremental.RemainingAddedPreparationWork += work;
		}

		List<ChunkCoordinate> orderedSky = new List<ChunkCoordinate>(skyChangedChunks);
		orderedSky.Sort(CompareCoordinates);
		foreach (ChunkCoordinate coordinate in orderedSky)
		{
			if (!sources.TryGetValue(coordinate, out IncrementalSourceChunk source))
			{
				continue;
			}

			incremental.SkyChanges.Add(
				new SkyExposureChange(coordinate, source.DesiredSkyExposedAbove)
			);
		}

		HashSet<ChunkCoordinate> dirtyCoordinates =
			new HashSet<ChunkCoordinate>(dirtyWorldChunks);
		foreach (KeyValuePair<ChunkCoordinate, HashSet<int>> item in dirtyWorldCells)
		{
			if (dirtyWorldChunks.Contains(item.Key))
			{
				continue;
			}

			dirtyCoordinates.Add(item.Key);
			incremental.ExactDirtyCellSets.Add(item.Key, item.Value);
		}
		incremental.DirtyCoordinates.AddRange(dirtyCoordinates);
		incremental.DirtyCoordinates.Sort(CompareCoordinates);

		incremental.RemovedCoordinateList.AddRange(removedChunks);
		incremental.RemovedCoordinateList.Sort(CompareCoordinates);
		foreach (ChunkCoordinate coordinate in incremental.RemovedCoordinateList)
		{
			incremental.RemovedCoordinates.Add(coordinate);
			if (removedTombstones.TryGetValue(coordinate, out RemovedChunkTombstone tombstone))
			{
				incremental.RemovedTombstones.Add(coordinate, tombstone);
			}
		}

		ClearPendingIncrementalChanges();
		return incremental.AddedCoordinates.Count == 0
			&& incremental.SkyChanges.Count == 0
			&& incremental.DirtyCoordinates.Count == 0
			&& incremental.RemovedCoordinateList.Count == 0
			? null
			: incremental;
	}

	private IncrementalStepResult ProcessIncrementalStep(
		IncrementalTransaction incremental,
		bool canConsumeWork,
		ref List<(ChunkCoordinate Coordinate, long Revision)> invalidated
	)
	{
		while (true)
		{
			if (incremental.DiscardAtCommit
				&& incremental.Phase >= IncrementalPhase.CompareWorking)
			{
				RequeueDiscardedIncrementalTransaction(incremental);
				return IncrementalStepResult.Completed;
			}

				switch (incremental.Phase)
				{
				case IncrementalPhase.PrepareAdded:
					IncrementalStepResult addedResult = PrepareNextAddedMaterial(
						incremental,
						canConsumeWork
					);
					if (addedResult == IncrementalStepResult.Completed)
					{
						continue;
					}

					return addedResult;

				case IncrementalPhase.PrepareSky:
					if (incremental.SkyChangeIndex >= incremental.SkyChanges.Count)
					{
						incremental.Phase = IncrementalPhase.PrepareDirty;
						continue;
					}
					SkyExposureChange skyChange = incremental.SkyChanges[incremental.SkyChangeIndex];
					if (!incremental.Sources.TryGetValue(
						skyChange.Coordinate,
						out IncrementalSourceChunk skySource
					)
						|| !residents.TryGetValue(
							skyChange.Coordinate,
							out ResidentChunk skyResident
						)
						|| !ReferenceEquals(skyResident, skySource.Resident))
					{
						incremental.SkyChangeIndex++;
						incremental.SkyCellIndex = 0;
						continue;
					}
					if (!canConsumeWork)
					{
						return IncrementalStepResult.NeedsBudget;
					}

					IncrementalWorkingChunk skyWorking =
						EnsureIncrementalWorking(incremental, skyChange.Coordinate);
					if (skyWorking == null)
					{
						incremental.SkyChangeIndex++;
						incremental.SkyCellIndex = 0;
						continue;
					}
					if (incremental.SkyCellIndex == 0)
					{
						skyWorking.SkyExposedAbove = skyChange.SkyExposedAbove;
						skyWorking.SkyExposureChanged =
							!skyWorking.Resident.HasPublishedSkyExposure
							|| skyWorking.Resident.PublishedSkyExposedAbove
								!= skyChange.SkyExposedAbove;
					}
					int skyX = incremental.SkyCellIndex % VoxelWorld.ChunkSize;
					int skyZ = incremental.SkyCellIndex / VoxelWorld.ChunkSize;
					incremental.DirectColumnSet.Add(new WorldColumn(
						skyChange.Coordinate.X * VoxelWorld.ChunkSize + skyX,
						skyChange.Coordinate.Z * VoxelWorld.ChunkSize + skyZ
					));
					EnqueueIncremental(
						incremental,
						skySource,
						Index(skyX, VoxelWorld.ChunkSize - 1, skyZ)
					);
					incremental.SkyCellIndex++;
					if (incremental.SkyCellIndex == VoxelWorld.ChunkSize * VoxelWorld.ChunkSize)
					{
						incremental.SkyCellIndex = 0;
						incremental.SkyChangeIndex++;
					}
					return IncrementalStepResult.Consumed;

				case IncrementalPhase.PrepareDirty:
					if (incremental.DirtyCoordinateIndex >= incremental.DirtyCoordinates.Count)
					{
						incremental.Phase = IncrementalPhase.PrepareRemovedColumns;
						continue;
					}
					ChunkCoordinate dirtyCoordinate =
						incremental.DirtyCoordinates[incremental.DirtyCoordinateIndex];
					if (!incremental.Sources.TryGetValue(
						dirtyCoordinate,
						out IncrementalSourceChunk dirtySource
					)
						|| dirtySource.PublishedLights == null
						|| !residents.TryGetValue(dirtyCoordinate, out ResidentChunk dirtyResident)
						|| !ReferenceEquals(dirtyResident, dirtySource.Resident))
					{
						incremental.DirtyCoordinateIndex++;
						incremental.DirtyCellIndex = 0;
						continue;
					}
					if (!canConsumeWork)
					{
						return IncrementalStepResult.NeedsBudget;
					}

					int dirtyCellCount = VoxelWorld.ChunkVolume;
					int dirtyIndex = incremental.DirtyCellIndex;
					if (incremental.ExactDirtyCellSets.TryGetValue(
						dirtyCoordinate,
						out HashSet<int> exactDirtySet
					))
					{
						if (!incremental.ExactDirtyCells.TryGetValue(
							dirtyCoordinate,
							out List<int> exactDirty
						))
						{
							exactDirty = new List<int>(exactDirtySet);
							exactDirty.Sort();
							incremental.ExactDirtyCells.Add(dirtyCoordinate, exactDirty);
						}
						dirtyCellCount = exactDirty.Count;
						dirtyIndex = exactDirty[incremental.DirtyCellIndex];
					}
					if (!incremental.DirtyMaterialCells.TryGetValue(
						dirtyCoordinate,
						out ReadOnlyMemory<VoxelCell> dirtyCells
					))
					{
						dirtyCells = world.CaptureChunkCells(dirtyCoordinate);
						incremental.DirtyMaterialCells.Add(dirtyCoordinate, dirtyCells);
					}
					ushort dirtySignature = GetMaterialSignature(
						dirtyCells.Span[dirtyIndex].MaterialId
					);
					IncrementalWorkingChunk dirty = null;
					ushort previousSignature;
					if (incremental.Chunks.TryGetValue(dirtyCoordinate, out dirty))
					{
						previousSignature = dirty.MaterialSignatures[dirtyIndex];
					}
					else
					{
						previousSignature = dirtySource.MaterialSignatures[dirtyIndex];
					}

					if (previousSignature != dirtySignature)
					{
						dirty ??= EnsureIncrementalWorking(incremental, dirtyCoordinate);
						if (dirty == null || dirty.IsNew)
						{
							incremental.DirtyCoordinateIndex++;
							incremental.DirtyCellIndex = 0;
							continue;
						}
						dirty.MaterialSignatures[dirtyIndex] = dirtySignature;
						if (GetOpacity(previousSignature) != GetOpacity(dirtySignature))
						{
							GetLocalCoordinates(dirtyIndex, out int dirtyX, out _, out int dirtyZ);
							incremental.DirectColumnSet.Add(new WorldColumn(
								dirtyCoordinate.X * VoxelWorld.ChunkSize + dirtyX,
								dirtyCoordinate.Z * VoxelWorld.ChunkSize + dirtyZ
							));
						}
						EnqueueIncrementalWithNeighbors(incremental, dirtyCoordinate, dirtyIndex);
					}
					incremental.DirtyCellIndex++;
					if (incremental.DirtyCellIndex == dirtyCellCount)
					{
						incremental.DirtyCellIndex = 0;
						incremental.DirtyCoordinateIndex++;
					}
					return IncrementalStepResult.Consumed;

				case IncrementalPhase.PrepareRemovedColumns:
					if (incremental.RemovedColumnCoordinateIndex
						>= incremental.RemovedCoordinateList.Count)
					{
						incremental.Phase = IncrementalPhase.PrepareRemovedBoundaries;
						continue;
					}
					if (!canConsumeWork)
					{
						return IncrementalStepResult.NeedsBudget;
					}

					ChunkCoordinate removedColumnCoordinate =
						incremental.RemovedCoordinateList[incremental.RemovedColumnCoordinateIndex];
					int removedX = incremental.RemovedColumnIndex % VoxelWorld.ChunkSize;
					int removedZ = incremental.RemovedColumnIndex / VoxelWorld.ChunkSize;
					incremental.DirectColumnSet.Add(new WorldColumn(
						removedColumnCoordinate.X * VoxelWorld.ChunkSize + removedX,
						removedColumnCoordinate.Z * VoxelWorld.ChunkSize + removedZ
					));
					incremental.RemovedColumnIndex++;
					if (incremental.RemovedColumnIndex == VoxelWorld.ChunkSize * VoxelWorld.ChunkSize)
					{
						incremental.RemovedColumnIndex = 0;
						incremental.RemovedColumnCoordinateIndex++;
					}
					return IncrementalStepResult.Consumed;

				case IncrementalPhase.PrepareRemovedBoundaries:
					if (incremental.RemovedBoundaryCoordinateIndex
						>= incremental.RemovedCoordinateList.Count)
					{
						InitializeIncrementalDirectTraversal(incremental);
						continue;
					}
					if (!canConsumeWork)
					{
						return IncrementalStepResult.NeedsBudget;
					}

					EnqueueRemovedBoundaryNeighbor(
						incremental,
						incremental.RemovedCoordinateList[
							incremental.RemovedBoundaryCoordinateIndex
						],
						incremental.RemovedBoundaryIndex
					);
					incremental.RemovedBoundaryIndex++;
					if (incremental.RemovedBoundaryIndex
						== 6 * VoxelWorld.ChunkSize * VoxelWorld.ChunkSize)
					{
						incremental.RemovedBoundaryIndex = 0;
						incremental.RemovedBoundaryCoordinateIndex++;
					}
					return IncrementalStepResult.Consumed;

				case IncrementalPhase.DirectSky:
					if (!canConsumeWork && incremental.DirectRemaining > 0)
					{
						return IncrementalStepResult.NeedsBudget;
					}

					if (!TryPrepareNextIncrementalDirectCell(incremental))
					{
						incremental.Phase = IncrementalPhase.Relax;
						continue;
					}
					ProcessIncrementalDirectCell(incremental);
					return IncrementalStepResult.Consumed;

				case IncrementalPhase.Relax:
					if (incremental.Relaxation.Count == 0)
					{
						if (incremental.DiscardAtCommit)
						{
							RequeueDiscardedIncrementalTransaction(incremental);
							return IncrementalStepResult.Completed;
						}
						InitializeIncrementalComparison(incremental);
						continue;
					}
					if (!canConsumeWork)
					{
						return IncrementalStepResult.NeedsBudget;
					}

					RelaxNextCell(incremental);
					return IncrementalStepResult.Consumed;

				case IncrementalPhase.CompareWorking:
					if (!TryPrepareNextIncrementalComparisonCell(incremental))
					{
						incremental.Phase = IncrementalPhase.CompareTombstones;
						continue;
					}
					if (!canConsumeWork)
					{
						return IncrementalStepResult.NeedsBudget;
					}

					CompareNextIncrementalCell(incremental);
					return IncrementalStepResult.Consumed;

				case IncrementalPhase.CompareTombstones:
					if (!TryPrepareNextIncrementalTombstoneCell(incremental))
					{
						CommitIncrementalTransaction(incremental, ref invalidated);
						return IncrementalStepResult.Completed;
					}
					if (!canConsumeWork)
					{
						return IncrementalStepResult.NeedsBudget;
					}

					CompareNextIncrementalTombstoneCell(incremental);
					return IncrementalStepResult.Consumed;

				default:
					throw new InvalidOperationException("Unknown incremental lighting phase.");
			}
		}
	}

}
