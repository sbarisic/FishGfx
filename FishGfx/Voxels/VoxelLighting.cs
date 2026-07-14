using System;
using System.Collections.Generic;

namespace FishGfx.Voxels
{
	public readonly struct VoxelBlockLight : IEquatable<VoxelBlockLight>
	{
		public VoxelBlockLight(byte red, byte green, byte blue)
		{
			if (red > 15)
				throw new ArgumentOutOfRangeException(nameof(red));
			if (green > 15)
				throw new ArgumentOutOfRangeException(nameof(green));
			if (blue > 15)
				throw new ArgumentOutOfRangeException(nameof(blue));

			Red = red;
			Green = green;
			Blue = blue;
		}

		public byte Red { get; }
		public byte Green { get; }
		public byte Blue { get; }
		public bool IsDark => Red == 0 && Green == 0 && Blue == 0;

		public bool Equals(VoxelBlockLight other)
		{
			return Red == other.Red && Green == other.Green && Blue == other.Blue;
		}

		public override bool Equals(object obj) => obj is VoxelBlockLight other && Equals(other);
		public override int GetHashCode() => HashCode.Combine(Red, Green, Blue);
		public static bool operator ==(VoxelBlockLight left, VoxelBlockLight right) => left.Equals(right);
		public static bool operator !=(VoxelBlockLight left, VoxelBlockLight right) => !left.Equals(right);
	}

	public readonly struct VoxelLight : IEquatable<VoxelLight>
	{
		public VoxelLight(VoxelBlockLight block, byte sky)
		{
			if (sky > 15)
				throw new ArgumentOutOfRangeException(nameof(sky));

			Packed = Pack(block.Red, block.Green, block.Blue, sky);
		}

		internal VoxelLight(ushort packed)
		{
			Packed = packed;
		}

		public VoxelBlockLight Block => new VoxelBlockLight(
			(byte)(Packed & 0xf),
			(byte)((Packed >> 4) & 0xf),
			(byte)((Packed >> 8) & 0xf)
		);

		public byte Sky => (byte)((Packed >> 12) & 0xf);
		internal ushort Packed { get; }

		public bool Equals(VoxelLight other) => Packed == other.Packed;
		public override bool Equals(object obj) => obj is VoxelLight other && Equals(other);
		public override int GetHashCode() => Packed;
		public static bool operator ==(VoxelLight left, VoxelLight right) => left.Equals(right);
		public static bool operator !=(VoxelLight left, VoxelLight right) => !left.Equals(right);

		internal static ushort Pack(byte red, byte green, byte blue, byte sky)
		{
			return (ushort)(red | (green << 4) | (blue << 8) | (sky << 12));
		}
	}

	public readonly struct VoxelMaterialLightSettings : IEquatable<VoxelMaterialLightSettings>
	{
		public VoxelMaterialLightSettings(byte opacity, VoxelBlockLight emission = default)
		{
			if (opacity > 15)
				throw new ArgumentOutOfRangeException(nameof(opacity));

			Opacity = opacity;
			Emission = emission;
		}

		public byte Opacity { get; }
		public VoxelBlockLight Emission { get; }

		public bool Equals(VoxelMaterialLightSettings other)
		{
			return Opacity == other.Opacity && Emission == other.Emission;
		}

		public override bool Equals(object obj) =>
			obj is VoxelMaterialLightSettings other && Equals(other);
		public override int GetHashCode() => HashCode.Combine(Opacity, Emission);
		public static bool operator ==(
			VoxelMaterialLightSettings left,
			VoxelMaterialLightSettings right
		) => left.Equals(right);
		public static bool operator !=(
			VoxelMaterialLightSettings left,
			VoxelMaterialLightSettings right
		) => !left.Equals(right);
	}

	public sealed class VoxelLightingOptions
	{
		private int updateBudget = 65_536;

		public int UpdateBudget
		{
			get => updateBudget;
			set
			{
				if (value <= 0)
					throw new ArgumentOutOfRangeException(nameof(value));
				updateBudget = value;
			}
		}
	}

	internal sealed class VoxelLightChunkSnapshot
	{
		internal const int PaddedSize = VoxelWorld.ChunkSize + 2;
		private readonly ushort[] paddedLights;

		internal VoxelLightChunkSnapshot(
			ChunkCoordinate coordinate,
			long generation,
			long revision,
			ushort[] paddedLights
		)
		{
			Coordinate = coordinate;
			Generation = generation;
			Revision = revision;
			this.paddedLights = paddedLights;
		}

		internal VoxelLightChunkSnapshot(
			ChunkCoordinate coordinate,
			long revision,
			ushort[] paddedLights
		)
			: this(coordinate, 0, revision, paddedLights)
		{
		}

		internal ChunkCoordinate Coordinate { get; }
		internal long Generation { get; }
		internal long Revision { get; }

		internal VoxelLight GetLight(int localX, int localY, int localZ)
		{
			if (localX < -1 || localX > VoxelWorld.ChunkSize)
				throw new ArgumentOutOfRangeException(nameof(localX));
			if (localY < -1 || localY > VoxelWorld.ChunkSize)
				throw new ArgumentOutOfRangeException(nameof(localY));
			if (localZ < -1 || localZ > VoxelWorld.ChunkSize)
				throw new ArgumentOutOfRangeException(nameof(localZ));

			return GetLightUnchecked(localX, localY, localZ);
		}

		internal VoxelLight GetLightUnchecked(int localX, int localY, int localZ)
		{
			return new VoxelLight(paddedLights[
				(localX + 1) + PaddedSize * ((localY + 1) + PaddedSize * (localZ + 1))
			]);
		}
	}

	public sealed class VoxelLighting : IDisposable
	{
		private static readonly Neighbor[] Neighbors =
		{
			new Neighbor(1, 0, 0),
			new Neighbor(-1, 0, 0),
			new Neighbor(0, 1, 0),
			new Neighbor(0, -1, 0),
			new Neighbor(0, 0, 1),
			new Neighbor(0, 0, -1),
		};

		private readonly object sync = new object();
		private readonly VoxelWorld world;
		private readonly VoxelPalette palette;
		private readonly int updateBudget;
		private readonly Dictionary<ChunkCoordinate, ResidentChunk> residents =
			new Dictionary<ChunkCoordinate, ResidentChunk>();
		private readonly HashSet<ChunkCoordinate> dirtyWorldChunks =
			new HashSet<ChunkCoordinate>();
		private readonly Dictionary<ChunkCoordinate, HashSet<int>> dirtyWorldCells =
			new Dictionary<ChunkCoordinate, HashSet<int>>();
		private readonly HashSet<ChunkCoordinate> addedChunks =
			new HashSet<ChunkCoordinate>();
		private readonly HashSet<ChunkCoordinate> removedChunks =
			new HashSet<ChunkCoordinate>();
		private readonly HashSet<ChunkCoordinate> skyChangedChunks =
			new HashSet<ChunkCoordinate>();
		private readonly Dictionary<ChunkCoordinate, RemovedChunkTombstone> removedTombstones =
			new Dictionary<ChunkCoordinate, RemovedChunkTombstone>();
		private RebuildTransaction transaction;
		private IncrementalTransaction incrementalTransaction;
		private bool fullRebuildRequested;
		private bool disposed;
		private long nextChunkGeneration;

		public VoxelLighting(
			VoxelWorld world,
			VoxelPalette palette,
			VoxelLightingOptions options = null
		)
		{
			this.world = world ?? throw new ArgumentNullException(nameof(world));
			this.palette = palette ?? throw new ArgumentNullException(nameof(palette));
			updateBudget = (options ?? new VoxelLightingOptions()).UpdateBudget;
			world.ContentChanged += HandleWorldContentChanged;
		}

		internal event Action<ChunkCoordinate, long> ChunkInvalidated;

		public bool IsIdle
		{
			get
			{
				lock (sync)
				{
					ThrowIfDisposed();
					return transaction == null
						&& incrementalTransaction == null
						&& !fullRebuildRequested
						&& !HasPendingIncrementalChanges();
				}
			}
		}

		public int PendingCount
		{
			get
			{
				lock (sync)
				{
					ThrowIfDisposed();
					long count = dirtyWorldChunks.Count
						+ GetPendingExactCellCount()
						+ addedChunks.Count
						+ removedChunks.Count
						+ skyChangedChunks.Count
						+ (fullRebuildRequested ? 1 : 0);
					if (transaction != null)
						count += transaction.PendingCount;
					if (incrementalTransaction != null)
						count += incrementalTransaction.PendingCount;
					return count > int.MaxValue ? int.MaxValue : (int)count;
				}
			}
		}

		public int ResidentChunkCount
		{
			get
			{
				lock (sync)
				{
					ThrowIfDisposed();
					return residents.Count;
				}
			}
		}

		public void LoadChunk(ChunkCoordinate coordinate, bool skyExposedAbove = false)
		{
			lock (sync)
			{
				ThrowIfDisposed();

				if (residents.TryGetValue(coordinate, out ResidentChunk existing))
				{
					if (existing.SkyExposedAbove == skyExposedAbove)
						return;

					existing.SkyExposedAbove = skyExposedAbove;
					skyChangedChunks.Add(coordinate);
				}
				else
				{
					ResidentChunk resident = new ResidentChunk(
						coordinate,
						skyExposedAbove,
						++nextChunkGeneration
					);
					residents.Add(coordinate, resident);
					addedChunks.Add(coordinate);
				}
			}
		}

		public bool UnloadChunk(ChunkCoordinate coordinate)
		{
			lock (sync)
			{
				ThrowIfDisposed();

				if (!residents.TryGetValue(coordinate, out ResidentChunk removedChunk))
					return false;

				if (removedChunk.PublishedLights != null)
					removedTombstones[coordinate] = new RemovedChunkTombstone(removedChunk);

				bool wasPublished = removedChunk.PublishedLights != null;
				bool wasInFlight = transaction?.Lookup.ContainsKey(coordinate) == true
					|| incrementalTransaction?.Chunks.ContainsKey(coordinate) == true;
				bool restartFullRebuild = transaction != null;
				residents.Remove(coordinate);
				dirtyWorldChunks.Remove(coordinate);
				dirtyWorldCells.Remove(coordinate);
				skyChangedChunks.Remove(coordinate);
				bool wasPendingAddition = addedChunks.Remove(coordinate);
				if (wasPublished || wasInFlight || !wasPendingAddition)
					removedChunks.Add(coordinate);

				if (restartFullRebuild)
				{
					transaction = null;
					fullRebuildRequested = residents.Count != 0;
				}
				else if (incrementalTransaction != null)
				{
					incrementalTransaction.InvalidatedSourceCoordinates.Add(coordinate);
					if (incrementalTransaction.Chunks.ContainsKey(coordinate)
						|| incrementalTransaction.ReferencedSourceCoordinates.Contains(coordinate))
						incrementalTransaction.DiscardAtCommit = true;
				}

				return true;
			}
		}

		public void SetSkyExposedAbove(ChunkCoordinate coordinate, bool skyExposedAbove)
		{
			lock (sync)
			{
				ThrowIfDisposed();

				if (!residents.TryGetValue(coordinate, out ResidentChunk chunk))
					throw new InvalidOperationException("Only resident chunks can be marked as sky-exposed.");
				if (chunk.SkyExposedAbove == skyExposedAbove)
					return;

				chunk.SkyExposedAbove = skyExposedAbove;
				skyChangedChunks.Add(coordinate);
			}
		}

		public void RequestFullRebuild()
		{
			lock (sync)
			{
				ThrowIfDisposed();
				fullRebuildRequested = residents.Count != 0;
			}
		}

		public int Update(int? budget = null)
		{
			int actualBudget = budget ?? updateBudget;
			if (actualBudget <= 0)
				throw new ArgumentOutOfRangeException(nameof(budget));

			List<(ChunkCoordinate Coordinate, long Revision)> invalidated = null;
			int processed;

			lock (sync)
			{
				ThrowIfDisposed();
				processed = UpdateUnchecked(actualBudget, ref invalidated);
			}

			if (invalidated != null)
				foreach ((ChunkCoordinate coordinate, long revision) in invalidated)
					ChunkInvalidated?.Invoke(coordinate, revision);

			return processed;
		}

		public VoxelLight GetLight(int worldX, int worldY, int worldZ)
		{
			ChunkCoordinate coordinate = ChunkCoordinate.FromWorld(
				worldX,
				worldY,
				worldZ,
				out int localX,
				out int localY,
				out int localZ
			);

			lock (sync)
			{
				ThrowIfDisposed();
				return residents.TryGetValue(coordinate, out ResidentChunk chunk)
					&& chunk.PublishedLights != null
					? new VoxelLight(chunk.PublishedLights[Index(localX, localY, localZ)])
					: default;
			}
		}

		internal bool TryCreateSnapshot(
			ChunkCoordinate coordinate,
			out VoxelLightChunkSnapshot snapshot
		)
		{
			lock (sync)
			{
				ThrowIfDisposed();

				if (!residents.TryGetValue(coordinate, out ResidentChunk center)
					|| center.PublishedLights == null)
				{
					snapshot = null;
					return false;
				}

				ushort[] padded = new ushort[
					VoxelLightChunkSnapshot.PaddedSize
					* VoxelLightChunkSnapshot.PaddedSize
					* VoxelLightChunkSnapshot.PaddedSize
				];

				for (int z = -1; z <= VoxelWorld.ChunkSize; z++)
					for (int y = -1; y <= VoxelWorld.ChunkSize; y++)
						for (int x = -1; x <= VoxelWorld.ChunkSize; x++)
						{
							ResolveLocal(
								coordinate,
								x,
								y,
								z,
								out ChunkCoordinate sampleCoordinate,
								out int sampleX,
								out int sampleY,
								out int sampleZ
							);

							if (TryGetPublishedHaloLight(
								sampleCoordinate,
								Index(sampleX, sampleY, sampleZ),
								out ushort sampleLight
							))
								padded[(x + 1) + VoxelLightChunkSnapshot.PaddedSize
									* ((y + 1) + VoxelLightChunkSnapshot.PaddedSize * (z + 1))] =
									sampleLight;
							else if (y == VoxelWorld.ChunkSize)
							{
								ResolveLocal(
									coordinate,
									x,
									VoxelWorld.ChunkSize - 1,
									z,
									out ChunkCoordinate belowCoordinate,
									out _,
									out _,
									out _
								);
								if (IsPublishedSkyExposed(belowCoordinate))
									padded[(x + 1) + VoxelLightChunkSnapshot.PaddedSize
										* ((y + 1) + VoxelLightChunkSnapshot.PaddedSize * (z + 1))] =
										VoxelLight.Pack(0, 0, 0, 15);
							}
						}

				snapshot = new VoxelLightChunkSnapshot(
					coordinate,
					center.Generation,
					center.Revision,
					padded
				);
				return true;
			}
		}

		internal bool TryGetChunkRevision(ChunkCoordinate coordinate, out long revision)
		{
			lock (sync)
			{
				ThrowIfDisposed();

				if (residents.TryGetValue(coordinate, out ResidentChunk chunk)
					&& chunk.PublishedLights != null)
				{
					revision = chunk.Revision;
					return true;
				}

				revision = 0;
				return false;
			}
		}

		internal bool TryGetChunkState(
			ChunkCoordinate coordinate,
			out long generation,
			out long revision
		)
		{
			lock (sync)
			{
				ThrowIfDisposed();
				if (residents.TryGetValue(coordinate, out ResidentChunk chunk)
					&& chunk.PublishedLights != null)
				{
					generation = chunk.Generation;
					revision = chunk.Revision;
					return true;
				}

				generation = 0;
				revision = 0;
				return false;
			}
		}

		private bool TryGetPublishedHaloLight(
			ChunkCoordinate coordinate,
			int index,
			out ushort light
		)
		{
			if (residents.TryGetValue(coordinate, out ResidentChunk resident)
				&& resident.PublishedLights != null)
			{
				light = resident.PublishedLights[index];
				return true;
			}
			if (removedTombstones.TryGetValue(coordinate, out RemovedChunkTombstone tombstone))
			{
				light = tombstone.PublishedLights[index];
				return true;
			}

			light = 0;
			return false;
		}

		private bool IsPublishedSkyExposed(ChunkCoordinate coordinate)
		{
			if (residents.TryGetValue(coordinate, out ResidentChunk resident)
				&& resident.PublishedLights != null)
				return resident.PublishedSkyExposedAbove;
			return removedTombstones.TryGetValue(coordinate, out RemovedChunkTombstone tombstone)
				&& tombstone.PublishedSkyExposedAbove;
		}

		internal bool IsCompatibleWith(VoxelWorld candidateWorld, VoxelPalette candidatePalette)
		{
			lock (sync)
			{
				ThrowIfDisposed();
				return ReferenceEquals(world, candidateWorld) && ReferenceEquals(palette, candidatePalette);
			}
		}

		public void Dispose()
		{
			lock (sync)
			{
				if (disposed)
					return;

				disposed = true;
				world.ContentChanged -= HandleWorldContentChanged;
				transaction = null;
				incrementalTransaction = null;
				residents.Clear();
				dirtyWorldChunks.Clear();
				dirtyWorldCells.Clear();
				addedChunks.Clear();
				removedChunks.Clear();
				skyChangedChunks.Clear();
				removedTombstones.Clear();
			}
		}

		private int UpdateUnchecked(
			int budget,
			ref List<(ChunkCoordinate Coordinate, long Revision)> invalidated
		)
		{
			int processed = 0;

			while (true)
			{
				if (transaction == null && incrementalTransaction == null)
				{
					if (fullRebuildRequested)
					{
						fullRebuildRequested = false;
						if (residents.Count != 0)
							transaction = CreateTransaction();
						ClearPendingIncrementalChanges();
					}
					else if (HasPendingIncrementalChanges())
					{
						incrementalTransaction = CreateIncrementalTransaction();
					}

					if (transaction == null && incrementalTransaction == null)
						break;
				}

				if (incrementalTransaction != null)
				{
					IncrementalStepResult step = ProcessIncrementalStep(
						incrementalTransaction,
						processed < budget,
						ref invalidated
					);
					if (step == IncrementalStepResult.NeedsBudget)
						break;
					if (step == IncrementalStepResult.Consumed)
						processed++;
					else
						incrementalTransaction = null;
					continue;
				}

				if (transaction.Phase == RebuildPhase.Initialize)
				{
					if (processed >= budget)
						break;

					InitializeNextCell(transaction);
					processed++;
					continue;
				}

				if (transaction.Phase == RebuildPhase.SeedDirectSky)
				{
					if (!TryPrepareNextFullDirectCell(transaction))
					{
						transaction.Phase = RebuildPhase.Propagate;
						continue;
					}
					if (processed >= budget)
						break;

					ProcessNextFullDirectCell(transaction);
					processed++;
					continue;
				}

				if (transaction.Phase == RebuildPhase.Propagate)
				{
					if (transaction.Propagation.Count == 0)
					{
						transaction.Phase = RebuildPhase.Compare;
						continue;
					}
					if (processed >= budget)
						break;

					PropagateNextCell(transaction);
					processed++;
					continue;
				}

				if (transaction.Phase == RebuildPhase.Compare)
				{
					if (transaction.CompareChunkIndex >= transaction.Chunks.Count)
					{
						InitializeFullTombstoneComparison(transaction);
						continue;
					}
					if (processed >= budget)
						break;

					CompareNextCell(transaction);
					processed++;
					continue;
				}

				if (transaction.Phase == RebuildPhase.CompareTombstones)
				{
					if (!TryPrepareNextFullTombstoneCell(transaction))
					{
						CommitTransaction(transaction, ref invalidated);
						transaction = null;
						continue;
					}
					if (processed >= budget)
						break;

					CompareNextFullTombstoneCell(transaction);
					processed++;
				}
			}

			return processed;
		}

		private IncrementalTransaction CreateIncrementalTransaction()
		{
			Dictionary<ChunkCoordinate, IncrementalSourceChunk> sources =
				new Dictionary<ChunkCoordinate, IncrementalSourceChunk>(residents.Count);
			foreach (KeyValuePair<ChunkCoordinate, ResidentChunk> item in residents)
				sources.Add(item.Key, new IncrementalSourceChunk(item.Value));
			IncrementalTransaction incremental = new IncrementalTransaction(sources);

			incremental.AddedCoordinates.AddRange(addedChunks);
			incremental.AddedCoordinates.Sort(CompareCoordinates);

			List<ChunkCoordinate> orderedSky = new List<ChunkCoordinate>(skyChangedChunks);
			orderedSky.Sort(CompareCoordinates);
			foreach (ChunkCoordinate coordinate in orderedSky)
			{
				if (!sources.TryGetValue(coordinate, out IncrementalSourceChunk source))
					continue;
				incremental.SkyChanges.Add(
					new SkyExposureChange(coordinate, source.DesiredSkyExposedAbove)
				);
			}

			HashSet<ChunkCoordinate> dirtyCoordinates =
				new HashSet<ChunkCoordinate>(dirtyWorldChunks);
			foreach (KeyValuePair<ChunkCoordinate, HashSet<int>> item in dirtyWorldCells)
			{
				if (dirtyWorldChunks.Contains(item.Key))
					continue;
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
					incremental.RemovedTombstones.Add(coordinate, tombstone);
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
						if (incremental.AddedCoordinateIndex >= incremental.AddedCoordinates.Count)
						{
							incremental.Phase = IncrementalPhase.PrepareSky;
							continue;
						}
						ChunkCoordinate addedCoordinate =
							incremental.AddedCoordinates[incremental.AddedCoordinateIndex];
						if (!incremental.Sources.TryGetValue(addedCoordinate, out IncrementalSourceChunk addedSource)
							|| addedSource.PublishedLights != null
							|| !residents.TryGetValue(addedCoordinate, out ResidentChunk addedResident)
							|| !ReferenceEquals(addedResident, addedSource.Resident))
						{
							incremental.AddedCoordinateIndex++;
							incremental.AddedCellIndex = 0;
							continue;
						}
						if (!canConsumeWork)
							return IncrementalStepResult.NeedsBudget;
						if (!incremental.AddedMaterialCells.TryGetValue(
							addedCoordinate,
							out ReadOnlyMemory<VoxelCell> addedCells
						))
						{
							addedCells = world.CaptureChunkCells(addedCoordinate);
							incremental.AddedMaterialCells.Add(addedCoordinate, addedCells);
						}
						if (!incremental.Chunks.TryGetValue(addedCoordinate, out IncrementalWorkingChunk added))
						{
							added = new IncrementalWorkingChunk(
								addedSource.Resident,
								addedSource.DesiredSkyExposedAbove,
								new ushort[VoxelWorld.ChunkVolume],
								new ushort[VoxelWorld.ChunkVolume],
								new byte[VoxelWorld.ChunkVolume],
								isNew: true
							);
							incremental.Chunks.Add(addedCoordinate, added);
						}
						int addedIndex = incremental.AddedCellIndex;
						added.MaterialSignatures[addedIndex] = GetMaterialSignature(
							addedCells.Span[addedIndex].MaterialId
						);
						EnqueueIncremental(incremental, added, addedIndex);
						GetLocalCoordinates(addedIndex, out int addedX, out int addedY, out int addedZ);
						if (addedY == 0)
							incremental.DirectColumnSet.Add(new WorldColumn(
								addedCoordinate.X * VoxelWorld.ChunkSize + addedX,
								addedCoordinate.Z * VoxelWorld.ChunkSize + addedZ
							));
						AdvanceCellCursor(
							ref incremental.AddedCoordinateIndex,
							ref incremental.AddedCellIndex
						);
						return IncrementalStepResult.Consumed;

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
							return IncrementalStepResult.NeedsBudget;
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
							skyWorking,
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
							return IncrementalStepResult.NeedsBudget;
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
							previousSignature = dirty.MaterialSignatures[dirtyIndex];
						else
							previousSignature = dirtySource.MaterialSignatures[dirtyIndex];
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
							return IncrementalStepResult.NeedsBudget;
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
							return IncrementalStepResult.NeedsBudget;
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
							return IncrementalStepResult.NeedsBudget;
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
							return IncrementalStepResult.NeedsBudget;
						RelaxNextCell(incremental);
						return IncrementalStepResult.Consumed;

					case IncrementalPhase.CompareWorking:
						if (!TryPrepareNextIncrementalComparisonCell(incremental))
						{
							incremental.Phase = IncrementalPhase.CompareTombstones;
							continue;
						}
						if (!canConsumeWork)
							return IncrementalStepResult.NeedsBudget;
						CompareNextIncrementalCell(incremental);
						return IncrementalStepResult.Consumed;

					case IncrementalPhase.CompareTombstones:
						if (!TryPrepareNextIncrementalTombstoneCell(incremental))
						{
							CommitIncrementalTransaction(incremental, ref invalidated);
							return IncrementalStepResult.Completed;
						}
						if (!canConsumeWork)
							return IncrementalStepResult.NeedsBudget;
						CompareNextIncrementalTombstoneCell(incremental);
						return IncrementalStepResult.Consumed;

					default:
						throw new InvalidOperationException("Unknown incremental lighting phase.");
				}
			}
		}

		private static void AdvanceCellCursor(ref int coordinateIndex, ref int cellIndex)
		{
			cellIndex++;
			if (cellIndex == VoxelWorld.ChunkVolume)
			{
				cellIndex = 0;
				coordinateIndex++;
			}
		}

		private void EnqueueRemovedBoundaryNeighbor(
			IncrementalTransaction incremental,
			ChunkCoordinate removedCoordinate,
			int boundaryIndex
		)
		{
			int face = boundaryIndex / (VoxelWorld.ChunkSize * VoxelWorld.ChunkSize);
			int cell = boundaryIndex % (VoxelWorld.ChunkSize * VoxelWorld.ChunkSize);
			int first = cell % VoxelWorld.ChunkSize;
			int second = cell / VoxelWorld.ChunkSize;

			switch (face)
			{
				case 0:
					EnqueueAt(
						incremental,
						removedCoordinate + new ChunkCoordinate(-1, 0, 0),
						VoxelWorld.ChunkSize - 1,
						first,
						second
					);
					break;
				case 1:
					EnqueueAt(
						incremental,
						removedCoordinate + new ChunkCoordinate(1, 0, 0),
						0,
						first,
						second
					);
					break;
				case 2:
					EnqueueAt(
						incremental,
						removedCoordinate + new ChunkCoordinate(0, -1, 0),
						first,
						VoxelWorld.ChunkSize - 1,
						second
					);
					break;
				case 3:
					EnqueueAt(
						incremental,
						removedCoordinate + new ChunkCoordinate(0, 1, 0),
						first,
						0,
						second
					);
					break;
				case 4:
					EnqueueAt(
						incremental,
						removedCoordinate + new ChunkCoordinate(0, 0, -1),
						first,
						second,
						VoxelWorld.ChunkSize - 1
					);
					break;
				case 5:
					EnqueueAt(
						incremental,
						removedCoordinate + new ChunkCoordinate(0, 0, 1),
						first,
						second,
						0
					);
					break;
				default:
					throw new ArgumentOutOfRangeException(nameof(boundaryIndex));
			}
		}

		private void InitializeIncrementalDirectTraversal(IncrementalTransaction incremental)
		{
			incremental.DirectColumns.AddRange(incremental.DirectColumnSet);
			incremental.DirectColumns.Sort(CompareWorldColumns);

			foreach (IncrementalSourceChunk source in incremental.Sources.Values)
			{
				if (source.PublishedLights == null
					&& !incremental.Chunks.ContainsKey(source.Resident.Coordinate))
					continue;

				HorizontalChunkCoordinate horizontal = new HorizontalChunkCoordinate(
					source.Resident.Coordinate.X,
					source.Resident.Coordinate.Z
				);
				if (!incremental.DirectGroups.TryGetValue(
					horizontal,
					out List<ResidentChunk> group
				))
				{
					group = new List<ResidentChunk>();
					incremental.DirectGroups.Add(horizontal, group);
				}
				group.Add(source.Resident);
			}

			foreach (List<ResidentChunk> group in incremental.DirectGroups.Values)
				group.Sort((left, right) => right.Coordinate.Y.CompareTo(left.Coordinate.Y));

			long remaining = 0;
			foreach (WorldColumn column in incremental.DirectColumns)
			{
				HorizontalChunkCoordinate horizontal = HorizontalFromWorldColumn(column);
				if (incremental.DirectGroups.TryGetValue(horizontal, out List<ResidentChunk> group))
					remaining += (long)group.Count * VoxelWorld.ChunkSize;
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
						out List<ResidentChunk> group
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

				ResidentChunk resident =
					incremental.ActiveDirectGroup[incremental.DirectChunkIndex];
				IncrementalWorkingChunk working =
					EnsureIncrementalWorking(incremental, resident.Coordinate);
				if (working == null)
				{
					incremental.DirectChunkIndex++;
					incremental.DirectY = VoxelWorld.ChunkSize - 1;
					incremental.DirectChunkStarted = false;
					continue;
				}

				if (!incremental.DirectChunkStarted)
				{
					if (incremental.DirectChunkIndex == 0
						|| resident.Coordinate.Y
							!= incremental.ActiveDirectGroup[
								incremental.DirectChunkIndex - 1
							].Coordinate.Y - 1)
						incremental.DirectIncoming = 0;
					if (working.SkyExposedAbove)
						incremental.DirectIncoming = 15;
					incremental.DirectChunkStarted = true;
				}

				incremental.ActiveDirectWorking = working;
				return true;
			}

			return false;
		}

		private void ProcessIncrementalDirectCell(IncrementalTransaction incremental)
		{
			IncrementalWorkingChunk working = incremental.ActiveDirectWorking;
			int index = Index(
				incremental.DirectLocalX,
				incremental.DirectY,
				incremental.DirectLocalZ
			);
			byte direct = Subtract(
				incremental.DirectIncoming,
				GetOpacity(working.MaterialSignatures[index])
			);
			incremental.DirectIncoming = direct;
			if (working.DirectSky[index] != direct)
			{
				working.DirectSky[index] = direct;
				EnqueueIncrementalWithNeighbors(incremental, working.Coordinate, index);
			}

			incremental.DirectRemaining--;
			incremental.DirectY--;
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
			int index = incremental.ComparisonCellIndex;

			if (index == 0)
			{
				if (working.IsNew)
					incremental.InvalidationTargets.Add(working.Coordinate);
				if ((working.IsNew && working.SkyExposedAbove) || working.SkyExposureChanged)
					AddSkyHaloTargets(working.Coordinate, incremental.InvalidationTargets);
			}

			if (working.IsNew)
			{
				if (working.Lights[index] != 0)
					AddHaloTargets(working.Coordinate, index, incremental.InvalidationTargets);
			}
			else if (resident.PublishedLights[index] != working.Lights[index])
			{
				incremental.InvalidationTargets.Add(working.Coordinate);
				AddHaloTargets(working.Coordinate, index, incremental.InvalidationTargets);
			}

			AdvanceCellCursor(
				ref incremental.ComparisonCoordinateIndex,
				ref incremental.ComparisonCellIndex
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
				AddSkyHaloTargets(coordinate, incremental.InvalidationTargets);
			if (tombstone.PublishedLights[index] != 0)
				AddHaloTargets(coordinate, index, incremental.InvalidationTargets);
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

		private IncrementalWorkingChunk EnsureIncrementalWorking(
			IncrementalTransaction incremental,
			ChunkCoordinate coordinate
		)
		{
			if (incremental.Chunks.TryGetValue(coordinate, out IncrementalWorkingChunk existing))
			{
				if (incremental.InvalidatedSourceCoordinates.Contains(coordinate))
					incremental.DiscardAtCommit = true;
				return existing;
			}
			if (!incremental.Sources.TryGetValue(coordinate, out IncrementalSourceChunk source)
				|| source.PublishedLights == null
				|| source.MaterialSignatures == null)
				return null;

			IncrementalWorkingChunk working = new IncrementalWorkingChunk(
				source.Resident,
				source.PublishedSkyExposedAbove,
				(ushort[])source.MaterialSignatures.Clone(),
				(ushort[])source.PublishedLights.Clone(),
				(byte[])source.PublishedDirectSky.Clone(),
				isNew: false
			);
			incremental.Chunks.Add(coordinate, working);
			if (incremental.InvalidatedSourceCoordinates.Contains(coordinate))
				incremental.DiscardAtCommit = true;
			return working;
		}

		private void EnqueueIncrementalWithNeighbors(
			IncrementalTransaction incremental,
			ChunkCoordinate coordinate,
			int index
		)
		{
			IncrementalWorkingChunk center = EnsureIncrementalWorking(incremental, coordinate);
			if (center == null)
				return;

			EnqueueIncremental(incremental, center, index);
			GetLocalCoordinates(index, out int x, out int y, out int z);
			foreach (Neighbor neighbor in Neighbors)
			{
				ResolveLocal(
					coordinate,
					x + neighbor.X,
					y + neighbor.Y,
					z + neighbor.Z,
					out ChunkCoordinate neighborCoordinate,
					out int neighborX,
					out int neighborY,
					out int neighborZ
				);
				IncrementalWorkingChunk neighborChunk =
					EnsureIncrementalWorking(incremental, neighborCoordinate);
				if (neighborChunk != null)
					EnqueueIncremental(
						incremental,
						neighborChunk,
						Index(neighborX, neighborY, neighborZ)
					);
			}
		}

		private void EnqueueAt(
			IncrementalTransaction incremental,
			ChunkCoordinate coordinate,
			int x,
			int y,
			int z
		)
		{
			IncrementalWorkingChunk chunk = EnsureIncrementalWorking(incremental, coordinate);
			if (chunk != null)
				EnqueueIncremental(incremental, chunk, Index(x, y, z));
		}

		private static void EnqueueIncremental(
			IncrementalTransaction incremental,
			IncrementalWorkingChunk chunk,
			int index
		)
		{
			if (chunk.Queued[index])
				return;

			chunk.Queued[index] = true;
			incremental.Relaxation.Enqueue(new CellAddress(chunk.Coordinate, index));
		}

		private void RelaxNextCell(IncrementalTransaction incremental)
		{
			CellAddress address = incremental.Relaxation.Dequeue();
			if (!incremental.Chunks.TryGetValue(address.Coordinate, out IncrementalWorkingChunk target))
				return;

			target.Queued[address.Index] = false;
			GetLocalCoordinates(address.Index, out int targetX, out int targetY, out int targetZ);
			ushort signature = target.MaterialSignatures[address.Index];
			byte opacity = GetOpacity(signature);
			byte red = GetEmissionRed(signature);
			byte green = GetEmissionGreen(signature);
			byte blue = GetEmissionBlue(signature);
			byte sky = target.DirectSky[address.Index];
			byte ordinaryLoss = Math.Max((byte)1, opacity);

			foreach (Neighbor neighbor in Neighbors)
			{
				ResolveLocal(
					address.Coordinate,
					targetX + neighbor.X,
					targetY + neighbor.Y,
					targetZ + neighbor.Z,
					out ChunkCoordinate sourceCoordinate,
					out int sourceX,
					out int sourceY,
					out int sourceZ
				);
				if (!TryGetIncrementalLight(
					incremental,
					sourceCoordinate,
					Index(sourceX, sourceY, sourceZ),
					out ushort source
				))
					continue;

				red = Math.Max(red, Subtract((byte)(source & 0xf), ordinaryLoss));
				green = Math.Max(
					green,
					Subtract((byte)((source >> 4) & 0xf), ordinaryLoss)
				);
				blue = Math.Max(
					blue,
					Subtract((byte)((source >> 8) & 0xf), ordinaryLoss)
				);
				sky = Math.Max(sky, Subtract((byte)((source >> 12) & 0xf), ordinaryLoss));
			}

			ushort relaxed = VoxelLight.Pack(red, green, blue, sky);
			if (target.Lights[address.Index] == relaxed)
				return;

			target.Lights[address.Index] = relaxed;
			EnqueueIncrementalWithNeighbors(incremental, address.Coordinate, address.Index);
		}

		private bool TryGetIncrementalLight(
			IncrementalTransaction incremental,
			ChunkCoordinate coordinate,
			int index,
			out ushort light
		)
		{
			if (incremental.Chunks.TryGetValue(coordinate, out IncrementalWorkingChunk working))
			{
				light = working.Lights[index];
				return true;
			}
			if (incremental.Sources.TryGetValue(coordinate, out IncrementalSourceChunk source)
				&& source.PublishedLights != null)
			{
				incremental.ReferencedSourceCoordinates.Add(coordinate);
				if (incremental.InvalidatedSourceCoordinates.Contains(coordinate))
					incremental.DiscardAtCommit = true;
				light = source.PublishedLights[index];
				return true;
			}

			light = 0;
			return false;
		}

		private void CommitIncrementalTransaction(
			IncrementalTransaction incremental,
			ref List<(ChunkCoordinate Coordinate, long Revision)> invalidated
		)
		{
			foreach (IncrementalWorkingChunk working in incremental.Chunks.Values)
			{
				if (!residents.TryGetValue(working.Coordinate, out ResidentChunk resident)
					|| !ReferenceEquals(resident, working.Resident))
					continue;

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
				if (residents.TryGetValue(coordinate, out ResidentChunk resident)
					&& resident.PublishedLights != null)
				{
					resident.Revision++;
					(invalidated ??= new List<(ChunkCoordinate, long)>()).Add(
						(coordinate, resident.Revision)
					);
				}
		}

		private void RemoveCapturedTombstones(
			Dictionary<ChunkCoordinate, RemovedChunkTombstone> captured
		)
		{
			foreach (KeyValuePair<ChunkCoordinate, RemovedChunkTombstone> item in captured)
				if (removedTombstones.TryGetValue(
					item.Key,
					out RemovedChunkTombstone current
				) && ReferenceEquals(current, item.Value))
					removedTombstones.Remove(item.Key);
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
					continue;

				if (resident.PublishedLights == null)
					addedChunks.Add(coordinate);
				else
				{
					dirtyWorldChunks.Add(coordinate);
					dirtyWorldCells.Remove(coordinate);
				}
				if (!resident.HasPublishedSkyExposure
					|| resident.PublishedSkyExposedAbove != resident.SkyExposedAbove)
					skyChangedChunks.Add(coordinate);
			}

			foreach (ChunkCoordinate coordinate in incremental.RemovedCoordinates)
				removedChunks.Add(coordinate);
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
				count += cells.Count;
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
			if (rebuild.InitializeChunkIndex >= rebuild.Chunks.Count)
			{
				rebuild.Phase = RebuildPhase.SeedDirectSky;
				return;
			}

			WorkingChunk chunk = rebuild.Chunks[rebuild.InitializeChunkIndex];
			if (chunk.MaterialCells.IsEmpty)
			{
				chunk.InitializeStorage(world.CaptureChunkCells(chunk.Coordinate));
			}
			int index = rebuild.InitializeCellIndex;
			ushort signature = GetMaterialSignature(chunk.MaterialCells.Span[index].MaterialId);
			chunk.MaterialSignatures[index] = signature;

			byte opacity = GetOpacity(signature);
			byte red = GetEmissionRed(signature);
			byte green = GetEmissionGreen(signature);
			byte blue = GetEmissionBlue(signature);
			byte sky = 0;

			ushort light = VoxelLight.Pack(red, green, blue, sky);
			chunk.Lights[index] = light;
			if (light != 0)
				Enqueue(rebuild, chunk, index);

			rebuild.InitializeCellIndex++;
			if (rebuild.InitializeCellIndex == VoxelWorld.ChunkVolume)
			{
				rebuild.InitializeCellIndex = 0;
				rebuild.InitializeChunkIndex++;
				if (rebuild.InitializeChunkIndex == rebuild.Chunks.Count)
					rebuild.Phase = RebuildPhase.SeedDirectSky;
			}
		}

		private static void InitializeFullDirectTraversal(RebuildTransaction rebuild)
		{
			if (rebuild.DirectInitialized)
				return;

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
				group.Sort((left, right) => right.Coordinate.Y.CompareTo(left.Coordinate.Y));
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
						rebuild.DirectIncoming = 0;
					if (working.SkyExposedAbove)
						rebuild.DirectIncoming = 15;
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

		private static void PropagateNextCell(RebuildTransaction rebuild)
		{
			CellAddress sourceAddress = rebuild.Propagation.Dequeue();
			WorkingChunk sourceChunk = rebuild.Lookup[sourceAddress.Coordinate];
			sourceChunk.Queued[sourceAddress.Index] = false;
			ushort source = sourceChunk.Lights[sourceAddress.Index];
			GetLocalCoordinates(sourceAddress.Index, out int sourceX, out int sourceY, out int sourceZ);

			foreach (Neighbor neighbor in Neighbors)
			{
				ResolveLocal(
					sourceAddress.Coordinate,
					sourceX + neighbor.X,
					sourceY + neighbor.Y,
					sourceZ + neighbor.Z,
					out ChunkCoordinate targetCoordinate,
					out int targetX,
					out int targetY,
					out int targetZ
				);

				if (!rebuild.Lookup.TryGetValue(targetCoordinate, out WorkingChunk targetChunk))
					continue;

				int targetIndex = Index(targetX, targetY, targetZ);
				byte opacity = GetOpacity(targetChunk.MaterialSignatures[targetIndex]);
				byte ordinaryLoss = Math.Max((byte)1, opacity);
				ushort current = targetChunk.Lights[targetIndex];
				byte red = Math.Max((byte)(current & 0xf), Subtract((byte)(source & 0xf), ordinaryLoss));
				byte green = Math.Max(
					(byte)((current >> 4) & 0xf),
					Subtract((byte)((source >> 4) & 0xf), ordinaryLoss)
				);
				byte blue = Math.Max(
					(byte)((current >> 8) & 0xf),
					Subtract((byte)((source >> 8) & 0xf), ordinaryLoss)
				);
				byte sky = Math.Max(
					(byte)((current >> 12) & 0xf),
					Subtract((byte)((source >> 12) & 0xf), ordinaryLoss)
				);
				ushort propagated = VoxelLight.Pack(red, green, blue, sky);

				if (propagated == current)
					continue;

				targetChunk.Lights[targetIndex] = propagated;
				Enqueue(rebuild, targetChunk, targetIndex);
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
				AddSkyHaloTargets(working.Coordinate, rebuild.InvalidationTargets);

			if (resident.PublishedLights == null)
			{
				working.Changed = true;
				rebuild.InvalidationTargets.Add(working.Coordinate);
				if (working.Lights[index] != 0)
					AddHaloTargets(working.Coordinate, index, rebuild.InvalidationTargets);
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
				return;

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
				AddSkyHaloTargets(coordinate, rebuild.InvalidationTargets);
			if (tombstone.PublishedLights[index] != 0)
				AddHaloTargets(coordinate, index, rebuild.InvalidationTargets);
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
					continue;

				resident.MaterialSignatures = working.MaterialSignatures;
				resident.PublishedDirectSky = working.DirectSky;
				resident.PublishedSkyExposedAbove = working.SkyExposedAbove;
				resident.HasPublishedSkyExposure = true;

				if (!working.Changed)
					continue;

				resident.PublishedLights = working.Lights;
			}

			RemoveCapturedTombstones(rebuild.RemovedTombstones);

			List<ChunkCoordinate> orderedTargets =
				new List<ChunkCoordinate>(rebuild.InvalidationTargets);
			orderedTargets.Sort(CompareCoordinates);

			foreach (ChunkCoordinate coordinate in orderedTargets)
				if (residents.TryGetValue(coordinate, out ResidentChunk resident)
					&& resident.PublishedLights != null)
				{
					resident.Revision++;
					(invalidated ??= new List<(ChunkCoordinate, long)>()).Add(
						(coordinate, resident.Revision)
					);
				}
		}

		private ushort GetMaterialSignature(ushort materialId)
		{
			if (materialId == 0)
				return 0;
			if (!palette.Contains(materialId))
				return 15;

			VoxelMaterialLightSettings light = palette[materialId].Light;
			return (ushort)(
				light.Opacity
				| (light.Emission.Red << 4)
				| (light.Emission.Green << 8)
				| (light.Emission.Blue << 12)
			);
		}

		private void HandleWorldContentChanged(VoxelWorldContentChange change)
		{
			lock (sync)
			{
				if (disposed || !residents.ContainsKey(change.Coordinate))
					return;

				if (change.IsBulk)
				{
					dirtyWorldChunks.Add(change.Coordinate);
					dirtyWorldCells.Remove(change.Coordinate);
					return;
				}

				if (GetMaterialSignature(change.PreviousMaterialId)
					== GetMaterialSignature(change.MaterialId)
					|| dirtyWorldChunks.Contains(change.Coordinate))
					return;

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

		private static void Enqueue(RebuildTransaction rebuild, WorkingChunk chunk, int index)
		{
			if (chunk.Queued[index])
				return;

			chunk.Queued[index] = true;
			rebuild.Propagation.Enqueue(new CellAddress(chunk.Coordinate, index));
		}

		private static void AddHaloTargets(
			ChunkCoordinate coordinate,
			int index,
			HashSet<ChunkCoordinate> targets
		)
		{
			GetLocalCoordinates(index, out int x, out int y, out int z);
			int minX = x == 0 ? -1 : 0;
			int maxX = x == VoxelWorld.ChunkSize - 1 ? 1 : 0;
			int minY = y == 0 ? -1 : 0;
			int maxY = y == VoxelWorld.ChunkSize - 1 ? 1 : 0;
			int minZ = z == 0 ? -1 : 0;
			int maxZ = z == VoxelWorld.ChunkSize - 1 ? 1 : 0;

			for (int offsetZ = minZ; offsetZ <= maxZ; offsetZ++)
				for (int offsetY = minY; offsetY <= maxY; offsetY++)
					for (int offsetX = minX; offsetX <= maxX; offsetX++)
					{
						if (offsetX == 0 && offsetY == 0 && offsetZ == 0)
							continue;

						targets.Add(coordinate + new ChunkCoordinate(offsetX, offsetY, offsetZ));
					}
		}

		private static void AddSkyHaloTargets(
			ChunkCoordinate coordinate,
			HashSet<ChunkCoordinate> targets
		)
		{
			for (int offsetZ = -1; offsetZ <= 1; offsetZ++)
				for (int offsetX = -1; offsetX <= 1; offsetX++)
					targets.Add(coordinate + new ChunkCoordinate(offsetX, 0, offsetZ));
		}

		private static byte GetOpacity(ushort signature) => (byte)(signature & 0xf);
		private static byte GetEmissionRed(ushort signature) => (byte)((signature >> 4) & 0xf);
		private static byte GetEmissionGreen(ushort signature) => (byte)((signature >> 8) & 0xf);
		private static byte GetEmissionBlue(ushort signature) => (byte)((signature >> 12) & 0xf);
		private static byte Subtract(byte value, byte loss) => value > loss ? (byte)(value - loss) : (byte)0;

		private static int Index(int x, int y, int z)
		{
			return x + VoxelWorld.ChunkSize * (y + VoxelWorld.ChunkSize * z);
		}

		private static void GetLocalCoordinates(int index, out int x, out int y, out int z)
		{
			x = index % VoxelWorld.ChunkSize;
			index /= VoxelWorld.ChunkSize;
			y = index % VoxelWorld.ChunkSize;
			z = index / VoxelWorld.ChunkSize;
		}

		private static void ResolveLocal(
			ChunkCoordinate coordinate,
			int x,
			int y,
			int z,
			out ChunkCoordinate resolvedCoordinate,
			out int resolvedX,
			out int resolvedY,
			out int resolvedZ
		)
		{
			int chunkX = coordinate.X;
			int chunkY = coordinate.Y;
			int chunkZ = coordinate.Z;
			resolvedX = x;
			resolvedY = y;
			resolvedZ = z;

			if (resolvedX < 0)
			{
				resolvedX += VoxelWorld.ChunkSize;
				chunkX--;
			}
			else if (resolvedX >= VoxelWorld.ChunkSize)
			{
				resolvedX -= VoxelWorld.ChunkSize;
				chunkX++;
			}

			if (resolvedY < 0)
			{
				resolvedY += VoxelWorld.ChunkSize;
				chunkY--;
			}
			else if (resolvedY >= VoxelWorld.ChunkSize)
			{
				resolvedY -= VoxelWorld.ChunkSize;
				chunkY++;
			}

			if (resolvedZ < 0)
			{
				resolvedZ += VoxelWorld.ChunkSize;
				chunkZ--;
			}
			else if (resolvedZ >= VoxelWorld.ChunkSize)
			{
				resolvedZ -= VoxelWorld.ChunkSize;
				chunkZ++;
			}

			resolvedCoordinate = new ChunkCoordinate(chunkX, chunkY, chunkZ);
		}

		private static int CompareCoordinates(ChunkCoordinate left, ChunkCoordinate right)
		{
			int comparison = left.X.CompareTo(right.X);
			if (comparison != 0)
				return comparison;
			comparison = left.Y.CompareTo(right.Y);
			return comparison != 0 ? comparison : left.Z.CompareTo(right.Z);
		}

		private void ThrowIfDisposed()
		{
			if (disposed)
				throw new ObjectDisposedException(nameof(VoxelLighting));
		}

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

			internal void InitializeStorage(ReadOnlyMemory<VoxelCell> materialCells)
			{
				MaterialCells = materialCells;
				MaterialSignatures = new ushort[VoxelWorld.ChunkVolume];
				Lights = new ushort[VoxelWorld.ChunkVolume];
				DirectSky = new byte[VoxelWorld.ChunkVolume];
				Queued = new bool[VoxelWorld.ChunkVolume];
			}

			internal ChunkCoordinate Coordinate { get; }
			internal bool SkyExposedAbove { get; }
			internal ReadOnlyMemory<VoxelCell> MaterialCells { get; private set; }
			internal ushort[] MaterialSignatures { get; private set; }
			internal ushort[] Lights { get; private set; }
			internal byte[] DirectSky { get; private set; }
			internal bool[] Queued { get; private set; }
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
				Queued = new bool[VoxelWorld.ChunkVolume];
			}

			internal ResidentChunk Resident { get; }
			internal ChunkCoordinate Coordinate => Resident.Coordinate;
			internal bool SkyExposedAbove { get; set; }
			internal bool SkyExposureChanged { get; set; }
			internal ushort[] MaterialSignatures { get; }
			internal ushort[] Lights { get; }
			internal byte[] DirectSky { get; }
			internal bool[] Queued { get; }
			internal bool IsNew { get; }
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
			internal Dictionary<ChunkCoordinate, ReadOnlyMemory<VoxelCell>> AddedMaterialCells { get; } =
				new Dictionary<ChunkCoordinate, ReadOnlyMemory<VoxelCell>>();
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
			internal Queue<CellAddress> Relaxation { get; } = new Queue<CellAddress>();
			internal HashSet<WorldColumn> DirectColumnSet { get; } =
				new HashSet<WorldColumn>();
			internal List<WorldColumn> DirectColumns { get; } = new List<WorldColumn>();
			internal Dictionary<HorizontalChunkCoordinate, List<ResidentChunk>> DirectGroups { get; } =
				new Dictionary<HorizontalChunkCoordinate, List<ResidentChunk>>();
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
			internal int SkyChangeIndex { get; set; }
			internal int SkyCellIndex { get; set; }
			internal int DirtyCoordinateIndex;
			internal int DirtyCellIndex;
			internal int RemovedColumnCoordinateIndex { get; set; }
			internal int RemovedColumnIndex { get; set; }
			internal int RemovedBoundaryCoordinateIndex { get; set; }
			internal int RemovedBoundaryIndex { get; set; }
			internal int DirectColumnIndex { get; set; }
			internal List<ResidentChunk> ActiveDirectGroup { get; set; }
			internal IncrementalWorkingChunk ActiveDirectWorking { get; set; }
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
							remaining = RemainingCells(
								AddedCoordinates.Count,
								AddedCoordinateIndex,
								AddedCellIndex,
								VoxelWorld.ChunkVolume
							);
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
							remaining = RemainingCells(
								ComparisonCoordinates.Count,
								ComparisonCoordinateIndex,
								ComparisonCellIndex,
								VoxelWorld.ChunkVolume
							);
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
							count = (long)(Chunks.Count - InitializeChunkIndex)
								* VoxelWorld.ChunkVolume - InitializeCellIndex;
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

		private readonly struct Neighbor
		{
			internal Neighbor(int x, int y, int z)
			{
				X = x;
				Y = y;
				Z = z;
			}

			internal int X { get; }
			internal int Y { get; }
			internal int Z { get; }
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
}
