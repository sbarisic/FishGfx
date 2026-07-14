using System;
using System.Buffers;
using System.Linq;

namespace FishGfx.Voxels;

public sealed class VoxelMeshData
{
	private readonly VoxelVertex[] opaqueVertices;
	private readonly VoxelVertex[] cutoutVertices;
	private readonly int opaqueVertexCount;
	private readonly int cutoutVertexCount;
	private readonly bool pooledVertexBuffers;
	private bool pooledBuffersReleased;

	internal VoxelMeshData(
		ChunkCoordinate coordinate,
		long worldGeneration,
		long revision,
		long lightGeneration,
		long lightRevision,
		VoxelVertex[] opaqueVertices,
		VoxelVertex[] cutoutVertices,
		VoxelTransparentFace[] transparentFaces,
		AxisAlignedBoundingBox bounds
	)
	{
		Coordinate = coordinate;
		WorldGeneration = worldGeneration;
		Revision = revision;
		LightGeneration = lightGeneration;
		LightRevision = lightRevision;
		this.opaqueVertices = opaqueVertices;
		this.cutoutVertices = cutoutVertices;
		opaqueVertexCount = opaqueVertices.Length;
		cutoutVertexCount = cutoutVertices.Length;
		TransparentFaces = transparentFaces;
		Bounds = bounds;
	}

	internal VoxelMeshData(
		ChunkCoordinate coordinate,
		long worldGeneration,
		long revision,
		long lightGeneration,
		long lightRevision,
		VoxelVertex[] opaqueVertices,
		int opaqueVertexCount,
		VoxelVertex[] cutoutVertices,
		int cutoutVertexCount,
		VoxelTransparentFace[] transparentFaces,
		AxisAlignedBoundingBox bounds
	)
	{
		Coordinate = coordinate;
		WorldGeneration = worldGeneration;
		Revision = revision;
		LightGeneration = lightGeneration;
		LightRevision = lightRevision;
		this.opaqueVertices = opaqueVertices;
		this.cutoutVertices = cutoutVertices;
		this.opaqueVertexCount = opaqueVertexCount;
		this.cutoutVertexCount = cutoutVertexCount;
		pooledVertexBuffers = true;
		TransparentFaces = transparentFaces;
		Bounds = bounds;
	}

	public ChunkCoordinate Coordinate { get; }

	internal long WorldGeneration { get; }

	public long Revision { get; }

	internal long LightGeneration { get; }

	public long LightRevision { get; }

	public VoxelVertex[] OpaqueVertices => GetPublicVertices(
		opaqueVertices,
		opaqueVertexCount
	);

	public VoxelVertex[] CutoutVertices => GetPublicVertices(
		cutoutVertices,
		cutoutVertexCount
	);

	public VoxelTransparentFace[] TransparentFaces { get; }

	public AxisAlignedBoundingBox Bounds { get; }

	public int TransparentVertexCount => TransparentFaces.Sum(face => face.Vertices.Count);

	internal int OpaqueVertexCount => opaqueVertexCount;
	internal int CutoutVertexCount => cutoutVertexCount;
	internal ReadOnlySpan<VoxelVertex> OpaqueVertexSpan =>
		opaqueVertices.AsSpan(0, opaqueVertexCount);
	internal ReadOnlySpan<VoxelVertex> CutoutVertexSpan =>
		cutoutVertices.AsSpan(0, cutoutVertexCount);

	internal void ReleasePooledVertexBuffers()
	{
		if (!pooledVertexBuffers || pooledBuffersReleased)
		{
			return;
		}

		pooledBuffersReleased = true;

		if (opaqueVertices.Length > 0)
		{
			ArrayPool<VoxelVertex>.Shared.Return(opaqueVertices);
		}

		if (cutoutVertices.Length > 0)
		{
			ArrayPool<VoxelVertex>.Shared.Return(cutoutVertices);
		}
	}

	private VoxelVertex[] GetPublicVertices(
		VoxelVertex[] vertices,
		int count
	)
	{
		if (pooledBuffersReleased)
		{
			throw new ObjectDisposedException(nameof(VoxelMeshData));
		}

		return pooledVertexBuffers
			? vertices.AsSpan(0, count).ToArray()
			: vertices;
	}
}
