using System.Numerics;
using Xunit;

namespace FishGfx.Tests;

public sealed class GeometryTests
{
	[Fact]
	public void EmptyBoundsGrowFromIncludedPoints()
	{
		AxisAlignedBoundingBox bounds = AxisAlignedBoundingBox.Empty
			.Include(new Vector3(3, -2, 4))
			.Include(new Vector3(-1, 5, 2));

		Assert.Equal(new Vector3(-1, -2, 2), bounds.Min);
		Assert.Equal(new Vector3(3, 5, 4), bounds.Max);
		Assert.True(bounds.Contains(Vector3.Zero + new Vector3(0, 0, 2)));
	}

	[Fact]
	public void IntersectionAndUnionHandleEmptyBounds()
	{
		AxisAlignedBoundingBox first = AxisAlignedBoundingBox.FromPositionAndSize(
			Vector3.Zero,
			new Vector3(4)
		);
		AxisAlignedBoundingBox second = AxisAlignedBoundingBox.FromPositionAndSize(
			new Vector3(2),
			new Vector3(4)
		);

		Assert.Equal(
			new AxisAlignedBoundingBox(new Vector3(2), new Vector3(4)),
			first.Intersection(second)
		);
		Assert.Equal(
			new AxisAlignedBoundingBox(Vector3.Zero, new Vector3(6)),
			first.Union(second)
		);
		Assert.Equal(first, first.Union(AxisAlignedBoundingBox.Empty));
		Assert.True(first.Intersection(AxisAlignedBoundingBox.Empty).IsEmpty);
	}

	[Fact]
	public void BoundingSphereUsesTheBoundsCenterAndHalfDiagonal()
	{
		AxisAlignedBoundingBox bounds = new(Vector3.Zero, new Vector3(2, 4, 6));

		BoundingSphere sphere = BoundingSphere.FromBounds(bounds);

		Assert.Equal(new Vector3(1, 2, 3), sphere.Center);
		Assert.Equal(bounds.Size.Length() * 0.5f, sphere.Radius);
	}
}
