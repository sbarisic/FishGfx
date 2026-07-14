using System;

namespace FishGfx.Voxels;

public sealed partial class VoxelLighting
{
	private IncrementalWorkingChunk EnsureIncrementalWorking(
		IncrementalTransaction incremental,
		ChunkCoordinate coordinate
	)
	{
		return incremental.Sources.TryGetValue(coordinate, out IncrementalSourceChunk source)
			? EnsureIncrementalWorking(incremental, source)
			: null;
	}

	private IncrementalWorkingChunk EnsureIncrementalWorking(
		IncrementalTransaction incremental,
		IncrementalSourceChunk source
	)
	{
		if (source.Working != null)
		{
			if (incremental.InvalidatedSourceCoordinates.Contains(source.Resident.Coordinate))
			{
				incremental.DiscardAtCommit = true;
			}

			return source.Working;
		}
		if (source.PublishedLights == null
			|| source.MaterialSignatures == null)
		{
			return null;
		}

		IncrementalWorkingChunk working = new IncrementalWorkingChunk(
			source.Resident,
			source.PublishedSkyExposedAbove,
			(ushort[])source.MaterialSignatures.Clone(),
			(ushort[])source.PublishedLights.Clone(),
			(byte[])source.PublishedDirectSky.Clone(),
			isNew: false
		);
		source.Working = working;
		incremental.Chunks.Add(source.Resident.Coordinate, working);
		if (incremental.InvalidatedSourceCoordinates.Contains(source.Resident.Coordinate))
		{
			incremental.DiscardAtCommit = true;
		}

		return working;
	}

	private void EnqueueIncrementalWithNeighbors(
		IncrementalTransaction incremental,
		ChunkCoordinate coordinate,
		int index
	)
	{
		if (!incremental.Sources.TryGetValue(coordinate, out IncrementalSourceChunk center)
			|| center.CurrentLights == null)
		{
			return;
		}

		EnqueueIncremental(incremental, center, index);
		EnqueueIncrementalNeighbors(incremental, center, index);
	}

	private void EnqueueAt(
		IncrementalTransaction incremental,
		ChunkCoordinate coordinate,
		int x,
		int y,
		int z
	)
	{
		if (incremental.Sources.TryGetValue(coordinate, out IncrementalSourceChunk source)
			&& source.CurrentLights != null)
		{
			EnqueueIncremental(incremental, source, Index(x, y, z));
		}
	}

	private static void EnqueueIncremental(
		IncrementalTransaction incremental,
		IncrementalSourceChunk source,
		int index
	)
	{
		if (!source.TryMarkQueued(index))
		{
			return;
		}

		incremental.Relaxation.Enqueue(new IncrementalCellAddress(source, index));
	}

	private static void EnqueueIncrementalNeighbors(
		IncrementalTransaction incremental,
		IncrementalSourceChunk source,
		int index
	)
	{
		GetLocalCoordinates(index, out int x, out int y, out int z);

		if (x > 0)
		{
			EnqueueIncremental(incremental, source, index - 1);
		}
		else
		{
			EnqueueIncrementalBoundary(
				incremental,
				source.Resident.Coordinate + new ChunkCoordinate(-1, 0, 0),
				Index(VoxelWorld.ChunkSize - 1, y, z)
			);
		}

		if (x < VoxelWorld.ChunkSize - 1)
		{
			EnqueueIncremental(incremental, source, index + 1);
		}
		else
		{
			EnqueueIncrementalBoundary(
				incremental,
				source.Resident.Coordinate + new ChunkCoordinate(1, 0, 0),
				Index(0, y, z)
			);
		}

		if (y > 0)
		{
			EnqueueIncremental(incremental, source, index - VoxelWorld.ChunkSize);
		}
		else
		{
			EnqueueIncrementalBoundary(
				incremental,
				source.Resident.Coordinate + new ChunkCoordinate(0, -1, 0),
				Index(x, VoxelWorld.ChunkSize - 1, z)
			);
		}

		if (y < VoxelWorld.ChunkSize - 1)
		{
			EnqueueIncremental(incremental, source, index + VoxelWorld.ChunkSize);
		}
		else
		{
			EnqueueIncrementalBoundary(
				incremental,
				source.Resident.Coordinate + new ChunkCoordinate(0, 1, 0),
				Index(x, 0, z)
			);
		}

		int zStride = VoxelWorld.ChunkSize * VoxelWorld.ChunkSize;
		if (z > 0)
		{
			EnqueueIncremental(incremental, source, index - zStride);
		}
		else
		{
			EnqueueIncrementalBoundary(
				incremental,
				source.Resident.Coordinate + new ChunkCoordinate(0, 0, -1),
				Index(x, y, VoxelWorld.ChunkSize - 1)
			);
		}

		if (z < VoxelWorld.ChunkSize - 1)
		{
			EnqueueIncremental(incremental, source, index + zStride);
		}
		else
		{
			EnqueueIncrementalBoundary(
				incremental,
				source.Resident.Coordinate + new ChunkCoordinate(0, 0, 1),
				Index(x, y, 0)
			);
		}
	}

	private static void EnqueueIncrementalBoundary(
		IncrementalTransaction incremental,
		ChunkCoordinate coordinate,
		int index
	)
	{
		if (incremental.Sources.TryGetValue(coordinate, out IncrementalSourceChunk source)
			&& source.CurrentLights != null)
		{
			EnqueueIncremental(incremental, source, index);
		}
	}

	private void RelaxNextCell(IncrementalTransaction incremental)
	{
		IncrementalCellAddress address = incremental.Relaxation.Dequeue();
		IncrementalSourceChunk targetSource = address.Source;
		targetSource.ClearQueued(address.Index);
		if (targetSource.CurrentLights == null || targetSource.CurrentMaterialSignatures == null)
		{
			return;
		}

		ReferenceIncrementalSource(incremental, targetSource);
		GetLocalCoordinates(address.Index, out int targetX, out int targetY, out int targetZ);
		ushort signature = targetSource.CurrentMaterialSignatures[address.Index];
		byte opacity = GetOpacity(signature);
		byte red = GetEmissionRed(signature);
		byte green = GetEmissionGreen(signature);
		byte blue = GetEmissionBlue(signature);
		byte sky = targetSource.CurrentDirectSky[address.Index];
		byte ordinaryLoss = Math.Max((byte)1, opacity);
		ushort[] localLights = targetSource.CurrentLights;
		ChunkCoordinate coordinate = targetSource.Resident.Coordinate;

		AccumulateIncrementalNeighbor(
			incremental,
			targetSource,
			targetX > 0 ? address.Index - 1 : Index(VoxelWorld.ChunkSize - 1, targetY, targetZ),
			targetX > 0 ? coordinate : coordinate + new ChunkCoordinate(-1, 0, 0),
			ordinaryLoss,
			ref red,
			ref green,
			ref blue,
			ref sky
		);
		AccumulateIncrementalNeighbor(
			incremental,
			targetSource,
			targetX < VoxelWorld.ChunkSize - 1 ? address.Index + 1 : Index(0, targetY, targetZ),
			targetX < VoxelWorld.ChunkSize - 1 ? coordinate : coordinate + new ChunkCoordinate(1, 0, 0),
			ordinaryLoss,
			ref red,
			ref green,
			ref blue,
			ref sky
		);
		AccumulateIncrementalNeighbor(
			incremental,
			targetSource,
			targetY > 0 ? address.Index - VoxelWorld.ChunkSize : Index(targetX, VoxelWorld.ChunkSize - 1, targetZ),
			targetY > 0 ? coordinate : coordinate + new ChunkCoordinate(0, -1, 0),
			ordinaryLoss,
			ref red,
			ref green,
			ref blue,
			ref sky
		);
		AccumulateIncrementalNeighbor(
			incremental,
			targetSource,
			targetY < VoxelWorld.ChunkSize - 1 ? address.Index + VoxelWorld.ChunkSize : Index(targetX, 0, targetZ),
			targetY < VoxelWorld.ChunkSize - 1 ? coordinate : coordinate + new ChunkCoordinate(0, 1, 0),
			ordinaryLoss,
			ref red,
			ref green,
			ref blue,
			ref sky
		);
		int zStride = VoxelWorld.ChunkSize * VoxelWorld.ChunkSize;
		AccumulateIncrementalNeighbor(
			incremental,
			targetSource,
			targetZ > 0 ? address.Index - zStride : Index(targetX, targetY, VoxelWorld.ChunkSize - 1),
			targetZ > 0 ? coordinate : coordinate + new ChunkCoordinate(0, 0, -1),
			ordinaryLoss,
			ref red,
			ref green,
			ref blue,
			ref sky
		);
		AccumulateIncrementalNeighbor(
			incremental,
			targetSource,
			targetZ < VoxelWorld.ChunkSize - 1 ? address.Index + zStride : Index(targetX, targetY, 0),
			targetZ < VoxelWorld.ChunkSize - 1 ? coordinate : coordinate + new ChunkCoordinate(0, 0, 1),
			ordinaryLoss,
			ref red,
			ref green,
			ref blue,
			ref sky
		);

		ushort relaxed = VoxelLight.Pack(red, green, blue, sky);
		if (localLights[address.Index] == relaxed)
		{
			return;
		}

		IncrementalWorkingChunk target = EnsureIncrementalWorking(incremental, targetSource);
		if (target == null)
		{
			return;
		}

		target.Lights[address.Index] = relaxed;
		bool isBoundary = targetX == 0
			|| targetX == VoxelWorld.ChunkSize - 1
			|| targetY == 0
			|| targetY == VoxelWorld.ChunkSize - 1
			|| targetZ == 0
			|| targetZ == VoxelWorld.ChunkSize - 1;
		target.MarkModified(address.Index, isBoundary);
		EnqueueIncrementalNeighbors(incremental, targetSource, address.Index);
	}

	private void AccumulateIncrementalNeighbor(
		IncrementalTransaction incremental,
		IncrementalSourceChunk localSource,
		int index,
		ChunkCoordinate coordinate,
		byte loss,
		ref byte red,
		ref byte green,
		ref byte blue,
		ref byte sky
	)
	{
		IncrementalSourceChunk source;
		if (coordinate == localSource.Resident.Coordinate)
		{
			source = localSource;
		}
		else if (!incremental.Sources.TryGetValue(coordinate, out source))
		{
			return;
		}

		if (source.CurrentLights == null)
		{
			return;
		}

		ReferenceIncrementalSource(incremental, source);
		ushort light = source.CurrentLights[index];
		red = Math.Max(red, Subtract((byte)(light & 0xf), loss));
		green = Math.Max(green, Subtract((byte)((light >> 4) & 0xf), loss));
		blue = Math.Max(blue, Subtract((byte)((light >> 8) & 0xf), loss));
		sky = Math.Max(sky, Subtract((byte)((light >> 12) & 0xf), loss));
	}

	private static void ReferenceIncrementalSource(
		IncrementalTransaction incremental,
		IncrementalSourceChunk source
	)
	{
		if (source.Working != null || source.IsReferenced)
		{
			return;
		}

		ChunkCoordinate coordinate = source.Resident.Coordinate;
		source.IsReferenced = true;
		incremental.ReferencedSourceCoordinates.Add(coordinate);
		if (incremental.InvalidatedSourceCoordinates.Contains(coordinate))
		{
			incremental.DiscardAtCommit = true;
		}
	}

}
