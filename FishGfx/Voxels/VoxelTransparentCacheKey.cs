using System;
using System.Numerics;

namespace FishGfx.Voxels;

internal readonly struct VoxelTransparentCacheKey : IEquatable<VoxelTransparentCacheKey>
{
	internal VoxelTransparentCacheKey(long geometryRevision, ulong visibleSignature, Matrix4x4 view)
	{
		GeometryRevision = geometryRevision;
		VisibleSignature = visibleSignature;
		View = view;
	}

	internal long GeometryRevision { get; }
	internal ulong VisibleSignature { get; }
	internal Matrix4x4 View { get; }

	public bool Equals(VoxelTransparentCacheKey other)
	{
		return GeometryRevision == other.GeometryRevision
			&& VisibleSignature == other.VisibleSignature
			&& View == other.View;
	}

	public override bool Equals(object obj) => obj is VoxelTransparentCacheKey other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(GeometryRevision, VisibleSignature, View);
}
