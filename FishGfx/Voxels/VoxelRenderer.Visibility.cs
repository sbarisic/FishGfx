using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using FishGfx.Graphics;
using FishGfx.Graphics.Shadows;

namespace FishGfx.Voxels;

public sealed partial class VoxelRenderer : IDisposable
{
	public void EnqueueVisible(RenderQueue queue, Camera camera, float? maxRenderDistance = null)
	{
		EnqueueVisible(queue, camera, shadows: null, maxRenderDistance);
	}

	public void EnqueueVisible(
		RenderQueue queue,
		Camera camera,
		DirectionalShadowFrame? shadows,
		float? maxRenderDistance = null)
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
		RefreshActiveSetIfNeeded(camera.Position, distance);
		UpdateTransparentOrdering(camera, distance);
		long allocatedStart = GC.GetAllocatedBytesForCurrentThread();
		long cullingStart = Stopwatch.GetTimestamp();
		ViewFrustum frustum = ViewFrustum.FromCamera(camera);
		float distanceSquared = distance * distance;
		Vector3 cameraForward = camera.WorldForwardNormal;
		visibleOpaque.Clear();
		visibleCutout.Clear();
		visibleChunks = 0;

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
			DrawVoxelPagesCommand command = SubmitPageSnapshot(queue, camera, shadows);
			opaquePageGroups = command.OpaqueGroupCount;
			cutoutPageGroups = command.CutoutGroupCount;
			indirectCommandCount = command.OpaqueCommandCount + command.CutoutCommandCount;
			passSubmissions++;
		}

		double commandBuildMilliseconds = Stopwatch.GetElapsedTime(commandStart).TotalMilliseconds;
		bool transparentHasPendingRequest = transparentOrdering.HasPending;
		bool transparentWorkerRunning = transparentOrdering.IsRunning;
		bool transparentPending = transparentHasPendingRequest || transparentWorkerRunning;
		bool transparentCacheHit = !transparentPending
			&& transparentSourceBuildMilliseconds == 0
			&& transparentIndexUploadMilliseconds == 0
			&& GetTransparentInvalidationReason(
				transparentGeometryRevision,
				activeSetGeneration,
				camera.Position,
				cameraForward
			) == VoxelTransparentInvalidationReason.None;

		if (transparentSnapshot?.IndexCount > 0)
		{
			SubmitTransparentSnapshot(queue, camera, shadows);
			passSubmissions++;
		}

		int transparentDrawCalls = transparentSnapshot?.IndexCount > 0 ? 1 : 0;
		int cullingAndCommandAllocatedBytes = checked(
			(int)(GC.GetAllocatedBytesForCurrentThread() - allocatedStart)
		);
		double transparentOrderingAgeSeconds = transparentSnapshot == null
			? 0
			: Stopwatch.GetElapsedTime(transparentSnapshot.CompletedTimestamp).TotalSeconds;
		float transparentCameraDistanceDelta = transparentSnapshot == null
			? 0
			: Vector3.Distance(transparentSnapshot.CameraPosition, camera.Position);
		float transparentCameraAngleDelta = transparentSnapshot == null
			? 0
			: GetAngleDegrees(transparentSnapshot.CameraForward, cameraForward);
		frameDiagnostics = new VoxelRendererFrameDiagnostics(
			cullingMilliseconds,
			commandBuildMilliseconds,
			lastSubmissionMilliseconds,
			gpuTimer.LastMilliseconds,
			transparentSourceBuildMilliseconds
				+ transparentResultApplyMilliseconds,
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
			transparentIndexUploadBytes,
			cullingAndCommandAllocatedBytes,
			lastSubmissionAllocatedBytes,
			transparentCacheHit,
			transparentPending
				? transparentLastRequestReason
				: VoxelTransparentInvalidationReason.None,
			transparentSnapshot?.FaceCount ?? 0,
			transparentSnapshot?.IndexCount ?? 0,
			transparentSourceBuildMilliseconds,
			transparentSortMilliseconds,
			transparentResultApplyMilliseconds,
			transparentIndexUploadMilliseconds,
			transparentGpuTimer.LastMilliseconds,
			transparentMainThreadAllocatedBytes,
			transparentWorkerAllocatedBytes,
			transparentHasPendingRequest,
			transparentWorkerRunning,
			transparentOrdering.CoalescedRequests,
			transparentStaleResults,
			transparentOrdering.DroppedResults,
			transparentSnapshot?.GeometryRevision ?? 0,
			transparentOrderingAgeSeconds,
			transparentCameraDistanceDelta,
			transparentCameraAngleDelta,
			transparentSnapshot?.Reason ?? VoxelTransparentInvalidationReason.None
		);
		ResetTransparentFrameWorkDiagnostics();
	}

	private DrawVoxelPagesCommand SubmitPageSnapshot(
		RenderQueue queue,
		Camera camera,
		DirectionalShadowFrame? shadows)
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
			this,
			shadows
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

	private void SubmitTransparentSnapshot(
		RenderQueue queue,
		Camera camera,
		DirectionalShadowFrame? shadows)
	{
		DrawVoxelIndexedCommand command = new(
			transparentSnapshot,
			atlasTexture,
			waveShader,
			transparentState,
			sun,
			fog,
			transparentGpuTimer,
			shadows
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
		long activeSetGeneration,
		Vector3 cameraPosition,
		Vector3 cameraForward
	)
	{
		return VoxelTransparentCachePolicy.Evaluate(
			hasTransparentRequestKey,
			transparentRequestKey,
			geometryRevision,
			activeSetGeneration,
			cameraPosition,
			cameraForward,
			options.TransparentResortDistance,
			options.TransparentResortAngleDegrees
		);
	}

	private static float GetAngleDegrees(Vector3 left, Vector3 right)
	{
		float dot = Math.Clamp(Vector3.Dot(left, right), -1, 1);
		return MathF.Acos(dot) * (180f / MathF.PI);
	}
}
