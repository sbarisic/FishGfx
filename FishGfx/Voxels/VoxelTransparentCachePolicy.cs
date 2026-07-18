using System;
using System.Numerics;

namespace FishGfx.Voxels;

internal static class VoxelTransparentCachePolicy
{
	internal static VoxelTransparentInvalidationReason Evaluate(
		bool hasCache,
		VoxelTransparentCacheKey cached,
		long geometryRevision,
		ulong visibleSignature,
		Vector3 cameraPosition,
		Vector3 cameraForward,
		float distanceThreshold,
		float angleThresholdDegrees
	)
	{
		if (!hasCache)
		{
			return VoxelTransparentInvalidationReason.FirstFrame;
		}

		if (cached.GeometryRevision != geometryRevision)
		{
			return VoxelTransparentInvalidationReason.Geometry;
		}

		if (cached.VisibleSignature != visibleSignature)
		{
			return VoxelTransparentInvalidationReason.Visibility;
		}

		float distanceSquared = Vector3.DistanceSquared(cached.CameraPosition, cameraPosition);

		if (distanceThreshold == 0
			? distanceSquared > 0
			: distanceSquared >= distanceThreshold * distanceThreshold)
		{
			return VoxelTransparentInvalidationReason.Translation;
		}

		float dot = Math.Clamp(Vector3.Dot(cached.CameraForward, cameraForward), -1, 1);
		float angleDegrees = MathF.Acos(dot) * (180f / MathF.PI);

		if (angleThresholdDegrees == 0
			? angleDegrees > 0
			: angleDegrees >= angleThresholdDegrees)
		{
			return VoxelTransparentInvalidationReason.Rotation;
		}

		return VoxelTransparentInvalidationReason.None;
	}
}
