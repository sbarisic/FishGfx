using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using FishGfx.Graphics;

namespace FishGfx.Voxels;

public sealed partial class VoxelRenderer : IDisposable
{
	public void EnqueueVisible(RenderQueue queue, Camera camera, float? maxRenderDistance = null)
	{
		ThrowIfDisposed();

		if (queue == null)
		{
			throw new ArgumentNullException(nameof(queue));
		}

		if (camera == null)
		{
			throw new ArgumentNullException(nameof(camera));
		}

		float distance = maxRenderDistance ?? options.MaxRenderDistance;

		if (!float.IsFinite(distance) || distance <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(maxRenderDistance));
		}

		long cullingStart = Stopwatch.GetTimestamp();
		ViewFrustum frustum = ViewFrustum.FromCamera(camera);
		float distanceSquared = distance * distance;
		Vector3 cameraForward = camera.WorldForwardNormal;
		visibleOpaque.Clear();
		visibleCutout.Clear();
		visibleTransparentChunks.Clear();
		visibleChunks = 0;
		ulong transparentSignature = 14695981039346656037UL;

		for (int chunkIndex = 0; chunkIndex < orderedGpuChunks.Count; chunkIndex++)
		{
			GpuChunk chunk = orderedGpuChunks[chunkIndex];

			if (chunk.Bounds.IsEmpty)
			{
				continue;
			}

			Vector3 origin = chunk.Coordinate.WorldOrigin;
			AxisAlignedBoundingBox worldBounds = chunk.Bounds.Translate(origin);
			Vector3 center = worldBounds.Center;

			if (cullingEnabled)
			{
				if (Vector3.DistanceSquared(camera.Position, center) > distanceSquared)
				{
					continue;
				}

				if (!frustum.Intersects(worldBounds))
				{
					continue;
				}
			}

			visibleChunks++;
			Matrix4x4 model = Matrix4x4.CreateTranslation(origin);
			float depth = Vector3.Dot(center - camera.Position, cameraForward);

			if (chunk.Opaque?.VertexCount > 0)
			{
				visibleOpaque.Add(new VoxelPassEntry(chunk.Opaque, model, chunk.Coordinate, depth));
			}

			if (chunk.Cutout?.VertexCount > 0)
			{
				visibleCutout.Add(new VoxelPassEntry(chunk.Cutout, model, chunk.Coordinate, depth));
			}

			if (chunk.TransparentFaces.Length > 0)
			{
				visibleTransparentChunks.Add(chunk);
				transparentSignature = AddSignature(transparentSignature, chunk.Coordinate);
				transparentSignature = AddSignature(transparentSignature, chunk.Revision);
			}
		}

		visibleOpaque.Sort(ComparePassEntries);
		visibleCutout.Sort(ComparePassEntries);

		int passSubmissions = 0;

		if (visibleOpaque.Count > 0)
		{
			SubmitPassSnapshot(
				queue,
				camera,
				visibleOpaque,
				alphaCutoff: -1,
				sortKey: 0
			);
			passSubmissions++;
		}

		if (visibleCutout.Count > 0)
		{
			SubmitPassSnapshot(
				queue,
				camera,
				visibleCutout,
				options.AlphaCutoff,
				sortKey: 1
			);
			passSubmissions++;
		}

		double cullingMilliseconds = Stopwatch.GetElapsedTime(cullingStart).TotalMilliseconds;
		VoxelTransparentCacheKey currentCacheKey = new VoxelTransparentCacheKey(
			transparentGeometryRevision,
			transparentSignature,
			camera.View
		);
		bool transparentCacheHit = hasTransparentCache && transparentCacheKey.Equals(currentCacheKey);
		double transparentBuildMilliseconds = 0;
		int transparentUploadBytes = 0;

		if (!transparentCacheHit)
		{
			long transparentStart = Stopwatch.GetTimestamp();
			transparentUploadBytes = BuildTransparentStream(camera);
			transparentBuildMilliseconds = Stopwatch.GetElapsedTime(transparentStart).TotalMilliseconds;
			transparentCacheKey = currentCacheKey;
			hasTransparentCache = true;
		}

		if (transparentMesh.VertexCount > 0)
		{
			SubmitTransparentSnapshot(queue, camera);
			passSubmissions++;
		}

		int transparentDrawCalls = transparentMesh.VertexCount > 0 ? 1 : 0;
		frameDiagnostics = new VoxelRendererFrameDiagnostics(
			cullingMilliseconds,
			transparentBuildMilliseconds,
			meshSchedulingMilliseconds,
			meshUploadMilliseconds,
			scheduledMeshes,
			uploadedMeshes,
			fastCompletedMeshes,
			visibleOpaque.Count,
			visibleCutout.Count,
			transparentDrawCalls,
			passSubmissions,
			passSubmissions,
			passSubmissions,
			transparentUploadBytes,
			transparentCacheHit
		);
	}

	private void SubmitPassSnapshot(
		RenderQueue queue,
		Camera camera,
		IReadOnlyList<VoxelPassEntry> entries,
		float alphaCutoff,
		ulong sortKey
	)
	{
		DrawVoxelPassCommand command = new(
			atlasTexture,
			voxelShader,
			opaqueState,
			sun,
			alphaCutoff,
			fog,
			entries
		);

		try
		{
			RenderCommandBatch batch = new(new RenderCommand[] { command });
			queue.SubmitRetained(
				RenderQueueBucket.Opaque,
				batch,
				Matrix4x4.Identity,
				command,
				camera.Position,
				sortKey: sortKey,
				tag: this
			);
		}
		catch
		{
			command.Dispose();
			throw;
		}
	}

	private void SubmitTransparentSnapshot(RenderQueue queue, Camera camera)
	{
		DrawVoxelMeshCommand command = new(
			transparentMesh,
			atlasTexture,
			waveShader,
			transparentState,
			sun,
			alphaCutoff: -1,
			fog
		);

		try
		{
			RenderCommandBatch batch = new(new RenderCommand[] { command });
			queue.SubmitRetained(
				RenderQueueBucket.Transparent,
				batch,
				Matrix4x4.Identity,
				command,
				camera.Position,
				sortKey: 2,
				tag: this
			);
		}
		catch
		{
			command.Dispose();
			throw;
		}
	}

}
