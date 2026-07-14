using System;
using System.Collections.Generic;
using System.Numerics;

namespace FishGfx.Graphics;

public static class RenderItemComparers
{
	public static IComparer<RenderItem> OpaqueFrontToBack(RenderView view)
	{
		return CreateFrontToBack(CaptureView(view));
	}

	public static IComparer<RenderItem> OpaqueFrontToBack(Camera camera)
	{
		return CreateFrontToBack(CaptureCamera(camera));
	}

	public static IComparer<RenderItem> OpaqueStateThenFrontToBack(RenderView view)
	{
		return CreateStateThenFrontToBack(CaptureView(view));
	}

	public static IComparer<RenderItem> OpaqueStateThenFrontToBack(Camera camera)
	{
		return CreateStateThenFrontToBack(CaptureCamera(camera));
	}

	public static IComparer<RenderItem> TransparentBackToFront(RenderView view)
	{
		return CreateBackToFront(CaptureView(view));
	}

	public static IComparer<RenderItem> TransparentBackToFront(Camera camera)
	{
		return CreateBackToFront(CaptureCamera(camera));
	}

	private static IComparer<RenderItem> CreateFrontToBack(CameraValues camera)
	{
		return Comparer<RenderItem>.Create((left, right) =>
		{
			int result = left.Layer.CompareTo(right.Layer);

			if (result == 0)
			{
				result = Depth(left, camera).CompareTo(Depth(right, camera));
			}

			if (result == 0)
			{
				result = left.SortKey.CompareTo(right.SortKey);
			}

			if (result == 0)
			{
				result = left.Sequence.CompareTo(right.Sequence);
			}

			return result;
		});
	}

	private static IComparer<RenderItem> CreateStateThenFrontToBack(CameraValues camera)
	{
		return Comparer<RenderItem>.Create((left, right) =>
		{
			int result = left.Layer.CompareTo(right.Layer);

			if (result == 0)
			{
				result = left.SortKey.CompareTo(right.SortKey);
			}

			if (result == 0)
			{
				result = Depth(left, camera).CompareTo(Depth(right, camera));
			}

			if (result == 0)
			{
				result = left.Sequence.CompareTo(right.Sequence);
			}

			return result;
		});
	}

	private static IComparer<RenderItem> CreateBackToFront(CameraValues camera)
	{
		return Comparer<RenderItem>.Create((left, right) =>
		{
			int result = left.Layer.CompareTo(right.Layer);

			if (result == 0)
			{
				result = Depth(right, camera).CompareTo(Depth(left, camera));
			}

			if (result == 0)
			{
				result = left.SortKey.CompareTo(right.SortKey);
			}

			if (result == 0)
			{
				result = left.Sequence.CompareTo(right.Sequence);
			}

			return result;
		});
	}

	private static CameraValues CaptureView(RenderView view)
	{
		if (!Matrix4x4.Invert(view.View, out Matrix4x4 world))
		{
			throw new ArgumentException("The render view matrix must be invertible.", nameof(view));
		}

		Vector3 forward = Vector3.Normalize(Vector3.TransformNormal(-Vector3.UnitZ, world));

		if (!IsFinite(view.Position) || !IsFinite(forward))
		{
			throw new ArgumentException(
				"Render-view position and forward direction must be finite.",
				nameof(view)
			);
		}

		return new CameraValues(view.Position, forward);
	}

	private static CameraValues CaptureCamera(Camera camera)
	{
		ArgumentNullException.ThrowIfNull(camera);

		Vector3 position = camera.Position;
		Vector3 forward = camera.WorldForwardNormal;

		if (!IsFinite(position) || !IsFinite(forward))
		{
			throw new ArgumentException(
				"Camera position and forward direction must be finite.",
				nameof(camera)
			);
		}

		return new CameraValues(position, forward);
	}

	private static float Depth(RenderItem item, CameraValues camera)
	{
		return Vector3.Dot(item.SortPosition - camera.Position, camera.Forward);
	}

	private static bool IsFinite(Vector3 value)
	{
		return float.IsFinite(value.X)
			&& float.IsFinite(value.Y)
			&& float.IsFinite(value.Z);
	}

	private readonly record struct CameraValues(Vector3 Position, Vector3 Forward);
}
