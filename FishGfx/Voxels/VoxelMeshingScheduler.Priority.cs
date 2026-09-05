using System;
using System.Collections.Generic;
using System.Numerics;
using FishGfx.Graphics;

namespace FishGfx.Voxels;

public sealed partial class VoxelMeshingScheduler
{
	private static readonly IComparer<VoxelMeshingPriority> WorstPriorityFirst =
		Comparer<VoxelMeshingPriority>.Create((left, right) => right.CompareTo(left));

	private static void SelectPending(
		IEnumerable<ChunkCoordinate> coordinates,
		VoxelMeshingFocus? focus,
		int limit,
		PriorityQueue<ChunkCoordinate, VoxelMeshingPriority> selected,
		List<ChunkCoordinate> destination
	)
	{
		selected.Clear();
		destination.Clear();

		foreach (ChunkCoordinate coordinate in coordinates)
		{
			if (focus.HasValue && !focus.Value.ShouldSchedule(coordinate))
			{
				continue;
			}

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

		while (selected.TryDequeue(out ChunkCoordinate coordinate, out _))
			destination.Add(coordinate);
		destination.Reverse();
	}
}

internal readonly struct VoxelMeshingFocus
{
	private readonly Vector3 cameraPosition;
	private readonly ViewFrustum frustum;
	private readonly float maximumDistanceSquared;
	private readonly float schedulingDistanceSquared;
	private readonly bool cullingEnabled;

	internal VoxelMeshingFocus(
		Camera camera,
		float maximumDistance,
		float schedulingMargin,
		bool cullingEnabled
	)
	{
		ArgumentNullException.ThrowIfNull(camera);

		if (!float.IsFinite(maximumDistance) || maximumDistance <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(maximumDistance));
		}

		if (!float.IsFinite(schedulingMargin) || schedulingMargin < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(schedulingMargin));
		}

		cameraPosition = camera.Position;
		frustum = ViewFrustum.FromCamera(camera);
		maximumDistanceSquared = maximumDistance * maximumDistance;
		float schedulingDistance = maximumDistance + schedulingMargin;
		schedulingDistanceSquared = schedulingDistance * schedulingDistance;
		this.cullingEnabled = cullingEnabled;
	}

	internal bool TryGetColumnBounds(out int minX, out int maxX, out int minZ, out int maxZ)
	{
		minX = maxX = minZ = maxZ = 0;
		if (!cullingEnabled || !float.IsFinite(schedulingDistanceSquared)
			|| !float.IsFinite(cameraPosition.X) || !float.IsFinite(cameraPosition.Z)) return false;
		double radius = Math.Sqrt(schedulingDistanceSquared);
		minX = (int)Math.Clamp(Math.Floor((cameraPosition.X - radius - 8) / 16), int.MinValue, int.MaxValue);
		maxX = (int)Math.Clamp(Math.Ceiling((cameraPosition.X + radius - 8) / 16), int.MinValue, int.MaxValue);
		minZ = (int)Math.Clamp(Math.Floor((cameraPosition.Z - radius - 8) / 16), int.MinValue, int.MaxValue);
		maxZ = (int)Math.Clamp(Math.Ceiling((cameraPosition.Z + radius - 8) / 16), int.MinValue, int.MaxValue);
		return true;
	}

	internal bool ShouldScheduleColumn(int x, int z)
	{
		if (!cullingEnabled) return true;
		float dx = x * (float)VoxelWorld.ChunkSize + 8 - cameraPosition.X;
		float dz = z * (float)VoxelWorld.ChunkSize + 8 - cameraPosition.Z;
		return dx * dx + dz * dz <= schedulingDistanceSquared;
	}

	internal bool ShouldSchedule(ChunkCoordinate coordinate)
	{
		if (!cullingEnabled)
		{
			return true;
		}

		Vector3 center = coordinate.WorldOrigin + new Vector3(VoxelWorld.ChunkSize * 0.5f);
		return Vector3.DistanceSquared(cameraPosition, center) <= schedulingDistanceSquared;
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
