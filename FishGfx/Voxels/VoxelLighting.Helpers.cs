using System;
using System.Collections.Generic;

namespace FishGfx.Voxels;

public sealed partial class VoxelLighting
{
	private static void Enqueue(RebuildTransaction rebuild, WorkingChunk chunk, int index)
	{
		if (IsCellMarked(chunk.QueuedWords, index))
		{
			return;
		}

		MarkCell(chunk.QueuedWords, index);
		rebuild.Propagation.Enqueue(new CellAddress(chunk.Coordinate, index));
	}

	private static bool IsCellMarked(ulong[] words, int index)
	{
		return (words[index >> 6] & (1UL << (index & 63))) != 0;
	}

	private static void MarkCell(ulong[] words, int index)
	{
		words[index >> 6] |= 1UL << (index & 63);
	}

	private static void ClearCell(ulong[] words, int index)
	{
		words[index >> 6] &= ~(1UL << (index & 63));
	}

	private ushort[] GetUniformMaterialSignatures(ushort signature)
	{
		if (uniformMaterialSignatures.TryGetValue(signature, out ushort[] signatures))
		{
			return signatures;
		}

		signatures = new ushort[VoxelWorld.ChunkVolume];
		if (signature != 0)
		{
			Array.Fill(signatures, signature);
		}

		uniformMaterialSignatures.Add(signature, signatures);
		return signatures;
	}

	private static ushort[] CreateMaterialSignatureLookup(VoxelPalette palette)
	{
		ushort[] signatures = new ushort[palette.Count];
		for (int index = 1; index < signatures.Length; index++)
		{
			VoxelMaterialLightSettings light = palette[(ushort)index].Light;
			signatures[index] = (ushort)(
				light.Opacity
				| (light.Emission.Red << 4)
				| (light.Emission.Green << 8)
				| (light.Emission.Blue << 12)
			);
		}

		return signatures;
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
		{
			for (int offsetY = minY; offsetY <= maxY; offsetY++)
			{
				for (int offsetX = minX; offsetX <= maxX; offsetX++)
				{
					if (offsetX == 0 && offsetY == 0 && offsetZ == 0)
					{
						continue;
					}

					targets.Add(coordinate + new ChunkCoordinate(offsetX, offsetY, offsetZ));
				}
			}
		}
	}

	private static void AddSkyHaloTargets(
		ChunkCoordinate coordinate,
		HashSet<ChunkCoordinate> targets
	)
	{
		for (int offsetZ = -1; offsetZ <= 1; offsetZ++)
		{
			for (int offsetX = -1; offsetX <= 1; offsetX++)
			{
				targets.Add(coordinate + new ChunkCoordinate(offsetX, 0, offsetZ));
			}
		}
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
		{
			return comparison;
		}

		comparison = left.Y.CompareTo(right.Y);
		return comparison != 0 ? comparison : left.Z.CompareTo(right.Z);
	}

	private void ThrowIfDisposed()
	{
		if (disposed)
		{
			throw new ObjectDisposedException(nameof(VoxelLighting));
		}
	}

}
