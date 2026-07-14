using System;
using System.Collections.Generic;

namespace FishGfx.Voxels;

public sealed partial class VoxelLighting
{
	private sealed class ResidentChunk
	{
		internal ResidentChunk(
			ChunkCoordinate coordinate,
			bool skyExposedAbove,
			long generation
		)
		{
			Coordinate = coordinate;
			SkyExposedAbove = skyExposedAbove;
			Generation = generation;
		}

		internal ChunkCoordinate Coordinate { get; }
		internal long Generation { get; }
		internal bool SkyExposedAbove { get; set; }
		internal bool PublishedSkyExposedAbove { get; set; }
		internal bool HasPublishedSkyExposure { get; set; }
		internal ushort[] MaterialSignatures { get; set; }
		internal ushort[] PublishedLights { get; set; }
		internal byte[] PublishedDirectSky { get; set; }
		internal long Revision { get; set; }
	}

	private sealed class RemovedChunkTombstone
	{
		internal RemovedChunkTombstone(ResidentChunk resident)
		{
			PublishedLights = resident.PublishedLights;
			PublishedSkyExposedAbove = resident.PublishedSkyExposedAbove;
		}

		internal ushort[] PublishedLights { get; }
		internal bool PublishedSkyExposedAbove { get; }
	}

	private sealed class WorkingChunk
	{
		internal WorkingChunk(
			ChunkCoordinate coordinate,
			bool skyExposedAbove
		)
		{
			Coordinate = coordinate;
			SkyExposedAbove = skyExposedAbove;
		}

		internal void CaptureMaterialContent(
			VoxelChunkContent content,
			int initializationWork
		)
		{
			MaterialCells = content.Cells;
			MaterialRuns = content.MaterialRuns;
			InitializationWork = initializationWork;
		}

		internal void InitializeStorage(ushort[] materialSignatures)
		{
			MaterialSignatures = materialSignatures;
			Lights = new ushort[VoxelWorld.ChunkVolume];
			DirectSky = new byte[VoxelWorld.ChunkVolume];
			QueuedWords = new ulong[(VoxelWorld.ChunkVolume + 63) / 64];
			StorageInitialized = true;
		}

		internal ChunkCoordinate Coordinate { get; }
		internal bool SkyExposedAbove { get; }
		internal ReadOnlyMemory<VoxelCell> MaterialCells { get; private set; }
		internal ReadOnlyMemory<VoxelMaterialRun> MaterialRuns { get; private set; }
		internal ushort[] MaterialSignatures { get; private set; }
		internal ushort[] Lights { get; private set; }
		internal byte[] DirectSky { get; private set; }
		internal ulong[] QueuedWords { get; private set; }
		internal int InitializationWork { get; private set; }
		internal bool StorageInitialized { get; private set; }
		internal ResidentChunk Resident { get; set; }
		internal bool Changed { get; set; }
	}

	private sealed class IncrementalWorkingChunk
	{
		internal IncrementalWorkingChunk(
			ResidentChunk resident,
			bool skyExposedAbove,
			ushort[] materialSignatures,
			ushort[] lights,
			byte[] directSky,
			bool isNew
		)
		{
			Resident = resident;
			SkyExposedAbove = skyExposedAbove;
			MaterialSignatures = materialSignatures;
			Lights = lights;
			DirectSky = directSky;
			IsNew = isNew;
		}

		internal ResidentChunk Resident { get; }
		internal ChunkCoordinate Coordinate => Resident.Coordinate;
		internal bool SkyExposedAbove { get; set; }
		internal bool SkyExposureChanged { get; set; }
		internal ushort[] MaterialSignatures { get; }
		internal ushort[] Lights { get; }
		internal byte[] DirectSky { get; }
		internal bool IsNew { get; }
		internal List<int> ModifiedCells { get; } = new List<int>();
		internal List<int> ModifiedBoundaryCells { get; } = new List<int>();
		private ulong[] modifiedWords;
		private ulong[] modifiedBoundaryWords;

		internal void MarkModified(int index, bool isBoundary)
		{
			if (IsNew)
			{
				if (!isBoundary)
				{
					return;
				}

				modifiedBoundaryWords ??= new ulong[(VoxelWorld.ChunkVolume + 63) / 64];
				if (IsCellMarked(modifiedBoundaryWords, index))
				{
					return;
				}

				MarkCell(modifiedBoundaryWords, index);
				ModifiedBoundaryCells.Add(index);
				return;
			}

			modifiedWords ??= new ulong[(VoxelWorld.ChunkVolume + 63) / 64];
			if (IsCellMarked(modifiedWords, index))
			{
				return;
			}

			MarkCell(modifiedWords, index);
			ModifiedCells.Add(index);
		}
	}

	private sealed class IncrementalTransaction
	{
		internal IncrementalTransaction(
			Dictionary<ChunkCoordinate, IncrementalSourceChunk> sources
		)
		{
			Sources = sources;
		}

		internal Dictionary<ChunkCoordinate, IncrementalSourceChunk> Sources { get; }
		internal List<ChunkCoordinate> AddedCoordinates { get; } =
			new List<ChunkCoordinate>();
		internal Dictionary<ChunkCoordinate, VoxelChunkContent> AddedMaterialContents { get; } =
			new Dictionary<ChunkCoordinate, VoxelChunkContent>();
		internal Dictionary<ChunkCoordinate, int> AddedPreparationWork { get; } =
			new Dictionary<ChunkCoordinate, int>();
		internal List<SkyExposureChange> SkyChanges { get; } =
			new List<SkyExposureChange>();
		internal List<ChunkCoordinate> DirtyCoordinates { get; } =
			new List<ChunkCoordinate>();
		internal Dictionary<ChunkCoordinate, ReadOnlyMemory<VoxelCell>> DirtyMaterialCells { get; } =
			new Dictionary<ChunkCoordinate, ReadOnlyMemory<VoxelCell>>();
		internal Dictionary<ChunkCoordinate, HashSet<int>> ExactDirtyCellSets { get; } =
			new Dictionary<ChunkCoordinate, HashSet<int>>();
		internal Dictionary<ChunkCoordinate, List<int>> ExactDirtyCells { get; } =
			new Dictionary<ChunkCoordinate, List<int>>();
		internal List<ChunkCoordinate> RemovedCoordinateList { get; } =
			new List<ChunkCoordinate>();
		internal Dictionary<ChunkCoordinate, IncrementalWorkingChunk> Chunks { get; } =
			new Dictionary<ChunkCoordinate, IncrementalWorkingChunk>();
		internal Queue<IncrementalCellAddress> Relaxation { get; } =
			new Queue<IncrementalCellAddress>();
		internal HashSet<WorldColumn> DirectColumnSet { get; } =
			new HashSet<WorldColumn>();
		internal HashSet<HorizontalChunkCoordinate> AddedDirectGroups { get; } =
			new HashSet<HorizontalChunkCoordinate>();
		internal List<WorldColumn> DirectColumns { get; } = new List<WorldColumn>();
		internal Dictionary<HorizontalChunkCoordinate, List<IncrementalSourceChunk>> DirectGroups { get; } =
			new Dictionary<HorizontalChunkCoordinate, List<IncrementalSourceChunk>>();
		internal List<ChunkCoordinate> ComparisonCoordinates { get; } =
			new List<ChunkCoordinate>();
		internal List<ChunkCoordinate> TombstoneCoordinates { get; } =
			new List<ChunkCoordinate>();
		internal HashSet<ChunkCoordinate> InvalidationTargets { get; } =
			new HashSet<ChunkCoordinate>();
		internal Dictionary<ChunkCoordinate, RemovedChunkTombstone> RemovedTombstones { get; } =
			new Dictionary<ChunkCoordinate, RemovedChunkTombstone>();
		internal HashSet<ChunkCoordinate> RemovedCoordinates { get; } =
			new HashSet<ChunkCoordinate>();
		internal HashSet<ChunkCoordinate> InvalidatedSourceCoordinates { get; } =
			new HashSet<ChunkCoordinate>();
		internal HashSet<ChunkCoordinate> ReferencedSourceCoordinates { get; } =
			new HashSet<ChunkCoordinate>();
		internal IncrementalPhase Phase { get; set; }
		internal int AddedCoordinateIndex;
		internal int AddedCellIndex;
		internal int AddedRunIndex;
		internal int AddedRunCellIndex;
		internal int AddedCoordinateConsumedWork;
		internal long RemainingAddedPreparationWork;
		internal int SkyChangeIndex { get; set; }
		internal int SkyCellIndex { get; set; }
		internal int DirtyCoordinateIndex;
		internal int DirtyCellIndex;
		internal int RemovedColumnCoordinateIndex { get; set; }
		internal int RemovedColumnIndex { get; set; }
		internal int RemovedBoundaryCoordinateIndex { get; set; }
		internal int RemovedBoundaryIndex { get; set; }
		internal int DirectColumnIndex { get; set; }
		internal List<IncrementalSourceChunk> ActiveDirectGroup { get; set; }
		internal IncrementalSourceChunk ActiveDirectSource { get; set; }
		internal int DirectChunkIndex { get; set; }
		internal int DirectY { get; set; }
		internal int DirectLocalX;
		internal int DirectLocalZ;
		internal byte DirectIncoming { get; set; }
		internal bool DirectChunkStarted { get; set; }
		internal long DirectRemaining { get; set; }
		internal int ComparisonCoordinateIndex;
		internal int ComparisonCellIndex;
		internal IncrementalWorkingChunk ActiveComparisonWorking { get; set; }
		internal int TombstoneCoordinateIndex;
		internal int TombstoneCellIndex;
		internal RemovedChunkTombstone ActiveComparisonTombstone { get; set; }
		internal bool DiscardAtCommit { get; set; }

		internal long PendingCount
		{
			get
			{
				long remaining;
				switch (Phase)
				{
					case IncrementalPhase.PrepareAdded:
						remaining = RemainingAddedPreparationWork;
						break;
					case IncrementalPhase.PrepareSky:
						remaining = RemainingCells(
							SkyChanges.Count,
							SkyChangeIndex,
							SkyCellIndex,
							VoxelWorld.ChunkSize * VoxelWorld.ChunkSize
						);
						break;
					case IncrementalPhase.PrepareDirty:
						remaining = RemainingDirtyCells();
						break;
					case IncrementalPhase.PrepareRemovedColumns:
						remaining = RemainingCells(
							RemovedCoordinateList.Count,
							RemovedColumnCoordinateIndex,
							RemovedColumnIndex,
							VoxelWorld.ChunkSize * VoxelWorld.ChunkSize
						);
						break;
					case IncrementalPhase.PrepareRemovedBoundaries:
						remaining = RemainingCells(
							RemovedCoordinateList.Count,
							RemovedBoundaryCoordinateIndex,
							RemovedBoundaryIndex,
							6 * VoxelWorld.ChunkSize * VoxelWorld.ChunkSize
						);
						break;
					case IncrementalPhase.DirectSky:
						remaining = DirectRemaining;
						break;
					case IncrementalPhase.Relax:
						remaining = Relaxation.Count;
						break;
					case IncrementalPhase.CompareWorking:
						remaining = RemainingComparisons();
						break;
					case IncrementalPhase.CompareTombstones:
						remaining = RemainingCells(
							TombstoneCoordinates.Count,
							TombstoneCoordinateIndex,
							TombstoneCellIndex,
							VoxelWorld.ChunkVolume
						);
						break;
					default:
						remaining = 0;
						break;
				}

				return Math.Max(
					1,
					remaining + (Phase == IncrementalPhase.Relax ? 0 : Relaxation.Count)
				);
			}
		}

		private static long RemainingCells(
			int itemCount,
			int itemIndex,
			int cellIndex,
			int cellsPerItem
		)
		{
			return Math.Max(0, (long)(itemCount - itemIndex) * cellsPerItem - cellIndex);
		}

		private long RemainingDirtyCells()
		{
			long remaining = 0;
			for (int index = DirtyCoordinateIndex; index < DirtyCoordinates.Count; index++)
			{
				ChunkCoordinate coordinate = DirtyCoordinates[index];
				int count = ExactDirtyCellSets.TryGetValue(coordinate, out HashSet<int> cells)
					? cells.Count
					: VoxelWorld.ChunkVolume;
				remaining += count;
			}

			return Math.Max(0, remaining - DirtyCellIndex);
		}

		private long RemainingComparisons()
		{
			long remaining = 0;
			for (int index = ComparisonCoordinateIndex; index < ComparisonCoordinates.Count; index++)
			{
				if (Chunks.TryGetValue(
					ComparisonCoordinates[index],
					out IncrementalWorkingChunk working
				))
				{
					remaining += GetIncrementalComparisonWorkCount(working);
				}
			}

			return Math.Max(0, remaining - ComparisonCellIndex);
		}
	}

	private sealed class IncrementalSourceChunk
	{
		internal IncrementalSourceChunk(ResidentChunk resident)
		{
			Resident = resident;
			DesiredSkyExposedAbove = resident.SkyExposedAbove;
			PublishedSkyExposedAbove = resident.PublishedSkyExposedAbove;
			MaterialSignatures = resident.MaterialSignatures;
			PublishedLights = resident.PublishedLights;
			PublishedDirectSky = resident.PublishedDirectSky;
		}

		internal ResidentChunk Resident { get; }
		internal bool DesiredSkyExposedAbove { get; }
		internal bool PublishedSkyExposedAbove { get; }
		internal ushort[] MaterialSignatures { get; }
		internal ushort[] PublishedLights { get; }
		internal byte[] PublishedDirectSky { get; }
		internal IncrementalWorkingChunk Working { get; set; }
		internal bool IsReferenced { get; set; }
		private ulong[] queuedWords;

		internal ushort[] CurrentMaterialSignatures =>
			Working?.MaterialSignatures ?? MaterialSignatures;
		internal ushort[] CurrentLights => Working?.Lights ?? PublishedLights;
		internal byte[] CurrentDirectSky => Working?.DirectSky ?? PublishedDirectSky;

		internal bool TryMarkQueued(int index)
		{
			queuedWords ??= new ulong[(VoxelWorld.ChunkVolume + 63) / 64];
			if (IsCellMarked(queuedWords, index))
			{
				return false;
			}

			MarkCell(queuedWords, index);
			return true;
		}

		internal void ClearQueued(int index)
		{
			ClearCell(queuedWords, index);
		}
	}

	private sealed class RebuildTransaction
	{
		internal RebuildTransaction(
			List<WorkingChunk> chunks,
			Dictionary<ChunkCoordinate, WorkingChunk> lookup,
			Dictionary<ChunkCoordinate, RemovedChunkTombstone> removedTombstones
		)
		{
			Chunks = chunks;
			Lookup = lookup;
			RemovedTombstones = removedTombstones;
			foreach (WorkingChunk chunk in chunks)
			{
				RemainingInitializationWork += chunk.InitializationWork;
			}
		}

		internal List<WorkingChunk> Chunks { get; }
		internal Dictionary<ChunkCoordinate, WorkingChunk> Lookup { get; }
		internal Dictionary<ChunkCoordinate, RemovedChunkTombstone> RemovedTombstones { get; }
		internal Queue<CellAddress> Propagation { get; } = new Queue<CellAddress>();
		internal HashSet<ChunkCoordinate> InvalidationTargets { get; } =
			new HashSet<ChunkCoordinate>();
		internal Dictionary<HorizontalChunkCoordinate, List<WorkingChunk>> DirectGroups { get; } =
			new Dictionary<HorizontalChunkCoordinate, List<WorkingChunk>>();
		internal List<HorizontalChunkCoordinate> DirectGroupCoordinates { get; } =
			new List<HorizontalChunkCoordinate>();
		internal List<ChunkCoordinate> TombstoneCoordinates { get; } =
			new List<ChunkCoordinate>();
		internal RebuildPhase Phase { get; set; }
		internal int InitializeChunkIndex;
		internal int InitializeCellIndex;
		internal int InitializeRunIndex;
		internal int InitializeRunCellIndex;
		internal long RemainingInitializationWork;
		internal bool DirectInitialized { get; set; }
		internal int DirectGroupIndex { get; set; }
		internal int DirectLocalColumnIndex { get; set; }
		internal int DirectChunkIndex { get; set; }
		internal int DirectY { get; set; }
		internal byte DirectIncoming { get; set; }
		internal bool DirectChunkStarted { get; set; }
		internal List<WorkingChunk> ActiveDirectGroup { get; set; }
		internal WorkingChunk ActiveDirectWorking { get; set; }
		internal long DirectRemaining { get; set; }
		internal int CompareChunkIndex;
		internal int CompareCellIndex;
		internal int TombstoneCoordinateIndex;
		internal int TombstoneCellIndex;
		internal RemovedChunkTombstone ActiveComparisonTombstone { get; set; }

		internal long PendingCount
		{
			get
			{
				long count;
				switch (Phase)
				{
					case RebuildPhase.Initialize:
						count = RemainingInitializationWork;
						break;
					case RebuildPhase.SeedDirectSky:
						count = DirectInitialized
							? DirectRemaining
							: (long)Chunks.Count * VoxelWorld.ChunkVolume;
						break;
					case RebuildPhase.Propagate:
						count = Propagation.Count;
						break;
					case RebuildPhase.Compare:
						count = (long)(Chunks.Count - CompareChunkIndex)
							* VoxelWorld.ChunkVolume - CompareCellIndex;
						break;
					case RebuildPhase.CompareTombstones:
						count = (long)(TombstoneCoordinates.Count - TombstoneCoordinateIndex)
							* VoxelWorld.ChunkVolume - TombstoneCellIndex;
						break;
					default:
						count = 0;
						break;
				}

				return Math.Max(
					1,
					count + (Phase == RebuildPhase.Propagate ? 0 : Propagation.Count)
				);
			}
		}
	}

	private readonly struct CellAddress
	{
		internal CellAddress(ChunkCoordinate coordinate, int index)
		{
			Coordinate = coordinate;
			Index = index;
		}

		internal ChunkCoordinate Coordinate { get; }
		internal int Index { get; }
	}

	private readonly struct IncrementalCellAddress
	{
		internal IncrementalCellAddress(IncrementalSourceChunk source, int index)
		{
			Source = source;
			Index = index;
		}

		internal IncrementalSourceChunk Source { get; }
		internal int Index { get; }
	}

	private readonly struct WorldColumn : IEquatable<WorldColumn>
	{
		internal WorldColumn(int x, int z)
		{
			X = x;
			Z = z;
		}

		internal int X { get; }
		internal int Z { get; }
		public bool Equals(WorldColumn other) => X == other.X && Z == other.Z;
		public override bool Equals(object obj) => obj is WorldColumn other && Equals(other);
		public override int GetHashCode() => HashCode.Combine(X, Z);
	}

	private readonly struct SkyExposureChange
	{
		internal SkyExposureChange(ChunkCoordinate coordinate, bool skyExposedAbove)
		{
			Coordinate = coordinate;
			SkyExposedAbove = skyExposedAbove;
		}

		internal ChunkCoordinate Coordinate { get; }
		internal bool SkyExposedAbove { get; }
	}

	private readonly struct HorizontalChunkCoordinate : IEquatable<HorizontalChunkCoordinate>
	{
		internal HorizontalChunkCoordinate(int x, int z)
		{
			X = x;
			Z = z;
		}

		internal int X { get; }
		internal int Z { get; }
		public bool Equals(HorizontalChunkCoordinate other) => X == other.X && Z == other.Z;
		public override bool Equals(object obj) =>
			obj is HorizontalChunkCoordinate other && Equals(other);
		public override int GetHashCode() => HashCode.Combine(X, Z);
	}

	private enum RebuildPhase
	{
		Initialize,
		SeedDirectSky,
		Propagate,
		Compare,
		CompareTombstones,
	}

	private enum IncrementalPhase
	{
		PrepareAdded,
		PrepareSky,
		PrepareDirty,
		PrepareRemovedColumns,
		PrepareRemovedBoundaries,
		DirectSky,
		Relax,
		CompareWorking,
		CompareTombstones,
	}

	private enum IncrementalStepResult
	{
		NeedsBudget,
		Consumed,
		Completed,
	}
}
