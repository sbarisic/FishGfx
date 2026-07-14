using System;

namespace FishGfx.Voxels;

public sealed partial class VoxelLighting
{
	private static void PropagateNextCell(RebuildTransaction rebuild)
	{
		CellAddress sourceAddress = rebuild.Propagation.Dequeue();
		WorkingChunk sourceChunk = rebuild.Lookup[sourceAddress.Coordinate];
		ClearCell(sourceChunk.QueuedWords, sourceAddress.Index);
		ushort source = sourceChunk.Lights[sourceAddress.Index];
		GetLocalCoordinates(sourceAddress.Index, out int sourceX, out int sourceY, out int sourceZ);

		if (sourceX > 0)
		{
			PropagateFullToCell(rebuild, sourceChunk, sourceAddress.Index - 1, source);
		}
		else
		{
			PropagateFullToBoundary(
				rebuild,
				sourceAddress.Coordinate + new ChunkCoordinate(-1, 0, 0),
				Index(VoxelWorld.ChunkSize - 1, sourceY, sourceZ),
				source
			);
		}

		if (sourceX < VoxelWorld.ChunkSize - 1)
		{
			PropagateFullToCell(rebuild, sourceChunk, sourceAddress.Index + 1, source);
		}
		else
		{
			PropagateFullToBoundary(
				rebuild,
				sourceAddress.Coordinate + new ChunkCoordinate(1, 0, 0),
				Index(0, sourceY, sourceZ),
				source
			);
		}

		if (sourceY > 0)
		{
			PropagateFullToCell(
				rebuild,
				sourceChunk,
				sourceAddress.Index - VoxelWorld.ChunkSize,
				source
			);
		}
		else
		{
			PropagateFullToBoundary(
				rebuild,
				sourceAddress.Coordinate + new ChunkCoordinate(0, -1, 0),
				Index(sourceX, VoxelWorld.ChunkSize - 1, sourceZ),
				source
			);
		}

		if (sourceY < VoxelWorld.ChunkSize - 1)
		{
			PropagateFullToCell(
				rebuild,
				sourceChunk,
				sourceAddress.Index + VoxelWorld.ChunkSize,
				source
			);
		}
		else
		{
			PropagateFullToBoundary(
				rebuild,
				sourceAddress.Coordinate + new ChunkCoordinate(0, 1, 0),
				Index(sourceX, 0, sourceZ),
				source
			);
		}

		int zStride = VoxelWorld.ChunkSize * VoxelWorld.ChunkSize;
		if (sourceZ > 0)
		{
			PropagateFullToCell(rebuild, sourceChunk, sourceAddress.Index - zStride, source);
		}
		else
		{
			PropagateFullToBoundary(
				rebuild,
				sourceAddress.Coordinate + new ChunkCoordinate(0, 0, -1),
				Index(sourceX, sourceY, VoxelWorld.ChunkSize - 1),
				source
			);
		}

		if (sourceZ < VoxelWorld.ChunkSize - 1)
		{
			PropagateFullToCell(rebuild, sourceChunk, sourceAddress.Index + zStride, source);
		}
		else
		{
			PropagateFullToBoundary(
				rebuild,
				sourceAddress.Coordinate + new ChunkCoordinate(0, 0, 1),
				Index(sourceX, sourceY, 0),
				source
			);
		}
	}

	private static void PropagateFullToBoundary(
		RebuildTransaction rebuild,
		ChunkCoordinate coordinate,
		int index,
		ushort source
	)
	{
		if (rebuild.Lookup.TryGetValue(coordinate, out WorkingChunk target))
		{
			PropagateFullToCell(rebuild, target, index, source);
		}
	}

	private static void PropagateFullToCell(
		RebuildTransaction rebuild,
		WorkingChunk target,
		int index,
		ushort source
	)
	{
		byte opacity = GetOpacity(target.MaterialSignatures[index]);
		byte loss = Math.Max((byte)1, opacity);
		ushort current = target.Lights[index];
		byte red = Math.Max((byte)(current & 0xf), Subtract((byte)(source & 0xf), loss));
		byte green = Math.Max(
			(byte)((current >> 4) & 0xf),
			Subtract((byte)((source >> 4) & 0xf), loss)
		);
		byte blue = Math.Max(
			(byte)((current >> 8) & 0xf),
			Subtract((byte)((source >> 8) & 0xf), loss)
		);
		byte sky = Math.Max(
			(byte)((current >> 12) & 0xf),
			Subtract((byte)((source >> 12) & 0xf), loss)
		);
		ushort propagated = VoxelLight.Pack(red, green, blue, sky);
		if (propagated == current)
		{
			return;
		}

		target.Lights[index] = propagated;
		Enqueue(rebuild, target, index);
	}
}
