using System.Numerics;

namespace FishGfx.Voxels;

internal readonly struct VoxelTransparentCacheKey
{
	internal VoxelTransparentCacheKey(
		long geometryRevision,
		long activeSetGeneration,
		Vector3 cameraPosition,
		Vector3 cameraForward
	)
	{
		GeometryRevision = geometryRevision;
		ActiveSetGeneration = activeSetGeneration;
		CameraPosition = cameraPosition;
		CameraForward = cameraForward;
	}

	internal long GeometryRevision { get; }
	internal long ActiveSetGeneration { get; }
	internal Vector3 CameraPosition { get; }

	internal Vector3 CameraForward { get; }
}
