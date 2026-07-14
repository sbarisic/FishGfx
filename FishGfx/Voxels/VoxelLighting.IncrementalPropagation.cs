using System;
using System.Collections.Generic;

namespace FishGfx.Voxels;

public sealed partial class VoxelLighting
{
	private void InitializeIncrementalDirectTraversal(IncrementalTransaction incremental)
	{
		incremental.DirectColumns.AddRange(incremental.DirectColumnSet);
		incremental.DirectColumns.Sort(CompareWorldColumns);

		foreach (IncrementalSourceChunk source in incremental.Sources.Values)
		{
			if (source.PublishedLights == null
				&& source.Working == null)
			{
				continue;
			}

			HorizontalChunkCoordinate horizontal = new HorizontalChunkCoordinate(
				source.Resident.Coordinate.X,
				source.Resident.Coordinate.Z
			);
			if (!incremental.DirectGroups.TryGetValue(
				horizontal,
				out List<IncrementalSourceChunk> group
			))
			{
				group = new List<IncrementalSourceChunk>();
				incremental.DirectGroups.Add(horizontal, group);
			}
			group.Add(source);
		}

		foreach (List<IncrementalSourceChunk> group in incremental.DirectGroups.Values)
		{
			group.Sort((left, right) =>
				right.Resident.Coordinate.Y.CompareTo(left.Resident.Coordinate.Y));
		}

		long remaining = 0;
		foreach (WorldColumn column in incremental.DirectColumns)
		{
			HorizontalChunkCoordinate horizontal = HorizontalFromWorldColumn(column);
			if (incremental.DirectGroups.TryGetValue(
				horizontal,
				out List<IncrementalSourceChunk> group
			))
			{
				remaining += (long)group.Count * VoxelWorld.ChunkSize;
			}
		}
		incremental.DirectRemaining = remaining;
		incremental.Phase = IncrementalPhase.DirectSky;
	}

	private bool TryPrepareNextIncrementalDirectCell(IncrementalTransaction incremental)
	{
		while (incremental.DirectColumnIndex < incremental.DirectColumns.Count)
		{
			if (incremental.ActiveDirectGroup == null)
			{
				WorldColumn column = incremental.DirectColumns[incremental.DirectColumnIndex];
				HorizontalChunkCoordinate horizontal = HorizontalFromWorldColumn(column);
				if (!incremental.DirectGroups.TryGetValue(
					horizontal,
				out List<IncrementalSourceChunk> group
				))
				{
					incremental.DirectColumnIndex++;
					continue;
				}

				incremental.ActiveDirectGroup = group;
				incremental.DirectChunkIndex = 0;
				incremental.DirectY = VoxelWorld.ChunkSize - 1;
				incremental.DirectIncoming = 0;
				incremental.DirectChunkStarted = false;
				ChunkCoordinate.FromWorld(
					column.X,
					0,
					column.Z,
					out incremental.DirectLocalX,
					out _,
					out incremental.DirectLocalZ
				);
			}

			if (incremental.DirectChunkIndex >= incremental.ActiveDirectGroup.Count)
			{
				incremental.ActiveDirectGroup = null;
				incremental.DirectColumnIndex++;
				continue;
			}

			IncrementalSourceChunk source =
				incremental.ActiveDirectGroup[incremental.DirectChunkIndex];
			if (source.CurrentMaterialSignatures == null || source.CurrentDirectSky == null)
			{
				incremental.DirectChunkIndex++;
				incremental.DirectY = VoxelWorld.ChunkSize - 1;
				incremental.DirectChunkStarted = false;
				continue;
			}

			if (!incremental.DirectChunkStarted)
			{
				if (incremental.DirectChunkIndex == 0
					|| source.Resident.Coordinate.Y
						!= incremental.ActiveDirectGroup[
							incremental.DirectChunkIndex - 1
						].Resident.Coordinate.Y - 1)
				{
					incremental.DirectIncoming = 0;
				}

				bool skyExposedAbove = source.Working?.SkyExposedAbove
					?? source.PublishedSkyExposedAbove;
				if (skyExposedAbove)
				{
					incremental.DirectIncoming = 15;
				}

				incremental.DirectChunkStarted = true;
				if (source.Working?.IsNew == true && incremental.DirectIncoming == 0)
				{
					incremental.DirectRemaining -= VoxelWorld.ChunkSize;
					incremental.DirectChunkIndex++;
					incremental.DirectY = VoxelWorld.ChunkSize - 1;
					incremental.DirectChunkStarted = false;
					continue;
				}
			}

			ReferenceIncrementalSource(incremental, source);
			incremental.ActiveDirectSource = source;
			return true;
		}

		return false;
	}

	private void ProcessIncrementalDirectCell(IncrementalTransaction incremental)
	{
		IncrementalSourceChunk source = incremental.ActiveDirectSource;
		int index = Index(
			incremental.DirectLocalX,
			incremental.DirectY,
			incremental.DirectLocalZ
		);
		byte direct = Subtract(
			incremental.DirectIncoming,
			GetOpacity(source.CurrentMaterialSignatures[index])
		);
		incremental.DirectIncoming = direct;
		if (source.CurrentDirectSky[index] != direct)
		{
			IncrementalWorkingChunk working = EnsureIncrementalWorking(incremental, source);
			if (working != null)
			{
				working.DirectSky[index] = direct;
				EnqueueIncremental(incremental, source, index);
			}
		}

		incremental.DirectRemaining--;
		incremental.DirectY--;
		if (direct == 0
			&& source.Working?.IsNew == true
			&& incremental.DirectY >= 0)
		{
			incremental.DirectRemaining -= incremental.DirectY + 1;
			incremental.DirectY = -1;
		}
		if (incremental.DirectY < 0)
		{
			incremental.DirectY = VoxelWorld.ChunkSize - 1;
			incremental.DirectChunkIndex++;
			incremental.DirectChunkStarted = false;
		}
	}

	private void InitializeIncrementalComparison(IncrementalTransaction incremental)
	{
		incremental.ComparisonCoordinates.AddRange(incremental.Chunks.Keys);
		incremental.ComparisonCoordinates.Sort(CompareCoordinates);
		incremental.TombstoneCoordinates.AddRange(incremental.RemovedTombstones.Keys);
		incremental.TombstoneCoordinates.Sort(CompareCoordinates);
		incremental.InvalidationTargets.Clear();
		incremental.Phase = IncrementalPhase.CompareWorking;
	}

	private bool TryPrepareNextIncrementalComparisonCell(IncrementalTransaction incremental)
	{
		while (incremental.ComparisonCoordinateIndex < incremental.ComparisonCoordinates.Count)
		{
			ChunkCoordinate coordinate =
				incremental.ComparisonCoordinates[incremental.ComparisonCoordinateIndex];
			IncrementalWorkingChunk working = incremental.Chunks[coordinate];
			if (residents.TryGetValue(coordinate, out ResidentChunk resident)
				&& ReferenceEquals(resident, working.Resident))
			{
				if (incremental.ComparisonCellIndex
					>= GetIncrementalComparisonWorkCount(working))
				{
					incremental.ComparisonCoordinateIndex++;
					incremental.ComparisonCellIndex = 0;
					continue;
				}

				incremental.ActiveComparisonWorking = working;
				return true;
			}

			incremental.ComparisonCoordinateIndex++;
			incremental.ComparisonCellIndex = 0;
		}

		return false;
	}

	private void CompareNextIncrementalCell(IncrementalTransaction incremental)
	{
		IncrementalWorkingChunk working = incremental.ActiveComparisonWorking;
		ResidentChunk resident = working.Resident;
		int comparisonIndex = incremental.ComparisonCellIndex;

		if (comparisonIndex == 0)
		{
			if (working.IsNew)
			{
				incremental.InvalidationTargets.Add(working.Coordinate);
			}

			if ((working.IsNew && working.SkyExposedAbove) || working.SkyExposureChanged)
			{
				AddSkyHaloTargets(working.Coordinate, incremental.InvalidationTargets);
			}
		}
		else
		{
			int index = working.IsNew
				? working.ModifiedBoundaryCells[comparisonIndex - 1]
				: working.ModifiedCells[comparisonIndex - 1];
			if (working.IsNew)
			{
				if (working.Lights[index] != 0)
				{
					AddHaloTargets(working.Coordinate, index, incremental.InvalidationTargets);
				}
			}
			else if (resident.PublishedLights[index] != working.Lights[index])
			{
				incremental.InvalidationTargets.Add(working.Coordinate);
				AddHaloTargets(working.Coordinate, index, incremental.InvalidationTargets);
			}
		}

		incremental.ComparisonCellIndex++;
		if (incremental.ComparisonCellIndex == GetIncrementalComparisonWorkCount(working))
		{
			incremental.ComparisonCoordinateIndex++;
			incremental.ComparisonCellIndex = 0;
		}
	}

	private static int GetIncrementalComparisonWorkCount(IncrementalWorkingChunk working)
	{
		return 1 + (
			working.IsNew
				? working.ModifiedBoundaryCells.Count
				: working.ModifiedCells.Count
		);
	}

	private bool TryPrepareNextIncrementalTombstoneCell(IncrementalTransaction incremental)
	{
		while (incremental.TombstoneCoordinateIndex < incremental.TombstoneCoordinates.Count)
		{
			ChunkCoordinate coordinate =
				incremental.TombstoneCoordinates[incremental.TombstoneCoordinateIndex];
			RemovedChunkTombstone captured = incremental.RemovedTombstones[coordinate];
			if (removedTombstones.TryGetValue(coordinate, out RemovedChunkTombstone current)
				&& ReferenceEquals(current, captured))
			{
				incremental.ActiveComparisonTombstone = captured;
				return true;
			}

			incremental.TombstoneCoordinateIndex++;
			incremental.TombstoneCellIndex = 0;
		}

		return false;
	}

	private static void CompareNextIncrementalTombstoneCell(
		IncrementalTransaction incremental
	)
	{
		ChunkCoordinate coordinate =
			incremental.TombstoneCoordinates[incremental.TombstoneCoordinateIndex];
		RemovedChunkTombstone tombstone = incremental.ActiveComparisonTombstone;
		int index = incremental.TombstoneCellIndex;
		if (index == 0 && tombstone.PublishedSkyExposedAbove)
		{
			AddSkyHaloTargets(coordinate, incremental.InvalidationTargets);
		}

		if (tombstone.PublishedLights[index] != 0)
		{
			AddHaloTargets(coordinate, index, incremental.InvalidationTargets);
		}

		AdvanceCellCursor(
			ref incremental.TombstoneCoordinateIndex,
			ref incremental.TombstoneCellIndex
		);
	}

	private static HorizontalChunkCoordinate HorizontalFromWorldColumn(WorldColumn column)
	{
		ChunkCoordinate coordinate = ChunkCoordinate.FromWorld(
			column.X,
			0,
			column.Z,
			out _,
			out _,
			out _
		);
		return new HorizontalChunkCoordinate(coordinate.X, coordinate.Z);
	}

	private static int CompareWorldColumns(WorldColumn left, WorldColumn right)
	{
		int comparison = left.X.CompareTo(right.X);
		return comparison != 0 ? comparison : left.Z.CompareTo(right.Z);
	}

	private static int CompareHorizontalCoordinates(
		HorizontalChunkCoordinate left,
		HorizontalChunkCoordinate right
	)
	{
		int comparison = left.X.CompareTo(right.X);
		return comparison != 0 ? comparison : left.Z.CompareTo(right.Z);
	}

}
