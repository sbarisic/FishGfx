using System;
using System.Collections.Generic;

namespace FishGfx.Voxels;

public sealed partial class VoxelLighting
{
	private RebuildTransaction CreateTransaction()
	{
		List<ChunkCoordinate> ordered = new List<ChunkCoordinate>(residents.Keys);
		ordered.Sort(CompareCoordinates);
		List<WorkingChunk> chunks = new List<WorkingChunk>(ordered.Count);
		Dictionary<ChunkCoordinate, WorkingChunk> lookup =
			new Dictionary<ChunkCoordinate, WorkingChunk>(ordered.Count);

		foreach (ChunkCoordinate coordinate in ordered)
		{
			WorkingChunk chunk = new WorkingChunk(
				coordinate,
				residents[coordinate].SkyExposedAbove
			);
			VoxelChunkContent content = world.CaptureChunkContent(coordinate);
			chunk.CaptureMaterialContent(
				content,
				GetMaterialPreparationWork(content)
			);
			chunk.Resident = residents[coordinate];
			chunks.Add(chunk);
			lookup.Add(coordinate, chunk);
		}

		return new RebuildTransaction(
			chunks,
			lookup,
			new Dictionary<ChunkCoordinate, RemovedChunkTombstone>(removedTombstones)
		);
	}

	private void InitializeNextCell(RebuildTransaction rebuild)
	{
		InitializeNextMaterial(rebuild);
		rebuild.RemainingInitializationWork--;
	}

	private static void InitializeFullDirectTraversal(RebuildTransaction rebuild)
	{
		if (rebuild.DirectInitialized)
		{
			return;
		}

		foreach (WorkingChunk chunk in rebuild.Chunks)
		{
			HorizontalChunkCoordinate horizontal = new HorizontalChunkCoordinate(
				chunk.Coordinate.X,
				chunk.Coordinate.Z
			);
			if (!rebuild.DirectGroups.TryGetValue(horizontal, out List<WorkingChunk> group))
			{
				group = new List<WorkingChunk>();
				rebuild.DirectGroups.Add(horizontal, group);
				rebuild.DirectGroupCoordinates.Add(horizontal);
			}
			group.Add(chunk);
		}

		rebuild.DirectGroupCoordinates.Sort(CompareHorizontalCoordinates);
		foreach (List<WorkingChunk> group in rebuild.DirectGroups.Values)
		{
			group.Sort((left, right) => right.Coordinate.Y.CompareTo(left.Coordinate.Y));
		}

		rebuild.DirectRemaining = (long)rebuild.Chunks.Count * VoxelWorld.ChunkVolume;
		rebuild.DirectInitialized = true;
	}

	private static bool TryPrepareNextFullDirectCell(RebuildTransaction rebuild)
	{
		InitializeFullDirectTraversal(rebuild);
		while (rebuild.DirectGroupIndex < rebuild.DirectGroupCoordinates.Count)
		{
			if (rebuild.ActiveDirectGroup == null)
			{
				HorizontalChunkCoordinate horizontal =
					rebuild.DirectGroupCoordinates[rebuild.DirectGroupIndex];
				rebuild.ActiveDirectGroup = rebuild.DirectGroups[horizontal];
				rebuild.DirectLocalColumnIndex = 0;
				rebuild.DirectChunkIndex = 0;
				rebuild.DirectY = VoxelWorld.ChunkSize - 1;
				rebuild.DirectIncoming = 0;
				rebuild.DirectChunkStarted = false;
			}

			if (rebuild.DirectLocalColumnIndex
				>= VoxelWorld.ChunkSize * VoxelWorld.ChunkSize)
			{
				rebuild.ActiveDirectGroup = null;
				rebuild.DirectGroupIndex++;
				continue;
			}

			if (rebuild.DirectChunkIndex >= rebuild.ActiveDirectGroup.Count)
			{
				rebuild.DirectLocalColumnIndex++;
				rebuild.DirectChunkIndex = 0;
				rebuild.DirectY = VoxelWorld.ChunkSize - 1;
				rebuild.DirectIncoming = 0;
				rebuild.DirectChunkStarted = false;
				continue;
			}

			WorkingChunk working = rebuild.ActiveDirectGroup[rebuild.DirectChunkIndex];
			if (!rebuild.DirectChunkStarted)
			{
				if (rebuild.DirectChunkIndex == 0
					|| working.Coordinate.Y
						!= rebuild.ActiveDirectGroup[
							rebuild.DirectChunkIndex - 1
						].Coordinate.Y - 1)
				{
					rebuild.DirectIncoming = 0;
				}

				if (working.SkyExposedAbove)
				{
					rebuild.DirectIncoming = 15;
				}

				rebuild.DirectChunkStarted = true;
			}

			rebuild.ActiveDirectWorking = working;
			return true;
		}

		return false;
	}

	private static void ProcessNextFullDirectCell(RebuildTransaction rebuild)
	{
		WorkingChunk working = rebuild.ActiveDirectWorking;
		int x = rebuild.DirectLocalColumnIndex % VoxelWorld.ChunkSize;
		int z = rebuild.DirectLocalColumnIndex / VoxelWorld.ChunkSize;
		int index = Index(x, rebuild.DirectY, z);
		byte direct = Subtract(
			rebuild.DirectIncoming,
			GetOpacity(working.MaterialSignatures[index])
		);
		rebuild.DirectIncoming = direct;
		working.DirectSky[index] = direct;

		ushort light = working.Lights[index];
		if (direct > (byte)((light >> 12) & 0xf))
		{
			working.Lights[index] = (ushort)((light & 0x0fff) | (direct << 12));
			Enqueue(rebuild, working, index);
		}

		rebuild.DirectRemaining--;
		rebuild.DirectY--;
		if (rebuild.DirectY < 0)
		{
			rebuild.DirectY = VoxelWorld.ChunkSize - 1;
			rebuild.DirectChunkIndex++;
			rebuild.DirectChunkStarted = false;
		}
	}

	private static void CompareNextCell(RebuildTransaction rebuild)
	{
		WorkingChunk working = rebuild.Chunks[rebuild.CompareChunkIndex];
		ResidentChunk resident = working.Resident;
		int index = rebuild.CompareCellIndex;
		if (index == 0 && (
			resident.HasPublishedSkyExposure
				? resident.PublishedSkyExposedAbove != working.SkyExposedAbove
				: working.SkyExposedAbove
		))
		{
			AddSkyHaloTargets(working.Coordinate, rebuild.InvalidationTargets);
		}

		if (resident.PublishedLights == null)
		{
			working.Changed = true;
			rebuild.InvalidationTargets.Add(working.Coordinate);
			if (working.Lights[index] != 0)
			{
				AddHaloTargets(working.Coordinate, index, rebuild.InvalidationTargets);
			}
		}
		else if (resident.PublishedLights[index] != working.Lights[index])
		{
			working.Changed = true;
			rebuild.InvalidationTargets.Add(working.Coordinate);
			AddHaloTargets(working.Coordinate, index, rebuild.InvalidationTargets);
		}

		rebuild.CompareCellIndex++;
		if (rebuild.CompareCellIndex == VoxelWorld.ChunkVolume)
		{
			rebuild.CompareCellIndex = 0;
			rebuild.CompareChunkIndex++;
		}
	}

	private static void InitializeFullTombstoneComparison(RebuildTransaction rebuild)
	{
		if (rebuild.Phase == RebuildPhase.CompareTombstones)
		{
			return;
		}

		rebuild.TombstoneCoordinates.AddRange(rebuild.RemovedTombstones.Keys);
		rebuild.TombstoneCoordinates.Sort(CompareCoordinates);
		rebuild.Phase = RebuildPhase.CompareTombstones;
	}

	private bool TryPrepareNextFullTombstoneCell(RebuildTransaction rebuild)
	{
		while (rebuild.TombstoneCoordinateIndex < rebuild.TombstoneCoordinates.Count)
		{
			ChunkCoordinate coordinate =
				rebuild.TombstoneCoordinates[rebuild.TombstoneCoordinateIndex];
			RemovedChunkTombstone captured = rebuild.RemovedTombstones[coordinate];
			if (removedTombstones.TryGetValue(coordinate, out RemovedChunkTombstone current)
				&& ReferenceEquals(current, captured))
			{
				rebuild.ActiveComparisonTombstone = captured;
				return true;
			}

			rebuild.TombstoneCoordinateIndex++;
			rebuild.TombstoneCellIndex = 0;
		}

		return false;
	}

	private static void CompareNextFullTombstoneCell(RebuildTransaction rebuild)
	{
		ChunkCoordinate coordinate =
			rebuild.TombstoneCoordinates[rebuild.TombstoneCoordinateIndex];
		RemovedChunkTombstone tombstone = rebuild.ActiveComparisonTombstone;
		int index = rebuild.TombstoneCellIndex;
		if (index == 0 && tombstone.PublishedSkyExposedAbove)
		{
			AddSkyHaloTargets(coordinate, rebuild.InvalidationTargets);
		}

		if (tombstone.PublishedLights[index] != 0)
		{
			AddHaloTargets(coordinate, index, rebuild.InvalidationTargets);
		}

		AdvanceCellCursor(
			ref rebuild.TombstoneCoordinateIndex,
			ref rebuild.TombstoneCellIndex
		);
	}

	private void CommitTransaction(
		RebuildTransaction rebuild,
		ref List<(ChunkCoordinate Coordinate, long Revision)> invalidated
	)
	{
		foreach (WorkingChunk working in rebuild.Chunks)
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

			if (!working.Changed)
			{
				continue;
			}

			resident.PublishedLights = working.Lights;
		}

		RemoveCapturedTombstones(rebuild.RemovedTombstones);

		List<ChunkCoordinate> orderedTargets =
			new List<ChunkCoordinate>(rebuild.InvalidationTargets);
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

	private ushort GetMaterialSignature(ushort materialId)
	{
		return materialId < materialSignatureLookup.Length
			? materialSignatureLookup[materialId]
			: (ushort)15;
	}

	private void HandleWorldContentChanged(VoxelWorldContentChange change)
	{
		lock (sync)
		{
			if (disposed || !residents.ContainsKey(change.Coordinate))
			{
				return;
			}

			if (change.IsBulk)
			{
				dirtyWorldChunks.Add(change.Coordinate);
				dirtyWorldCells.Remove(change.Coordinate);
				return;
			}

			if (GetMaterialSignature(change.PreviousMaterialId)
				== GetMaterialSignature(change.MaterialId)
				|| dirtyWorldChunks.Contains(change.Coordinate))
			{
				return;
			}

			if (!dirtyWorldCells.TryGetValue(
				change.Coordinate,
				out HashSet<int> cells
			))
			{
				cells = new HashSet<int>();
				dirtyWorldCells.Add(change.Coordinate, cells);
			}
			cells.Add(change.LocalIndex);
		}
	}

}
