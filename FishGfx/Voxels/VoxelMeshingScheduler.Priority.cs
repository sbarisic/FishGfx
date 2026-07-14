using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using FishGfx.Graphics;

namespace FishGfx.Voxels;

public sealed partial class VoxelMeshingScheduler
{
	private static readonly IComparer<VoxelMeshingPriority> WorstPriorityFirst =
		Comparer<VoxelMeshingPriority>.Create((left, right) => right.CompareTo(left));

	private static ChunkCoordinate[] SelectPending(
		IEnumerable<ChunkCoordinate> coordinates,
		VoxelMeshingFocus? focus,
		int limit
	)
	{
		PriorityQueue<ChunkCoordinate, VoxelMeshingPriority> selected =
			new PriorityQueue<ChunkCoordinate, VoxelMeshingPriority>(WorstPriorityFirst);

		foreach (ChunkCoordinate coordinate in coordinates)
		{
			VoxelMeshingPriority priority = focus.HasValue
				? focus.Value.GetPriority(coordinate)
				: new VoxelMeshingPriority(0, 0, coordinate);

			if (selected.Count < limit)
			{
				selected.Enqueue(coordinate, priority);
				continue;
			}

			selected.TryPeek(out _, out VoxelMeshingPriority worst);

			if (priority.CompareTo(worst) < 0)
			{
				selected.DequeueEnqueue(coordinate, priority);
			}
		}

		return selected.UnorderedItems
			.OrderBy(item => item.Priority)
			.Select(item => item.Element)
			.ToArray();
	}
}

internal readonly struct VoxelMeshingFocus
{
	private readonly Vector3 cameraPosition;
	private readonly ViewFrustum frustum;
	private readonly float maximumDistanceSquared;
	private readonly bool cullingEnabled;

	internal VoxelMeshingFocus(
		Camera camera,
		float maximumDistance,
		bool cullingEnabled
	)
	{
		ArgumentNullException.ThrowIfNull(camera);

		if (!float.IsFinite(maximumDistance) || maximumDistance <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(maximumDistance));
		}

		cameraPosition = camera.Position;
		frustum = ViewFrustum.FromCamera(camera);
		maximumDistanceSquared = maximumDistance * maximumDistance;
		this.cullingEnabled = cullingEnabled;
	}

	internal VoxelMeshingPriority GetPriority(ChunkCoordinate coordinate)
	{
		Vector3 origin = coordinate.WorldOrigin;
		AxisAlignedBoundingBox bounds = AxisAlignedBoundingBox.FromPositionAndSize(
			origin,
			new Vector3(VoxelWorld.ChunkSize)
		);
		float distanceSquared = Vector3.DistanceSquared(cameraPosition, bounds.Center);
		bool withinDistance = distanceSquared <= maximumDistanceSquared;
		bool visible = !cullingEnabled
			|| withinDistance && frustum.Intersects(bounds);
		int tier = visible
			? 0
			: withinDistance
				? 1
				: 2;

		return new VoxelMeshingPriority(tier, distanceSquared, coordinate);
	}
}

internal readonly struct VoxelMeshingPriority : IComparable<VoxelMeshingPriority>
{
	private readonly int tier;
	private readonly float distanceSquared;
	private readonly ChunkCoordinate coordinate;

	internal VoxelMeshingPriority(
		int tier,
		float distanceSquared,
		ChunkCoordinate coordinate
	)
	{
		this.tier = tier;
		this.distanceSquared = distanceSquared;
		this.coordinate = coordinate;
	}

	public int CompareTo(VoxelMeshingPriority other)
	{
		int comparison = tier.CompareTo(other.tier);

		if (comparison != 0)
		{
			return comparison;
		}

		comparison = distanceSquared.CompareTo(other.distanceSquared);

		if (comparison != 0)
		{
			return comparison;
		}

		comparison = coordinate.X.CompareTo(other.coordinate.X);

		if (comparison != 0)
		{
			return comparison;
		}

		comparison = coordinate.Y.CompareTo(other.coordinate.Y);

		return comparison != 0
			? comparison
			: coordinate.Z.CompareTo(other.coordinate.Z);
	}
}
