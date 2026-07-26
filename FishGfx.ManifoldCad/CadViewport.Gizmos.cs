using System.Numerics;
using FishGfx.Graphics;

namespace FishGfx.ManifoldCad;

internal sealed partial class CadViewport
{
	internal static bool TryIntersectSphere(PickingRay ray, Vector3 center, float radius, out float distance)
	{
		Vector3 offset = ray.Origin - center;
		float along = Vector3.Dot(offset, ray.Direction);
		float discriminant = along * along - (offset.LengthSquared() - radius * radius);

		if (discriminant < 0)
		{
			distance = 0;
			return false;
		}

		float root = MathF.Sqrt(discriminant);
		float near = -along - root;
		float far = -along + root;
		distance = near >= 0 ? near : far;
		return distance >= 0;
	}

	internal static float ClosestSegmentAmount(Vector2 point, Vector2 start, Vector2 end)
	{
		Vector2 segment = end - start;
		float lengthSquared = segment.LengthSquared();
		return lengthSquared <= 1e-8f
			? 0
			: Math.Clamp(Vector2.Dot(point - start, segment) / lengthSquared, 0, 1);
	}

	internal static bool IsProjectedPointInClip(Vector3 point)
	{
		return float.IsFinite(point.X)
			&& float.IsFinite(point.Y)
			&& float.IsFinite(point.Z)
			&& point.Z >= 0
			&& point.Z <= 1;
	}

	internal static bool IsProjectedPointVisible(Vector3 point, float surfaceDepth)
	{
		return IsProjectedPointInClip(point) && point.Z <= surfaceDepth + 0.002f;
	}

	internal static Vector2 ToCameraPoint(CadRect bounds, Vector2 layoutPoint)
	{
		// Layout coordinates are bottom-left based. The offscreen viewport is
		// composited with inverted V coordinates, so its camera-space Y already
		// matches the layout-local Y and must not be flipped again here.
		return layoutPoint - bounds.Minimum;
	}

	internal static Vector2 FromCameraPoint(CadRect bounds, Vector2 cameraPoint)
	{
		return bounds.Minimum + cameraPoint;
	}

	internal static Vector3 PanFocus(
		Vector3 currentFocus,
		Vector3 cameraRight,
		Vector3 cameraUp,
		Vector2 layoutDelta,
		float scale)
	{
		return currentFocus
			- cameraRight * layoutDelta.X * scale
			+ cameraUp * layoutDelta.Y * scale;
	}
}
