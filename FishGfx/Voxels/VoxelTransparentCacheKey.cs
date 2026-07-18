using System.Numerics;

namespace FishGfx.Voxels;

internal readonly struct VoxelTransparentCacheKey
{
	internal VoxelTransparentCacheKey(
		long geometryRevision,
		ulong visibleSignature,
		Vector3 cameraPosition,
		Vector3 cameraForward
	)
	{
		GeometryRevision = geometryRevision;
		VisibleSignature = visibleSignature;
		CameraPosition = cameraPosition;
		CameraForward = cameraForward;
	}

	internal long GeometryRevision { get; }
	internal ulong VisibleSignature { get; }
	internal Vector3 CameraPosition { get; }

	internal Vector3 CameraForward { get; }
}
