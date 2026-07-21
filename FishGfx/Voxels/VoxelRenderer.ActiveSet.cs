using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;

namespace FishGfx.Voxels;

public enum VoxelActiveSetRefreshReason
{
	None,
	FirstUse,
	Geometry,
	Movement,
	RenderDistance,
}

public sealed partial class VoxelRenderer
{
	private bool RefreshActiveSetIfNeeded(Vector3 cameraPosition, float renderDistance)
	{
		float refreshDistanceSquared = options.ActiveSetRefreshDistance
			* options.ActiveSetRefreshDistance;
		bool firstUse = !hasActiveSetAnchor;
		bool moved = !firstUse
			&& Vector3.DistanceSquared(cameraPosition, activeSetAnchor) >= refreshDistanceSquared;
		bool distanceChanged = !firstUse && activeSetRenderDistance != renderDistance;

		if (!activeSetDirty && !firstUse && !moved && !distanceChanged)
		{
			candidateChunks = activeSetTestedChunks;
			inactiveCachedChunks = gpuChunks.Count - activeGpuChunks.Count;
			activeSetRefreshReason = VoxelActiveSetRefreshReason.None;
			activeSetRefreshMilliseconds = 0;
			activeSetAllocatedBytes = 0;
			activeSetVisitedColumns = 0;
			activeSetAdditions = 0;
			activeSetRemovals = 0;
			return false;
		}

		long allocationStart = GC.GetAllocatedBytesForCurrentThread();
		long started = Stopwatch.GetTimestamp();
		nextActiveCoordinates.Clear();
		nextActiveGpuChunks.Clear();
		activeSetVisitedColumns = 0;
		activeSetTestedChunks = 0;
		activeSetAdditions = 0;
		activeSetRemovals = 0;
		bool transparentMembershipChanged = false;

		float deactivationRadius = renderDistance + options.DeactivationMargin;
		float deactivationRadiusSquared = deactivationRadius * deactivationRadius;
		for (int index = 0; index < activeGpuChunks.Count; index++)
		{
			GpuChunk chunk = activeGpuChunks[index];
			Vector3 center = chunk.Coordinate.WorldOrigin
				+ new Vector3(VoxelWorld.ChunkSize * 0.5f);
			bool retain = !cullingEnabled
				|| Vector3.DistanceSquared(cameraPosition, center) <= deactivationRadiusSquared;
			if (retain)
			{
				nextActiveCoordinates.Add(chunk.Coordinate);
				nextActiveGpuChunks.Add(chunk);
			}
			else
			{
				activeSetRemovals++;
				transparentMembershipChanged |= HasTransparentGeometry(chunk.Transparent);
			}
		}

		float activationRadius = renderDistance + options.ActivationMargin;
		float activationRadiusSquared = activationRadius * activationRadius;
		int minimumChunkX = (int)MathF.Floor(
			(cameraPosition.X - activationRadius) / VoxelWorld.ChunkSize
		);
		int maximumChunkX = (int)MathF.Floor(
			(cameraPosition.X + activationRadius) / VoxelWorld.ChunkSize
		);
		int minimumChunkZ = (int)MathF.Floor(
			(cameraPosition.Z - activationRadius) / VoxelWorld.ChunkSize
		);
		int maximumChunkZ = (int)MathF.Floor(
			(cameraPosition.Z + activationRadius) / VoxelWorld.ChunkSize
		);

		for (int chunkZ = minimumChunkZ; chunkZ <= maximumChunkZ; chunkZ++)
		for (int chunkX = minimumChunkX; chunkX <= maximumChunkX; chunkX++)
		{
			activeSetVisitedColumns++;
			if (!gpuChunkColumns.TryGetValue(
				new GpuColumnCoordinate(chunkX, chunkZ),
				out List<GpuChunk> chunks))
			{
				continue;
			}

			for (int index = 0; index < chunks.Count; index++)
			{
				GpuChunk chunk = chunks[index];
				activeSetTestedChunks++;
				if (nextActiveCoordinates.Contains(chunk.Coordinate))
				{
					continue;
				}

				Vector3 center = chunk.Coordinate.WorldOrigin
					+ new Vector3(VoxelWorld.ChunkSize * 0.5f);
				if (cullingEnabled
					&& Vector3.DistanceSquared(cameraPosition, center) > activationRadiusSquared)
				{
					continue;
				}

				nextActiveCoordinates.Add(chunk.Coordinate);
				nextActiveGpuChunks.Add(chunk);
				activeSetAdditions++;
				transparentMembershipChanged |= HasTransparentGeometry(chunk.Transparent);
			}
		}

		bool membershipChanged = activeSetAdditions != 0 || activeSetRemovals != 0;
		(activeCoordinates, nextActiveCoordinates) = (nextActiveCoordinates, activeCoordinates);
		(activeGpuChunks, nextActiveGpuChunks) = (nextActiveGpuChunks, activeGpuChunks);
		activeSetAnchor = cameraPosition;
		activeSetRenderDistance = renderDistance;
		hasActiveSetAnchor = true;
		activeSetDirty = false;
		candidateChunks = activeSetTestedChunks;
		inactiveCachedChunks = gpuChunks.Count - activeGpuChunks.Count;
		if (membershipChanged)
		{
			if (transparentMembershipChanged)
			{
				transparentActiveSetGeneration++;
				transparentSourceDirty = true;
			}
		}

		activeSetRefreshReason = firstUse
			? VoxelActiveSetRefreshReason.FirstUse
			: distanceChanged
				? VoxelActiveSetRefreshReason.RenderDistance
				: moved
					? VoxelActiveSetRefreshReason.Movement
					: VoxelActiveSetRefreshReason.Geometry;
		activeSetRefreshMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
		activeSetAllocatedBytes = checked(
			(int)(GC.GetAllocatedBytesForCurrentThread() - allocationStart)
		);
		return membershipChanged;
	}
}
