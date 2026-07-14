using System;
using System.Numerics;

namespace FishGfx.Graphics;

public readonly record struct PickingRay
{
	public PickingRay(Vector3 origin, Vector3 direction)
	{
		if (direction.LengthSquared() == 0)
		{
			throw new ArgumentException(
				"Ray direction cannot be zero.",
				nameof(direction)
			);
		}

		Origin = origin;
		Direction = Vector3.Normalize(direction);
	}

	public Vector3 Origin { get; }

	public Vector3 Direction { get; }

	public Vector3 GetPoint(float distance)
	{
		return Origin + Direction * distance;
	}
}
