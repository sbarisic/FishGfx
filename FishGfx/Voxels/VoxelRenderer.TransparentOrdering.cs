using System;
using System.Diagnostics;
using System.Numerics;
using FishGfx.Graphics;

namespace FishGfx.Voxels;

public sealed partial class VoxelRenderer
{
	private void UpdateTransparentOrdering(Camera camera, float renderDistance)
	{
		transparentGpuTimer.Poll();

		if (transparentOrdering.TryTakeFailure(out Exception failure))
		{
			throw new InvalidOperationException(
				"Transparent voxel ordering failed on its worker thread.",
				failure
			);
		}

		long allocatedStart = GC.GetAllocatedBytesForCurrentThread();

		if (transparentSourceDirty || transparentSource == null)
		{
			RebuildTransparentOrderingSource();
		}

		while (transparentOrdering.TryTakeCompleted(out VoxelTransparentOrderingResult completed))
		{
			if (!ReferenceEquals(completed.Source, transparentSource)
				|| completed.Source.GeometryRevision != transparentGeometryRevision
				|| completed.Source.ActiveSetGeneration != activeSetGeneration)
			{
				transparentStaleResults++;
				completed.Dispose();
				continue;
			}

			ApplyTransparentOrdering(completed);
		}

		Vector3 cameraForward = camera.WorldForwardNormal;
		VoxelTransparentInvalidationReason reason = GetTransparentInvalidationReason(
			transparentGeometryRevision,
			activeSetGeneration,
			camera.Position,
			cameraForward
		);

		if (transparentSnapshot == null)
		{
			VoxelTransparentOrderingRequest initial = CreateTransparentOrderingRequest(
				camera,
				renderDistance,
				VoxelTransparentInvalidationReason.FirstFrame
			);
			VoxelTransparentOrderingResult result = transparentOrdering.BuildSynchronously(initial);
			ApplyTransparentOrdering(result);
			SetTransparentRequestKey(camera.Position, cameraForward);
			transparentLastRequestReason = VoxelTransparentInvalidationReason.FirstFrame;
		}
		else if (reason != VoxelTransparentInvalidationReason.None)
		{
			VoxelTransparentOrderingRequest request = CreateTransparentOrderingRequest(
				camera,
				renderDistance,
				reason
			);
			transparentOrdering.Request(request);
			SetTransparentRequestKey(camera.Position, cameraForward);
			transparentLastRequestReason = reason;
		}

		transparentMainThreadAllocatedBytes = checked(
			transparentMainThreadAllocatedBytes
				+ (int)(GC.GetAllocatedBytesForCurrentThread() - allocatedStart)
		);
	}

	private void RebuildTransparentOrderingSource()
	{
		long start = Stopwatch.GetTimestamp();
		int count = 0;

		for (int index = 0; index < activeGpuChunks.Count; index++)
		{
			if (HasTransparentGeometry(activeGpuChunks[index].Transparent))
			{
				count++;
			}
		}

		VoxelTransparentOrderingChunk[] chunks = new VoxelTransparentOrderingChunk[count];
		int destination = 0;

		for (int index = 0; index < activeGpuChunks.Count; index++)
		{
			GpuChunk chunk = activeGpuChunks[index];
			VoxelTransparentAllocation allocation = chunk.Transparent;

			if (!HasTransparentGeometry(allocation))
			{
				continue;
			}

			chunks[destination++] = new VoxelTransparentOrderingChunk(
				chunk.Bounds.Translate(chunk.Coordinate.WorldOrigin),
				allocation
			);
		}

		VoxelTransparentOrderingSource replacement = new(
			chunks,
			transparentGeometryRevision,
			activeSetGeneration
		);
		VoxelTransparentOrderingSource previous = transparentSource;
		transparentSource = replacement;
		transparentSourceDirty = false;
		previous?.ReleaseOwner();
		transparentSourceBuildMilliseconds += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
	}

	internal static bool HasTransparentGeometry(VoxelTransparentAllocation allocation)
	{
		return allocation is { VertexCount: > 0 };
	}

	private VoxelTransparentOrderingRequest CreateTransparentOrderingRequest(
		Camera camera,
		float renderDistance,
		VoxelTransparentInvalidationReason reason
	)
	{
		return new VoxelTransparentOrderingRequest(
			transparentSource,
			ViewFrustum.FromCamera(camera),
			camera.Position,
			camera.WorldForwardNormal,
			renderDistance,
			cullingEnabled,
			reason,
			++transparentRequestSequence
		);
	}

	private void ApplyTransparentOrdering(VoxelTransparentOrderingResult result)
	{
		long applyStart = Stopwatch.GetTimestamp();

		try
		{
			long uploadStart = Stopwatch.GetTimestamp();
			VoxelTransparentDrawSnapshot replacement = transparentIndexRing.Upload(
				transparentGeometry.Generation,
				result
			);
			transparentIndexUploadMilliseconds += Stopwatch.GetElapsedTime(
				uploadStart
			).TotalMilliseconds;
			transparentIndexUploadBytes = checked(
				transparentIndexUploadBytes + result.IndexCount * sizeof(uint)
			);
			VoxelTransparentDrawSnapshot previous = transparentSnapshot;
			transparentSnapshot = replacement;
			previous?.Dispose();
			visibleTransparentFaces = result.FaceCount;
			visibleTransparentVertices = result.IndexCount;
			transparentSortMilliseconds = result.SortMilliseconds;
			transparentWorkerAllocatedBytes = checked(
				transparentWorkerAllocatedBytes + result.WorkerAllocatedBytes
			);
		}
		finally
		{
			result.Dispose();
			transparentResultApplyMilliseconds += Stopwatch.GetElapsedTime(
				applyStart
			).TotalMilliseconds;
		}
	}

	private void ResetTransparentFrameWorkDiagnostics()
	{
		transparentSourceBuildMilliseconds = 0;
		transparentResultApplyMilliseconds = 0;
		transparentIndexUploadMilliseconds = 0;
		transparentIndexUploadBytes = 0;
		transparentMainThreadAllocatedBytes = 0;
		transparentWorkerAllocatedBytes = 0;
	}

	private void SetTransparentRequestKey(Vector3 cameraPosition, Vector3 cameraForward)
	{
		transparentRequestKey = new VoxelTransparentCacheKey(
			transparentGeometryRevision,
			activeSetGeneration,
			cameraPosition,
			cameraForward
		);
		hasTransparentRequestKey = true;
	}
}
