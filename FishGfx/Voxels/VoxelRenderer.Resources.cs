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
		transparentIndexUploadJob?.Dispose();
		transparentIndexUploadJob = null;
		pendingTransparentOrderingResult?.Dispose();
		pendingTransparentOrderingResult = null;
		transparentSource?.ReleaseOwner();
		transparentSource = null;

		foreach (VoxelMeshData pending in pendingUploads)
		{
			pending.ReleasePooledVertexBuffers();
		}

		pendingUploads.Clear();
		currentUploadJob?.Dispose();
		currentUploadJob = null;
		completedEmptyChunks.Clear();

		foreach (GpuChunk chunk in gpuChunks.Values)
		{
			chunk.Dispose();
		}

		gpuChunks.Clear();
		gpuChunkColumns.Clear();
		orderedGpuChunks.Clear();
		activeGpuChunks.Clear();
		nextActiveGpuChunks.Clear();
		activeCoordinates.Clear();
		nextActiveCoordinates.Clear();
		transparentIndexRing.Dispose();
		transparentGeometry.Dispose();
		transparentGpuTimer.Dispose();
		gpuTimer.Dispose();
		indirectBuffer.Dispose();
		cutoutGeometry.Dispose();
		alphaShadowGeometry.Dispose();
		opaqueGeometry.Dispose();
		shadowAlphaShader.Dispose();
		shadowOpaqueShader.Dispose();
		waveShader.Dispose();
		voxelShader.Dispose();
		waveVertexShader.Dispose();
		vertexShader.Dispose();
		fragmentShader.Dispose();
		shadowAlphaFragmentShader.Dispose();
		shadowOpaqueFragmentShader.Dispose();
		shadowVertexShader.Dispose();
	}

	private void PublishUpload(VoxelUploadJob job)
	{
		VoxelMeshData result = job.Result;
		if (
			result.OpaqueVertexCount == 0
			&& result.CutoutVertexCount == 0
			&& result.AlphaShadowVertexCount == 0
			&& result.TransparentFaces.Length == 0
		)
		{
			RemoveGpuChunk(result.Coordinate);
			completedEmptyChunks[result.Coordinate] = new CompletedEmptyChunk(
				result.WorldGeneration,
				result.Revision,
				result.LightGeneration,
				result.LightRevision);
			return;
		}

		completedEmptyChunks.Remove(result.Coordinate);

		bool existed = gpuChunks.TryGetValue(result.Coordinate, out GpuChunk gpuChunk);
		if (!existed)
		{
			gpuChunk = new GpuChunk(result.Coordinate);
			gpuChunks.Add(result.Coordinate, gpuChunk);
			InsertOrderedGpuChunk(gpuChunk);
			AddToGpuColumn(gpuChunk);
		}

		bool previousShadowCaster = HasShadowCasterGeometry(gpuChunk);
		bool previousTransparentGeometry = HasTransparentGeometry(gpuChunk.Transparent);
		bool affectedActiveOrdering = activeCoordinates.Contains(result.Coordinate);
		AxisAlignedBoundingBox previousBounds = gpuChunk.Bounds;
		bool shadowGeometryChanged = !existed
			|| gpuChunk.WorldGeneration != result.WorldGeneration
			|| gpuChunk.Revision != result.Revision;

		opaqueVertices -= gpuChunk.Opaque?.VertexCount ?? 0;
		cutoutVertices -= gpuChunk.Cutout?.VertexCount ?? 0;

		gpuChunk.WorldGeneration = result.WorldGeneration;
		gpuChunk.Revision = result.Revision;
		gpuChunk.LightGeneration = result.LightGeneration;
		gpuChunk.LightRevision = result.LightRevision;
		gpuChunk.Bounds = result.Bounds;
		VoxelGeometryAllocation previousOpaque = gpuChunk.Opaque;
		VoxelGeometryAllocation previousCutout = gpuChunk.Cutout;
		VoxelGeometryAllocation previousAlphaShadow = gpuChunk.AlphaShadow;
		VoxelTransparentAllocation previousTransparent = gpuChunk.Transparent;
		gpuChunk.Opaque = job.Opaque;
		gpuChunk.Cutout = job.Cutout;
		gpuChunk.AlphaShadow = job.AlphaShadow;
		gpuChunk.Transparent = job.Transparent;
		job.DetachAllocations();
		previousOpaque?.ReleaseOwner();
		previousCutout?.ReleaseOwner();
		previousAlphaShadow?.ReleaseOwner();
		previousTransparent?.ReleaseOwner();
		opaqueVertices += gpuChunk.Opaque?.VertexCount ?? 0;
		cutoutVertices += gpuChunk.Cutout?.VertexCount ?? 0;
		bool currentTransparentGeometry = HasTransparentGeometry(gpuChunk.Transparent);
		if (shadowGeometryChanged
			&& (previousShadowCaster || HasShadowCasterGeometry(gpuChunk)))
		{
			shadowGeometryRevision++;
			EnqueueShadowInvalidation(previousBounds.Union(gpuChunk.Bounds));
		}
		if (affectedActiveOrdering
			&& (previousTransparentGeometry || currentTransparentGeometry))
		{
			transparentGeometryRevision++;
			transparentSourceDirty = true;
		}
		if (!existed)
			activeSetDirty = true;
	}

}
