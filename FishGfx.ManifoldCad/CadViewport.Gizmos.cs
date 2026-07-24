using System.Numerics;
using FishGfx.Cad;
using FishGfx.Game;
using FishGfx.Graphics;

namespace FishGfx.ManifoldCad;

internal sealed partial class CadViewport
{
	private bool TryBeginGizmo(CadRect bounds, Vector2 mouse)
	{
		if (!hasFrameGizmo)
		{
			return false;
		}

		if (rotationGizmo)
		{
			return TryBeginRotationGizmo(bounds, mouse);
		}

		Vector2 local = ToCameraPoint(bounds, mouse);
		Vector3 origin = camera.WorldToScreen(ToVector(frameGizmoOrigin));
		Vector3[] axes = { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ };
		float length = Math.Max(distance * 0.08f, 20);
		float best = 7;
		int selected = -1;

		for (int index = 0; index < axes.Length; index++)
		{
			Vector3 end = camera.WorldToScreen(ToVector(frameGizmoOrigin) + axes[index] * length);
			float screenDistance = DistanceToSegment(
				local,
				new Vector2(origin.X, origin.Y),
				new Vector2(end.X, end.Y)
			);

			if (screenDistance < best)
			{
				best = screenDistance;
				selected = index;
			}
		}

		if (selected < 0)
		{
			return false;
		}

		activeGizmoAxis = selected;
		gizmoDragStart = mouse;
		gizmoTranslationStart = frameGizmoOrigin;
		return true;
	}

	private void UpdateGizmo(CadRect bounds, Vector2 mouse)
	{
		if (rotationGizmo)
		{
			UpdateRotationGizmo(bounds, mouse);
			return;
		}

		Vector3[] axes = { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ };
		Vector3 axis = axes[activeGizmoAxis];
		Vector3 origin = camera.WorldToScreen(ToVector(gizmoTranslationStart));
		Vector3 end = camera.WorldToScreen(ToVector(gizmoTranslationStart) + axis * Math.Max(distance * 0.08f, 20));
		Vector2 screenAxis = new(end.X - origin.X, end.Y - origin.Y);

		if (screenAxis.LengthSquared() <= 1e-6f)
		{
			return;
		}

		float pixels = Vector2.Dot(mouse - gizmoDragStart, Vector2.Normalize(screenAxis));
		float worldAmount = pixels * Math.Max(distance, 1) / Math.Max(bounds.Height, 1);
		CadPoint3 translation = gizmoTranslationStart + CadPoint3.FromVector3(axis * worldAmount);
		GizmoTranslationChanged?.Invoke(translation);
	}

	private bool TryBeginRotationGizmo(CadRect bounds, Vector2 mouse)
	{
		Vector2 local = ToCameraPoint(bounds, mouse);
		Vector3 center = ToVector(frameGizmoOrigin);
		Vector3[] axes = { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ };
		float radius = Math.Max(distance * 0.065f, 16);
		float best = 7;
		int selected = -1;

		for (int axisIndex = 0; axisIndex < axes.Length; axisIndex++)
		{
			Vector3 previous = camera.WorldToScreen(RingPoint(center, axes[axisIndex], radius, 0));

			for (int segment = 1; segment <= 48; segment++)
			{
				float angle = segment * MathF.Tau / 48;
				Vector3 current = camera.WorldToScreen(RingPoint(center, axes[axisIndex], radius, angle));
				float screenDistance = DistanceToSegment(
					local,
					new Vector2(previous.X, previous.Y),
					new Vector2(current.X, current.Y)
				);

				if (screenDistance < best)
				{
					best = screenDistance;
					selected = axisIndex;
				}

				previous = current;
			}
		}

		if (selected < 0)
		{
			return false;
		}

		Vector3 projectedCenter = camera.WorldToScreen(center);
		activeGizmoAxis = selected;
		gizmoEulerStart = selectedEuler;
		gizmoRotationCenter = new Vector2(projectedCenter.X, projectedCenter.Y);
		gizmoRotationStartAngle = MathF.Atan2(
			local.Y - gizmoRotationCenter.Y,
			local.X - gizmoRotationCenter.X
		);
		return true;
	}

	private void UpdateRotationGizmo(CadRect bounds, Vector2 mouse)
	{
		Vector2 local = ToCameraPoint(bounds, mouse);
		float currentAngle = MathF.Atan2(
			local.Y - gizmoRotationCenter.Y,
			local.X - gizmoRotationCenter.X
		);
		float delta = (currentAngle - gizmoRotationStartAngle) * 180 / MathF.PI;

		if (delta > 180)
		{
			delta -= 360;
		}
		else if (delta < -180)
		{
			delta += 360;
		}

		CadPoint3 euler = activeGizmoAxis switch
		{
			0 => gizmoEulerStart with { X = gizmoEulerStart.X + delta },
			1 => gizmoEulerStart with { Y = gizmoEulerStart.Y + delta },
			_ => gizmoEulerStart with { Z = gizmoEulerStart.Z + delta },
		};
		GizmoRotationChanged?.Invoke(euler);
	}

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

	private static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
	{
		float amount = ClosestSegmentAmount(point, start, end);
		return Vector2.Distance(point, start + (end - start) * amount);
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
		float scale
	)
	{
		return currentFocus
			- cameraRight * layoutDelta.X * scale
			+ cameraUp * layoutDelta.Y * scale;
	}


	private void DrawPartGizmo(RenderPass pass)
	{
		if (!hasFrameGizmo)
		{
			return;
		}

		Vector3 origin = ToVector(frameGizmoOrigin);
		float length = Math.Max(distance * 0.08f, 20);

		if (rotationGizmo)
		{
			DrawRing(pass, origin, Vector3.UnitX, length * 0.8f, Color.Red);
			DrawRing(pass, origin, Vector3.UnitY, length * 0.8f, Color.Green);
			DrawRing(pass, origin, Vector3.UnitZ, length * 0.8f, Color.Blue);
			return;
		}

		pass.DrawLine(new Vertex3(origin, Color.Red), new Vertex3(origin + Vector3.UnitX * length, Color.Red), 4);
		pass.DrawLine(new Vertex3(origin, Color.Green), new Vertex3(origin + Vector3.UnitY * length, Color.Green), 4);
		pass.DrawLine(new Vertex3(origin, Color.Blue), new Vertex3(origin + Vector3.UnitZ * length, Color.Blue), 4);
	}


	private static void DrawRing(RenderPass pass, Vector3 center, Vector3 axis, float radius, Color color)
	{
		Vector3 previous = RingPoint(center, axis, radius, 0);

		for (int segment = 1; segment <= 48; segment++)
		{
			Vector3 current = RingPoint(center, axis, radius, segment * MathF.Tau / 48);
			pass.DrawLine(new Vertex3(previous, color), new Vertex3(current, color), 3);
			previous = current;
		}
	}

	private static Vector3 RingPoint(Vector3 center, Vector3 axis, float radius, float angle)
	{
		Vector3 reference = MathF.Abs(axis.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitY;
		Vector3 first = Vector3.Normalize(Vector3.Cross(axis, reference));
		Vector3 second = Vector3.Normalize(Vector3.Cross(axis, first));
		return center + radius * (first * MathF.Cos(angle) + second * MathF.Sin(angle));
	}
}
