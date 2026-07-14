using System;
using System.Collections.Generic;
using System.Numerics;

namespace FishGfx;

public readonly struct BoundingSphere : IEquatable<BoundingSphere>
{
	public static BoundingSphere Empty { get; } = new(Vector3.Zero, 0);

	public BoundingSphere(Vector3 center, float radius)
	{
		if (!float.IsFinite(center.X)
			|| !float.IsFinite(center.Y)
			|| !float.IsFinite(center.Z))
		{
			throw new ArgumentOutOfRangeException(nameof(center));
		}

		if (!float.IsFinite(radius) || radius < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(radius));
		}

		Center = center;
		Radius = radius;
	}

	public Vector3 Center { get; }

	public float Radius { get; }

	public bool Intersects(BoundingSphere other)
	{
		float combinedRadius = Radius + other.Radius;

		return Vector3.DistanceSquared(Center, other.Center) <= combinedRadius * combinedRadius;
	}

	public BoundingSphere Translate(Vector3 offset)
	{
		return new BoundingSphere(Center + offset, Radius);
	}

	public static BoundingSphere FromPoints(IEnumerable<Vector3> points)
	{
		return FromBounds(AxisAlignedBoundingBox.FromPoints(points));
	}

	public static BoundingSphere FromBounds(AxisAlignedBoundingBox bounds)
	{
		if (bounds.IsEmpty)
		{
			return Empty;
		}

		return new BoundingSphere(bounds.Center, bounds.Size.Length() * 0.5f);
	}

	public bool Equals(BoundingSphere other)
	{
		return Center.Equals(other.Center) && Radius.Equals(other.Radius);
	}

	public override bool Equals(object obj)
	{
		return obj is BoundingSphere other && Equals(other);
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(Center, Radius);
	}

	public static bool operator ==(BoundingSphere left, BoundingSphere right)
	{
		return left.Equals(right);
	}

	public static bool operator !=(BoundingSphere left, BoundingSphere right)
	{
		return !left.Equals(right);
	}
}
