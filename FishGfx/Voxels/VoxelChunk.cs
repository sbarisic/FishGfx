using System;
using System.Collections.Generic;

namespace FishGfx.Voxels;

public sealed class VoxelChunk
{
	private const int MaximumStoredMaterialRuns = 1_024;
	private static readonly VoxelMaterialRun[] AirMaterialRuns =
	{
		new VoxelMaterialRun(0, checked((ushort)VoxelWorld.ChunkVolume)),
	};

	private VoxelCell[] cells = new VoxelCell[VoxelWorld.ChunkVolume];
	private VoxelMaterialRun[] materialRuns = AirMaterialRuns;

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
	internal static ReadOnlyMemory<VoxelMaterialRun> EmptyMaterialRuns => AirMaterialRuns;

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

	internal ReadOnlyMemory<VoxelMaterialRun> CaptureMaterialRunsUnchecked()
	{
		return materialRuns;
	}

	internal bool SetLocalUnchecked(int x, int y, int z, VoxelCell value)
	{
		int index = Index(x, y, z);
		VoxelCell[] current = System.Threading.Volatile.Read(ref cells);
		VoxelCell previous = current[index];

		if (previous == value)
		{
			return false;
		}

		VoxelCell[] replacement = (VoxelCell[])current.Clone();
		replacement[index] = value;
		System.Threading.Volatile.Write(ref cells, replacement);

		if (previous.IsAir && !value.IsAir)
		{
			NonAirCount++;
		}
		else if (!previous.IsAir && value.IsAir)
		{
			NonAirCount--;
		}

		materialRuns = NonAirCount == 0 ? AirMaterialRuns : null;

		return true;
	}

	internal void FillUnchecked(VoxelCell value)
	{
		VoxelCell[] replacement = new VoxelCell[VoxelWorld.ChunkVolume];
		if (!value.IsAir)
		{
			Array.Fill(replacement, value);
		}

		System.Threading.Volatile.Write(ref cells, replacement);
		NonAirCount = value.IsAir ? 0 : replacement.Length;
		materialRuns = value.IsAir
			? AirMaterialRuns
			: new[]
			{
				new VoxelMaterialRun(
					value.MaterialId,
					checked((ushort)VoxelWorld.ChunkVolume)
				),
			};
	}

	internal bool ReplaceUnchecked(ReadOnlySpan<VoxelCell> values)
	{
		VoxelCell[] current = System.Threading.Volatile.Read(ref cells);
		bool changed = false;
		int nonAirCount = 0;
		List<VoxelMaterialRun> runs = new List<VoxelMaterialRun>();
		ushort runMaterialId = values[0].MaterialId;
		int runLength = 0;
		bool retainRuns = true;

		for (int i = 0; i < current.Length; i++)
		{
			changed |= current[i] != values[i];
			ushort materialId = values[i].MaterialId;

			if (!values[i].IsAir)
			{
				nonAirCount++;
			}

			if (!retainRuns)
			{
				continue;
			}

			if (materialId == runMaterialId)
			{
				runLength++;
				continue;
			}

			retainRuns = TryAddMaterialRun(runs, runMaterialId, runLength);
			runMaterialId = materialId;
			runLength = 1;
		}

		if (!changed)
		{
			return false;
		}

		VoxelCell[] replacement = new VoxelCell[VoxelWorld.ChunkVolume];
		values.CopyTo(replacement);
		if (retainRuns)
		{
			retainRuns = TryAddMaterialRun(runs, runMaterialId, runLength);
		}

		System.Threading.Volatile.Write(ref cells, replacement);
		NonAirCount = nonAirCount;
		materialRuns = retainRuns ? runs.ToArray() : null;
		return true;
	}

	internal void AdoptPreparedUnchecked(
		VoxelCell[] preparedCells,
		VoxelMaterialRun[] preparedRuns,
		int preparedNonAirCount)
	{
		ArgumentNullException.ThrowIfNull(preparedCells);
		if (preparedCells.Length != VoxelWorld.ChunkVolume)
		{
			throw new ArgumentException("Prepared storage must contain one complete chunk.", nameof(preparedCells));
		}

		System.Threading.Volatile.Write(ref cells, preparedCells);
		NonAirCount = Math.Clamp(preparedNonAirCount, 0, preparedCells.Length);
		materialRuns = preparedRuns;
	}

	private static bool TryAddMaterialRun(
		List<VoxelMaterialRun> runs,
		ushort materialId,
		int length
	)
	{
		if (runs.Count + 1 >= MaximumStoredMaterialRuns)
		{
			runs.Clear();
			return false;
		}

		runs.Add(new VoxelMaterialRun(materialId, checked((ushort)length)));
		return true;
	}

	private static int Index(int x, int y, int z) => x + VoxelWorld.ChunkSize * (y + VoxelWorld.ChunkSize * z);

	private static void ValidateLocal(int x, int y, int z)
	{
		if ((uint)x >= VoxelWorld.ChunkSize)
		{
			throw new ArgumentOutOfRangeException(nameof(x));
		}

		if ((uint)y >= VoxelWorld.ChunkSize)
		{
			throw new ArgumentOutOfRangeException(nameof(y));
		}

		if ((uint)z >= VoxelWorld.ChunkSize)
		{
			throw new ArgumentOutOfRangeException(nameof(z));
		}
	}
}
