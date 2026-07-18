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
		ArgumentNullException.ThrowIfNull(queue);
		ArgumentNullException.ThrowIfNull(camera);

		float distance = maxRenderDistance ?? options.MaxRenderDistance;

		if (!float.IsFinite(distance) || distance <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(maxRenderDistance));
		}

		gpuTimer.Poll();
		long allocatedStart = GC.GetAllocatedBytesForCurrentThread();
		long cullingStart = Stopwatch.GetTimestamp();
		RefreshActiveSetIfNeeded(camera.Position, distance);
		ViewFrustum frustum = ViewFrustum.FromCamera(camera);
		float distanceSquared = distance * distance;
		Vector3 cameraForward = camera.WorldForwardNormal;
		visibleOpaque.Clear();
		visibleCutout.Clear();
		visibleTransparentChunks.Clear();
		visibleChunks = 0;
		ulong transparentSignature = 14695981039346656037UL;

		for (int chunkIndex = 0; chunkIndex < activeGpuChunks.Count; chunkIndex++)
		{
			GpuChunk chunk = activeGpuChunks[chunkIndex];

			if (chunk.Bounds.IsEmpty)
			{
				continue;
			}

			Vector3 origin = chunk.Coordinate.WorldOrigin;
			AxisAlignedBoundingBox worldBounds = chunk.Bounds.Translate(origin);
			Vector3 center = worldBounds.Center;

			if (cullingEnabled)
			{
				if (Vector3.DistanceSquared(camera.Position, center) > distanceSquared
					|| !frustum.Intersects(worldBounds))
				{
					continue;
				}
			}

			visibleChunks++;
			float depth = Vector3.Dot(center - camera.Position, cameraForward);

			if (chunk.Opaque?.VertexCount > 0)
			{
				visibleOpaque.Add(new VoxelPassEntry(chunk.Opaque, chunk.Coordinate, depth));
			}

			if (chunk.Cutout?.VertexCount > 0)
			{
				visibleCutout.Add(new VoxelPassEntry(chunk.Cutout, chunk.Coordinate, depth));
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
		double cullingMilliseconds = Stopwatch.GetElapsedTime(cullingStart).TotalMilliseconds;
		long commandStart = Stopwatch.GetTimestamp();
		int opaquePageGroups = 0;
		int cutoutPageGroups = 0;
		int indirectCommandCount = 0;
		int passSubmissions = 0;

		if (visibleOpaque.Count > 0 || visibleCutout.Count > 0)
		{
			DrawVoxelPagesCommand command = SubmitPageSnapshot(queue, camera);
			opaquePageGroups = command.OpaqueGroupCount;
			cutoutPageGroups = command.CutoutGroupCount;
			indirectCommandCount = command.OpaqueCommandCount + command.CutoutCommandCount;
			passSubmissions++;
		}

		double commandBuildMilliseconds = Stopwatch.GetElapsedTime(commandStart).TotalMilliseconds;
		VoxelTransparentInvalidationReason invalidationReason = GetTransparentInvalidationReason(
			transparentGeometryRevision,
			transparentSignature,
			camera.Position,
			cameraForward
		);
		bool transparentCacheHit = invalidationReason == VoxelTransparentInvalidationReason.None;
		double transparentBuildMilliseconds = 0;
		int transparentUploadBytes = 0;

		if (!transparentCacheHit)
		{
			long transparentStart = Stopwatch.GetTimestamp();
			transparentUploadBytes = BuildTransparentStream(camera);
			transparentBuildMilliseconds = Stopwatch.GetElapsedTime(transparentStart).TotalMilliseconds;
			transparentCacheKey = new VoxelTransparentCacheKey(
				transparentGeometryRevision,
				transparentSignature,
				camera.Position,
				cameraForward
			);
			hasTransparentCache = true;
		}

		if (transparentMesh.VertexCount > 0)
		{
			SubmitTransparentSnapshot(queue, camera);
			passSubmissions++;
		}

		int transparentDrawCalls = transparentMesh.VertexCount > 0 ? 1 : 0;
		int cullingAndCommandAllocatedBytes = checked(
			(int)(GC.GetAllocatedBytesForCurrentThread() - allocatedStart)
		);
		frameDiagnostics = new VoxelRendererFrameDiagnostics(
			cullingMilliseconds,
			commandBuildMilliseconds,
			lastSubmissionMilliseconds,
			gpuTimer.LastMilliseconds,
			transparentBuildMilliseconds,
			meshSchedulingMilliseconds,
			meshUploadMilliseconds,
			scheduledMeshes,
			uploadedMeshes,
			fastCompletedMeshes,
			candidateChunks,
			activeGpuChunks.Count,
			visibleChunks,
			inactiveCachedChunks,
			visibleOpaque.Count,
			visibleCutout.Count,
			transparentDrawCalls,
			opaquePageGroups + cutoutPageGroups + transparentDrawCalls,
			indirectCommandCount,
			opaquePageGroups + cutoutPageGroups,
			passSubmissions,
			transparentUploadBytes,
			cullingAndCommandAllocatedBytes,
			lastSubmissionAllocatedBytes,
			transparentCacheHit,
			invalidationReason
		);
	}

	private DrawVoxelPagesCommand SubmitPageSnapshot(RenderQueue queue, Camera camera)
	{
		DrawVoxelPagesCommand command = new(
			atlasTexture,
			voxelShader,
			opaqueState,
			sun,
			options.AlphaCutoff,
			fog,
			indirectBuffer,
			gpuTimer,
			visibleOpaque,
			visibleCutout,
			this
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
				sortKey: 0,
				tag: this
			);
			return command;
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

	private VoxelTransparentInvalidationReason GetTransparentInvalidationReason(
		long geometryRevision,
		ulong visibleSignature,
		Vector3 cameraPosition,
		Vector3 cameraForward
	)
	{
		return VoxelTransparentCachePolicy.Evaluate(
			hasTransparentCache,
			transparentCacheKey,
			geometryRevision,
			visibleSignature,
			cameraPosition,
			cameraForward,
			options.TransparentResortDistance,
			options.TransparentResortAngleDegrees
		);
	}
}
