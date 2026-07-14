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
		transparentMesh.Dispose();
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
		gpuChunk.TransparentFaces = result.TransparentFaces;

		UpdateMesh(ref gpuChunk.Opaque, result.OpaqueVertexSpan);
		UpdateMesh(ref gpuChunk.Cutout, result.CutoutVertexSpan);
		opaqueVertices += gpuChunk.Opaque?.VertexCount ?? 0;
		cutoutVertices += gpuChunk.Cutout?.VertexCount ?? 0;
		transparentGeometryRevision++;
	}

	private void UpdateMesh(ref VoxelMesh mesh, ReadOnlySpan<VoxelVertex> vertices)
	{
		if (mesh?.IsRetained == true)
		{
			VoxelMesh previous = mesh;
			VoxelMesh replacement = null;

			try
			{
				if (vertices.Length > 0)
				{
					replacement = new VoxelMesh(
						Graphics,
						vertices.Length,
						BufferUsage.Dynamic
					);
					replacement.Update(vertices);
				}
			}
			catch
			{
				replacement?.Dispose();
				throw;
			}

			mesh = replacement;
			previous.Dispose();

			return;
		}

		if (mesh == null && vertices.Length > 0)
		{
			mesh = new VoxelMesh(
				Graphics,
				vertices.Length,
				BufferUsage.Dynamic
			);
		}

		mesh?.Update(vertices);
	}

}
