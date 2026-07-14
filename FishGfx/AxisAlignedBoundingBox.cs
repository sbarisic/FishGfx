using System;
using System.Collections.Generic;
using System.Numerics;

namespace FishGfx;

public readonly struct AxisAlignedBoundingBox : IEquatable<AxisAlignedBoundingBox>
{
	public static AxisAlignedBoundingBox Empty { get; } = new(
		new Vector3(float.PositiveInfinity),
		new Vector3(float.NegativeInfinity)
	);

	public AxisAlignedBoundingBox(Vector3 min, Vector3 max)
	{
		if (!IsFiniteOrInfinity(min))
		{
			throw new ArgumentOutOfRangeException(nameof(min));
		}

		if (!IsFiniteOrInfinity(max))
		{
			throw new ArgumentOutOfRangeException(nameof(max));
		}

		Min = min;
		Max = max;
	}

	public Vector3 Min { get; }

	public Vector3 Max { get; }

	public Vector3 Size => IsEmpty ? Vector3.Zero : Max - Min;

	public Vector3 Center => IsEmpty ? Vector3.Zero : (Min + Max) * 0.5f;

	public bool IsEmpty => Min.X > Max.X || Min.Y > Max.Y || Min.Z > Max.Z;

	public static AxisAlignedBoundingBox FromPositionAndSize(Vector3 position, Vector3 size)
	{
		if (!IsFinite(position))
		{
			throw new ArgumentOutOfRangeException(nameof(position));
		}

		if (!IsFinite(size) || size.X < 0 || size.Y < 0 || size.Z < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(size));
		}

		return new AxisAlignedBoundingBox(position, position + size);
	}

	public static AxisAlignedBoundingBox FromPoints(IEnumerable<Vector3> points)
	{
		ArgumentNullException.ThrowIfNull(points);

		AxisAlignedBoundingBox bounds = Empty;

		foreach (Vector3 point in points)
		{
			if (!IsFinite(point))
			{
				throw new ArgumentOutOfRangeException(nameof(points), "Points must contain only finite values.");
			}

			bounds = bounds.Include(point);
		}

		return bounds;
	}

	public AxisAlignedBoundingBox Include(Vector3 point)
	{
		if (!IsFinite(point))
		{
			throw new ArgumentOutOfRangeException(nameof(point));
		}

		if (IsEmpty)
		{
			return new AxisAlignedBoundingBox(point, point);
		}

		return new AxisAlignedBoundingBox(
			Vector3.Min(Min, point),
			Vector3.Max(Max, point)
		);
	}

	public bool Contains(Vector3 point)
	{
		if (IsEmpty)
		{
			return false;
		}

		return point.X >= Min.X
			&& point.X <= Max.X
			&& point.Y >= Min.Y
			&& point.Y <= Max.Y
			&& point.Z >= Min.Z
			&& point.Z <= Max.Z;
	}

	public bool Intersects(AxisAlignedBoundingBox other)
	{
		if (IsEmpty || other.IsEmpty)
		{
			return false;
		}

		return Min.X <= other.Max.X
			&& Max.X >= other.Min.X
			&& Min.Y <= other.Max.Y
			&& Max.Y >= other.Min.Y
			&& Min.Z <= other.Max.Z
			&& Max.Z >= other.Min.Z;
	}

	public AxisAlignedBoundingBox Intersection(AxisAlignedBoundingBox other)
	{
		if (!Intersects(other))
		{
			return Empty;
		}

		return new AxisAlignedBoundingBox(
			Vector3.Max(Min, other.Min),
			Vector3.Min(Max, other.Max)
		);
	}

	public AxisAlignedBoundingBox Union(AxisAlignedBoundingBox other)
	{
		if (IsEmpty)
		{
			return other;
		}

		if (other.IsEmpty)
		{
			return this;
		}

		return new AxisAlignedBoundingBox(
			Vector3.Min(Min, other.Min),
			Vector3.Max(Max, other.Max)
		);
	}

	public AxisAlignedBoundingBox Translate(Vector3 offset)
	{
		if (!IsFinite(offset))
		{
			throw new ArgumentOutOfRangeException(nameof(offset));
		}

		if (IsEmpty)
		{
			return Empty;
		}

		return new AxisAlignedBoundingBox(Min + offset, Max + offset);
	}

	public IEnumerable<Vector3> GetCorners()
	{
		if (IsEmpty)
		{
			yield break;
		}

		yield return new Vector3(Min.X, Min.Y, Min.Z);
		yield return new Vector3(Max.X, Min.Y, Min.Z);
		yield return new Vector3(Min.X, Max.Y, Min.Z);
		yield return new Vector3(Max.X, Max.Y, Min.Z);
		yield return new Vector3(Min.X, Min.Y, Max.Z);
		yield return new Vector3(Max.X, Min.Y, Max.Z);
		yield return new Vector3(Min.X, Max.Y, Max.Z);
		yield return new Vector3(Max.X, Max.Y, Max.Z);
	}

	public bool Equals(AxisAlignedBoundingBox other)
	{
		return Min.Equals(other.Min) && Max.Equals(other.Max);
	}

	public override bool Equals(object obj)
	{
		return obj is AxisAlignedBoundingBox other && Equals(other);
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(Min, Max);
	}

	public override string ToString()
	{
		return IsEmpty ? "Empty" : $"{Min} .. {Max} ({Size})";
	}

	public static bool operator ==(AxisAlignedBoundingBox left, AxisAlignedBoundingBox right)
	{
		return left.Equals(right);
	}

	public static bool operator !=(AxisAlignedBoundingBox left, AxisAlignedBoundingBox right)
	{
		return !left.Equals(right);
	}

	private static bool IsFinite(Vector3 value)
	{
		return float.IsFinite(value.X)
			&& float.IsFinite(value.Y)
			&& float.IsFinite(value.Z);
	}

	private static bool IsFiniteOrInfinity(Vector3 value)
	{
		return !float.IsNaN(value.X)
			&& !float.IsNaN(value.Y)
			&& !float.IsNaN(value.Z);
	}
}
