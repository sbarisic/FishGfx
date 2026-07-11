using System;
using System.Collections.Generic;
using System.Numerics;

namespace FishGfx.Graphics
{
	public static class RenderSubmissionComparers
	{
		public static IComparer<RenderSubmission> OpaqueFrontToBack(Camera camera)
		{
			CameraValues values = CaptureCamera(camera);

			return Comparer<RenderSubmission>.Create((left, right) =>
			{
				int result = left.Layer.CompareTo(right.Layer);

				if (result == 0)
					result = Depth(left, values).CompareTo(Depth(right, values));
				if (result == 0)
					result = left.SortKey.CompareTo(right.SortKey);
				if (result == 0)
					result = left.Sequence.CompareTo(right.Sequence);

				return result;
			});
		}

		public static IComparer<RenderSubmission> OpaqueStateThenFrontToBack(Camera camera)
		{
			CameraValues values = CaptureCamera(camera);

			return Comparer<RenderSubmission>.Create((left, right) =>
			{
				int result = left.Layer.CompareTo(right.Layer);

				if (result == 0)
					result = left.SortKey.CompareTo(right.SortKey);
				if (result == 0)
					result = Depth(left, values).CompareTo(Depth(right, values));
				if (result == 0)
					result = left.Sequence.CompareTo(right.Sequence);

				return result;
			});
		}

		public static IComparer<RenderSubmission> TransparentBackToFront(Camera camera)
		{
			CameraValues values = CaptureCamera(camera);

			return Comparer<RenderSubmission>.Create((left, right) =>
			{
				int result = left.Layer.CompareTo(right.Layer);

				if (result == 0)
					result = Depth(right, values).CompareTo(Depth(left, values));
				if (result == 0)
					result = left.SortKey.CompareTo(right.SortKey);
				if (result == 0)
					result = left.Sequence.CompareTo(right.Sequence);

				return result;
			});
		}

		private static CameraValues CaptureCamera(Camera camera)
		{
			if (camera == null)
				throw new ArgumentNullException(nameof(camera));

			Vector3 position = camera.Position;
			Vector3 forward = camera.WorldForwardNormal;

			if (!IsFinite(position) || !IsFinite(forward))
				throw new ArgumentException("Camera position and forward direction must be finite.", nameof(camera));

			return new CameraValues(position, forward);
		}

		private static float Depth(RenderSubmission submission, CameraValues camera)
		{
			return Vector3.Dot(submission.SortPosition - camera.Position, camera.Forward);
		}

		private static bool IsFinite(Vector3 value)
		{
			return float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
		}

		private readonly struct CameraValues
		{
			public CameraValues(Vector3 position, Vector3 forward)
			{
				Position = position;
				Forward = forward;
			}

			public Vector3 Position { get; }
			public Vector3 Forward { get; }
		}
	}
}
