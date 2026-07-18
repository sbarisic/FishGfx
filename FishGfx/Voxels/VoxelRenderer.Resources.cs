using System;
using FishGfx.Graphics;

namespace FishGfx.Voxels;

public sealed partial class VoxelRenderer : IDisposable
{
	public void Dispose()
	{
		if (disposed)
		{
			return;
		}

		disposed = true;
		world.ChunkRemoved -= OnChunkRemoved;
		scheduler.Dispose();
		transparentOrdering.Dispose();
		transparentSnapshot?.Dispose();
		transparentSnapshot = null;
		transparentSource?.ReleaseOwner();
		transparentSource = null;

		foreach (VoxelMeshData pending in pendingUploads)
		{
			pending.ReleasePooledVertexBuffers();
		}

		pendingUploads.Clear();

		foreach (GpuChunk chunk in gpuChunks.Values)
		{
			chunk.Dispose();
		}

		gpuChunks.Clear();
		orderedGpuChunks.Clear();
		activeGpuChunks.Clear();
		activeCoordinates.Clear();
		transparentIndexRing.Dispose();
		transparentGeometry.Dispose();
		transparentGpuTimer.Dispose();
		gpuTimer.Dispose();
		indirectBuffer.Dispose();
		cutoutGeometry.Dispose();
		opaqueGeometry.Dispose();
		waveShader.Dispose();
		voxelShader.Dispose();
		waveVertexShader.Dispose();
		vertexShader.Dispose();
		fragmentShader.Dispose();
	}

	private void Upload(VoxelMeshData result)
	{
		if (
			result.OpaqueVertexCount == 0
			&& result.CutoutVertexCount == 0
			&& result.TransparentFaces.Length == 0
		)
		{
			RemoveGpuChunk(result.Coordinate);
			return;
		}

		if (!gpuChunks.TryGetValue(result.Coordinate, out GpuChunk gpuChunk))
		{
			gpuChunk = new GpuChunk(result.Coordinate);
			gpuChunks.Add(result.Coordinate, gpuChunk);
			InsertOrderedGpuChunk(gpuChunk);
		}

		opaqueVertices -= gpuChunk.Opaque?.VertexCount ?? 0;
		cutoutVertices -= gpuChunk.Cutout?.VertexCount ?? 0;

		gpuChunk.Revision = result.Revision;
		gpuChunk.LightRevision = result.LightRevision;
		gpuChunk.Bounds = result.Bounds;
		gpuChunk.Opaque = opaqueGeometry.Update(
			gpuChunk.Opaque,
			result.OpaqueVertexSpan,
			result.Coordinate.WorldOrigin
		);
		gpuChunk.Cutout = cutoutGeometry.Update(
			gpuChunk.Cutout,
			result.CutoutVertexSpan,
			result.Coordinate.WorldOrigin
		);
		gpuChunk.Transparent = transparentGeometry.Update(
			gpuChunk.Transparent,
			result.TransparentFaces,
			result.Coordinate,
			result.Coordinate.WorldOrigin
		);
		opaqueVertices += gpuChunk.Opaque?.VertexCount ?? 0;
		cutoutVertices += gpuChunk.Cutout?.VertexCount ?? 0;
		transparentGeometryRevision++;
		transparentSourceDirty = true;
		activeSetDirty = true;
	}

}
