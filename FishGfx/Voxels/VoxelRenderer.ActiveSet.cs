using System;
using System.Collections.Generic;
using System.Numerics;

namespace FishGfx.Voxels;

public sealed partial class VoxelRenderer
{
	private bool RefreshActiveSetIfNeeded(Vector3 cameraPosition, float renderDistance)
	{
		float refreshDistanceSquared = options.ActiveSetRefreshDistance
			* options.ActiveSetRefreshDistance;
		bool moved = !hasActiveSetAnchor
			|| Vector3.DistanceSquared(cameraPosition, activeSetAnchor) >= refreshDistanceSquared;
		bool distanceChanged = !hasActiveSetAnchor || activeSetRenderDistance != renderDistance;

		if (!activeSetDirty && !moved && !distanceChanged)
		{
			candidateChunks = gpuChunks.Count;
			inactiveCachedChunks = gpuChunks.Count - activeGpuChunks.Count;
			return false;
		}

		HashSet<ChunkCoordinate> previous = new(activeCoordinates);
		activeCoordinates.Clear();
		activeGpuChunks.Clear();

		for (int index = 0; index < orderedGpuChunks.Count; index++)
		{
			GpuChunk chunk = orderedGpuChunks[index];
			bool wasActive = previous.Contains(chunk.Coordinate);
			float margin = wasActive ? options.DeactivationMargin : options.ActivationMargin;
			float radius = renderDistance + margin;
			Vector3 center = chunk.Coordinate.WorldOrigin + new Vector3(VoxelWorld.ChunkSize * 0.5f);
			bool active = !cullingEnabled
				|| Vector3.DistanceSquared(cameraPosition, center) <= radius * radius;

			if (!active)
			{
				continue;
			}

			activeCoordinates.Add(chunk.Coordinate);
			activeGpuChunks.Add(chunk);
		}

		activeSetAnchor = cameraPosition;
		activeSetRenderDistance = renderDistance;
		hasActiveSetAnchor = true;
		activeSetDirty = false;
		candidateChunks = gpuChunks.Count;
		inactiveCachedChunks = gpuChunks.Count - activeGpuChunks.Count;
		activeSetGeneration++;
		transparentSourceDirty = true;
		return true;
	}
}
