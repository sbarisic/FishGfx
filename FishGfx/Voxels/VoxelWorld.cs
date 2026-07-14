using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace FishGfx.Voxels
{
	public sealed class VoxelChunk
	{
		private VoxelCell[] cells = new VoxelCell[VoxelWorld.ChunkVolume];

		internal VoxelChunk(ChunkCoordinate coordinate, long generation)
		{
			Coordinate = coordinate;
			Generation = generation;
			Revision = 1;
		}

		public ChunkCoordinate Coordinate { get; }
		internal long Generation { get; }
		public long Revision { get; internal set; }
		public int NonAirCount { get; internal set; }
		public bool IsEmpty => NonAirCount == 0;

		public VoxelCell GetLocal(int x, int y, int z)
		{
			ValidateLocal(x, y, z);
			return System.Threading.Volatile.Read(ref cells)[Index(x, y, z)];
		}

		internal VoxelCell GetLocalUnchecked(int x, int y, int z)
		{
			return System.Threading.Volatile.Read(ref cells)[Index(x, y, z)];
		}

		internal ReadOnlyMemory<VoxelCell> CaptureCellsUnchecked()
		{
			return System.Threading.Volatile.Read(ref cells);
		}

		internal bool SetLocalUnchecked(int x, int y, int z, VoxelCell value)
		{
			int index = Index(x, y, z);
			VoxelCell[] current = System.Threading.Volatile.Read(ref cells);
			VoxelCell previous = current[index];

			if (previous == value)
				return false;

			VoxelCell[] replacement = (VoxelCell[])current.Clone();
			replacement[index] = value;
			System.Threading.Volatile.Write(ref cells, replacement);

			if (previous.IsAir && !value.IsAir)
				NonAirCount++;
			else if (!previous.IsAir && value.IsAir)
				NonAirCount--;

			return true;
		}

		internal void FillUnchecked(VoxelCell value)
		{
			VoxelCell[] replacement = new VoxelCell[VoxelWorld.ChunkVolume];
			if (!value.IsAir)
				Array.Fill(replacement, value);
			System.Threading.Volatile.Write(ref cells, replacement);
			NonAirCount = value.IsAir ? 0 : replacement.Length;
		}

		internal bool ReplaceUnchecked(ReadOnlySpan<VoxelCell> values)
		{
			VoxelCell[] current = System.Threading.Volatile.Read(ref cells);
			bool changed = false;
			int nonAirCount = 0;

			for (int i = 0; i < current.Length; i++)
			{
				changed |= current[i] != values[i];

				if (!values[i].IsAir)
					nonAirCount++;
			}

			if (!changed)
				return false;

			VoxelCell[] replacement = new VoxelCell[VoxelWorld.ChunkVolume];
			values.CopyTo(replacement);
			System.Threading.Volatile.Write(ref cells, replacement);
			NonAirCount = nonAirCount;
			return true;
		}

		private static int Index(int x, int y, int z) => x + VoxelWorld.ChunkSize * (y + VoxelWorld.ChunkSize * z);

		private static void ValidateLocal(int x, int y, int z)
		{
			if ((uint)x >= VoxelWorld.ChunkSize)
				throw new ArgumentOutOfRangeException(nameof(x));
			if ((uint)y >= VoxelWorld.ChunkSize)
				throw new ArgumentOutOfRangeException(nameof(y));
			if ((uint)z >= VoxelWorld.ChunkSize)
				throw new ArgumentOutOfRangeException(nameof(z));
		}
	}

	public sealed class VoxelChunkSnapshot
	{
		internal const int PaddedSize = VoxelWorld.ChunkSize + 2;
		private readonly ushort[] paddedMaterials;

		internal VoxelChunkSnapshot(
			ChunkCoordinate coordinate,
			long generation,
			long revision,
			ushort[] paddedMaterials
		)
		{
			Coordinate = coordinate;
			Generation = generation;
			Revision = revision;
			this.paddedMaterials = paddedMaterials;
		}

		public ChunkCoordinate Coordinate { get; }
		internal long Generation { get; }
		public long Revision { get; }

		public ushort GetMaterial(int localX, int localY, int localZ)
		{
			if (localX < -1 || localX > VoxelWorld.ChunkSize)
				throw new ArgumentOutOfRangeException(nameof(localX));
			if (localY < -1 || localY > VoxelWorld.ChunkSize)
				throw new ArgumentOutOfRangeException(nameof(localY));
			if (localZ < -1 || localZ > VoxelWorld.ChunkSize)
				throw new ArgumentOutOfRangeException(nameof(localZ));

			return GetMaterialUnchecked(localX, localY, localZ);
		}

		internal ushort GetMaterialUnchecked(int localX, int localY, int localZ)
		{
			return paddedMaterials[
				(localX + 1) + PaddedSize * ((localY + 1) + PaddedSize * (localZ + 1))
			];
		}
	}

	internal readonly struct VoxelWorldContentChange
	{
		private VoxelWorldContentChange(
			ChunkCoordinate coordinate,
			int localIndex,
			ushort previousMaterialId,
			ushort materialId,
			bool isBulk
		)
		{
			Coordinate = coordinate;
			LocalIndex = localIndex;
			PreviousMaterialId = previousMaterialId;
			MaterialId = materialId;
			IsBulk = isBulk;
		}

		internal ChunkCoordinate Coordinate { get; }
		internal int LocalIndex { get; }
		internal ushort PreviousMaterialId { get; }
		internal ushort MaterialId { get; }
		internal bool IsBulk { get; }

		internal static VoxelWorldContentChange Single(
			ChunkCoordinate coordinate,
			int localIndex,
			ushort previousMaterialId,
			ushort materialId
		)
		{
			return new VoxelWorldContentChange(
				coordinate,
				localIndex,
				previousMaterialId,
				materialId,
				isBulk: false
			);
		}

		internal static VoxelWorldContentChange Bulk(ChunkCoordinate coordinate)
		{
			return new VoxelWorldContentChange(coordinate, -1, 0, 0, isBulk: true);
		}
	}

	public sealed class VoxelWorld
	{
		public const int ChunkSize = 16;
		public const int ChunkVolume = ChunkSize * ChunkSize * ChunkSize;
		private static readonly ReadOnlyMemory<VoxelCell> EmptyChunkCells =
			new VoxelCell[ChunkVolume];

		private readonly object sync = new object();
		private readonly Dictionary<ChunkCoordinate, VoxelChunk> chunks =
			new Dictionary<ChunkCoordinate, VoxelChunk>();
		private long nextChunkGeneration;

		public event Action<ChunkCoordinate, long> ChunkInvalidated;
		public event Action<ChunkCoordinate> ChunkRemoved;
		internal event Action<VoxelWorldContentChange> ContentChanged;

		public int LoadedChunkCount
		{
			get
			{
				lock (sync)
					return chunks.Count;
			}
		}

		public IReadOnlyList<VoxelChunk> LoadedChunks
		{
			get
			{
				lock (sync)
					return Array.AsReadOnly(chunks.Values.ToArray());
			}
		}

		public bool TryGetChunk(ChunkCoordinate coordinate, out VoxelChunk chunk)
		{
			lock (sync)
				return chunks.TryGetValue(coordinate, out chunk);
		}

		public VoxelCell GetVoxel(int x, int y, int z)
		{
			ChunkCoordinate coordinate = ChunkCoordinate.FromWorld(x, y, z, out int localX, out int localY, out int localZ);

			lock (sync)
				return GetVoxelUnchecked(coordinate, localX, localY, localZ);
		}

		public bool SetVoxel(int x, int y, int z, VoxelCell value)
		{
			ChunkCoordinate coordinate = ChunkCoordinate.FromWorld(x, y, z, out int localX, out int localY, out int localZ);
			List<(ChunkCoordinate Coordinate, long Revision)> invalidated;
			ushort previousMaterialId;

			lock (sync)
			{
				if (!chunks.TryGetValue(coordinate, out VoxelChunk chunk))
				{
					if (value.IsAir)
						return false;

					chunk = CreateChunk(coordinate);
					chunks.Add(coordinate, chunk);
				}

				VoxelCell previous = chunk.GetLocalUnchecked(localX, localY, localZ);
				if (!chunk.SetLocalUnchecked(localX, localY, localZ, value))
					return false;
				previousMaterialId = previous.MaterialId;

				invalidated = InvalidateBoundaryNeighborhood(coordinate, localX, localY, localZ);
			}

			RaiseInvalidated(invalidated);
			ContentChanged?.Invoke(VoxelWorldContentChange.Single(
				coordinate,
				localX + ChunkSize * (localY + ChunkSize * localZ),
				previousMaterialId,
				value.MaterialId
			));
			return true;
		}

		public void FillChunk(ChunkCoordinate coordinate, VoxelCell value)
		{
			List<(ChunkCoordinate Coordinate, long Revision)> invalidated;

			lock (sync)
			{
				if (!chunks.TryGetValue(coordinate, out VoxelChunk chunk))
				{
					if (value.IsAir)
						return;

					chunk = CreateChunk(coordinate);
					chunks.Add(coordinate, chunk);
				}

				chunk.FillUnchecked(value);
				invalidated = InvalidateAllNeighbors(coordinate, includeCenter: true);
			}

			RaiseInvalidated(invalidated);
			ContentChanged?.Invoke(VoxelWorldContentChange.Bulk(coordinate));
		}

		public bool SetChunk(ChunkCoordinate coordinate, ReadOnlySpan<VoxelCell> cells)
		{
			if (cells.Length != ChunkVolume)
				throw new ArgumentException($"Chunk data must contain exactly {ChunkVolume} voxels.", nameof(cells));

			bool hasNonAir = false;

			for (int i = 0; i < cells.Length; i++)
				if (!cells[i].IsAir)
				{
					hasNonAir = true;
					break;
				}

			if (!hasNonAir)
				return RemoveChunk(coordinate);

			List<(ChunkCoordinate Coordinate, long Revision)> invalidated;

			lock (sync)
			{
				if (!chunks.TryGetValue(coordinate, out VoxelChunk chunk))
				{
					chunk = CreateChunk(coordinate);
					chunks.Add(coordinate, chunk);
				}

				if (!chunk.ReplaceUnchecked(cells))
					return false;

				invalidated = InvalidateAllNeighbors(coordinate, includeCenter: true);
			}

			RaiseInvalidated(invalidated);
			ContentChanged?.Invoke(VoxelWorldContentChange.Bulk(coordinate));
			return true;
		}

		public bool RemoveChunk(ChunkCoordinate coordinate)
		{
			List<(ChunkCoordinate Coordinate, long Revision)> invalidated;

			lock (sync)
			{
				if (!chunks.Remove(coordinate))
					return false;

				invalidated = InvalidateAllNeighbors(coordinate, includeCenter: false);
			}

			RaiseInvalidated(invalidated);
			ChunkRemoved?.Invoke(coordinate);
			ContentChanged?.Invoke(VoxelWorldContentChange.Bulk(coordinate));
			return true;
		}

		public int RemoveEmptyChunks()
		{
			ChunkCoordinate[] empty;

			lock (sync)
				empty = chunks.Where(pair => pair.Value.IsEmpty).Select(pair => pair.Key).ToArray();

			foreach (ChunkCoordinate coordinate in empty)
				RemoveChunk(coordinate);

			return empty.Length;
		}

		public VoxelChunkSnapshot CreateSnapshot(ChunkCoordinate coordinate)
		{
			lock (sync)
			{
				if (!chunks.TryGetValue(coordinate, out VoxelChunk chunk))
					return null;

				ushort[] padded = new ushort[VoxelChunkSnapshot.PaddedSize * VoxelChunkSnapshot.PaddedSize * VoxelChunkSnapshot.PaddedSize];
				Vector3Int origin = new Vector3Int(
					coordinate.X * ChunkSize,
					coordinate.Y * ChunkSize,
					coordinate.Z * ChunkSize
				);

				for (int z = -1; z <= ChunkSize; z++)
					for (int y = -1; y <= ChunkSize; y++)
						for (int x = -1; x <= ChunkSize; x++)
						{
							VoxelCell cell = GetVoxelWorldUnchecked(origin.X + x, origin.Y + y, origin.Z + z);
							padded[(x + 1) + VoxelChunkSnapshot.PaddedSize * ((y + 1) + VoxelChunkSnapshot.PaddedSize * (z + 1))] = cell.MaterialId;
						}

				return new VoxelChunkSnapshot(
					coordinate,
					chunk.Generation,
					chunk.Revision,
					padded
				);
			}
		}

		private VoxelChunk CreateChunk(ChunkCoordinate coordinate)
		{
			return new VoxelChunk(coordinate, checked(++nextChunkGeneration));
		}

		internal ReadOnlyMemory<VoxelCell> CaptureChunkCells(ChunkCoordinate coordinate)
		{
			lock (sync)
				return chunks.TryGetValue(coordinate, out VoxelChunk chunk)
					? chunk.CaptureCellsUnchecked()
					: EmptyChunkCells;
		}

		private VoxelCell GetVoxelWorldUnchecked(int x, int y, int z)
		{
			ChunkCoordinate coordinate = ChunkCoordinate.FromWorld(x, y, z, out int localX, out int localY, out int localZ);
			return GetVoxelUnchecked(coordinate, localX, localY, localZ);
		}

		private VoxelCell GetVoxelUnchecked(ChunkCoordinate coordinate, int localX, int localY, int localZ)
		{
			return chunks.TryGetValue(coordinate, out VoxelChunk chunk)
				? chunk.GetLocalUnchecked(localX, localY, localZ)
				: VoxelCell.Air;
		}

		private List<(ChunkCoordinate Coordinate, long Revision)> InvalidateBoundaryNeighborhood(
			ChunkCoordinate coordinate,
			int localX,
			int localY,
			int localZ
		)
		{
			int minX = localX == 0 ? -1 : 0;
			int maxX = localX == ChunkSize - 1 ? 1 : 0;
			int minY = localY == 0 ? -1 : 0;
			int maxY = localY == ChunkSize - 1 ? 1 : 0;
			int minZ = localZ == 0 ? -1 : 0;
			int maxZ = localZ == ChunkSize - 1 ? 1 : 0;
			List<(ChunkCoordinate Coordinate, long Revision)> result = new List<(ChunkCoordinate, long)>();

			for (int z = minZ; z <= maxZ; z++)
				for (int y = minY; y <= maxY; y++)
					for (int x = minX; x <= maxX; x++)
						InvalidateExisting(coordinate + new ChunkCoordinate(x, y, z), result);

			return result;
		}

		private List<(ChunkCoordinate Coordinate, long Revision)> InvalidateAllNeighbors(
			ChunkCoordinate coordinate,
			bool includeCenter
		)
		{
			List<(ChunkCoordinate Coordinate, long Revision)> result = new List<(ChunkCoordinate, long)>();

			for (int z = -1; z <= 1; z++)
				for (int y = -1; y <= 1; y++)
					for (int x = -1; x <= 1; x++)
					{
						if (!includeCenter && x == 0 && y == 0 && z == 0)
							continue;

						InvalidateExisting(coordinate + new ChunkCoordinate(x, y, z), result);
					}

			return result;
		}

		private void InvalidateExisting(
			ChunkCoordinate coordinate,
			List<(ChunkCoordinate Coordinate, long Revision)> result
		)
		{
			if (!chunks.TryGetValue(coordinate, out VoxelChunk chunk))
				return;

			chunk.Revision++;
			result.Add((coordinate, chunk.Revision));
		}

		private void RaiseInvalidated(List<(ChunkCoordinate Coordinate, long Revision)> invalidated)
		{
			foreach ((ChunkCoordinate coordinate, long revision) in invalidated)
				ChunkInvalidated?.Invoke(coordinate, revision);
		}

		private readonly struct Vector3Int
		{
			public Vector3Int(int x, int y, int z)
			{
				X = x;
				Y = y;
				Z = z;
			}

			public int X { get; }
			public int Y { get; }
			public int Z { get; }
		}
	}
}
