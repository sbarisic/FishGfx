using System;
using System.Collections.Generic;
using System.Numerics;

namespace FishGfx.Graphics
{
	public readonly struct CornerRadii : IEquatable<CornerRadii>
	{
		public float TopLeft { get; }
		public float TopRight { get; }
		public float BottomRight { get; }
		public float BottomLeft { get; }

		public CornerRadii(float radius)
			: this(radius, radius, radius, radius) { }

		public CornerRadii(float topLeft, float topRight, float bottomRight, float bottomLeft)
		{
			Validate(topLeft, nameof(topLeft));
			Validate(topRight, nameof(topRight));
			Validate(bottomRight, nameof(bottomRight));
			Validate(bottomLeft, nameof(bottomLeft));
			TopLeft = topLeft;
			TopRight = topRight;
			BottomRight = bottomRight;
			BottomLeft = bottomLeft;
		}

		public bool Equals(CornerRadii other) =>
			TopLeft == other.TopLeft
			&& TopRight == other.TopRight
			&& BottomRight == other.BottomRight
			&& BottomLeft == other.BottomLeft;

		public override bool Equals(object obj) => obj is CornerRadii other && Equals(other);

		public override int GetHashCode() => HashCode.Combine(TopLeft, TopRight, BottomRight, BottomLeft);

		private static void Validate(float value, string name)
		{
			if (!float.IsFinite(value) || value < 0)
				throw new ArgumentOutOfRangeException(name, "Corner radii must be finite and non-negative.");
		}
	}

	internal static class RoundedRectangleTessellator
	{
		internal static CornerRadii Clamp(Vector2 size, CornerRadii radii)
		{
			ValidateSize(size);
			float scale = 1;
			ScaleForPair(ref scale, size.X, radii.TopLeft + radii.TopRight);
			ScaleForPair(ref scale, size.X, radii.BottomLeft + radii.BottomRight);
			ScaleForPair(ref scale, size.Y, radii.TopLeft + radii.BottomLeft);
			ScaleForPair(ref scale, size.Y, radii.TopRight + radii.BottomRight);
			return new CornerRadii(
				radii.TopLeft * scale,
				radii.TopRight * scale,
				radii.BottomRight * scale,
				radii.BottomLeft * scale
			);
		}

		internal static Vector2[] Outline(Vector2 position, Vector2 size, CornerRadii radii, int cornerSegments = 0)
		{
			Validate(position, size, cornerSegments);
			if (size.X == 0 || size.Y == 0)
				return Array.Empty<Vector2>();

			CornerRadii fitted = Clamp(size, radii);
			List<Vector2> points = Boundary(position, size, fitted, cornerSegments);
			points.Add(points[0]);
			return points.ToArray();
		}

		internal static Vector2[] Filled(Vector2 position, Vector2 size, CornerRadii radii, int cornerSegments = 0)
		{
			Validate(position, size, cornerSegments);
			if (size.X == 0 || size.Y == 0)
				return Array.Empty<Vector2>();

			CornerRadii fitted = Clamp(size, radii);
			List<Vector2> boundary = Boundary(position, size, fitted, cornerSegments);
			Vector2 center = position + size / 2;
			Vector2[] vertices = new Vector2[boundary.Count * 3];
			for (int i = 0; i < boundary.Count; i++)
			{
				int offset = i * 3;
				vertices[offset] = center;
				vertices[offset + 1] = boundary[i];
				vertices[offset + 2] = boundary[(i + 1) % boundary.Count];
			}
			return vertices;
		}

		internal static int ResolveCornerSegments(float radius, int requested)
		{
			if (requested < 0)
				throw new ArgumentOutOfRangeException(nameof(requested));
			if (requested > 0)
				return requested;
			if (radius == 0)
				return 1;
			return Math.Max(1, (int)MathF.Ceiling(PrimitiveTessellator.ResolveArcSegments(radius, 0) / 4f));
		}

		private static List<Vector2> Boundary(Vector2 position, Vector2 size, CornerRadii radii, int requestedSegments)
		{
			float x0 = position.X;
			float y0 = position.Y;
			float x1 = position.X + size.X;
			float y1 = position.Y + size.Y;
			List<Vector2> points = new List<Vector2>();
			AppendCorner(
				points,
				new Vector2(x1 - radii.BottomRight, y0 + radii.BottomRight),
				radii.BottomRight,
				-MathF.PI / 2,
				0,
				requestedSegments,
				new Vector2(x1, y0)
			);
			AppendCorner(
				points,
				new Vector2(x1 - radii.TopRight, y1 - radii.TopRight),
				radii.TopRight,
				0,
				MathF.PI / 2,
				requestedSegments,
				new Vector2(x1, y1)
			);
			AppendCorner(
				points,
				new Vector2(x0 + radii.TopLeft, y1 - radii.TopLeft),
				radii.TopLeft,
				MathF.PI / 2,
				MathF.PI,
				requestedSegments,
				new Vector2(x0, y1)
			);
			AppendCorner(
				points,
				new Vector2(x0 + radii.BottomLeft, y0 + radii.BottomLeft),
				radii.BottomLeft,
				MathF.PI,
				MathF.PI * 1.5f,
				requestedSegments,
				new Vector2(x0, y0)
			);
			return points;
		}

		private static void AppendCorner(
			List<Vector2> points,
			Vector2 center,
			float radius,
			float start,
			float end,
			int requestedSegments,
			Vector2 sharpCorner
		)
		{
			if (radius == 0)
			{
				points.Add(sharpCorner);
				return;
			}
			int segments = ResolveCornerSegments(radius, requestedSegments);
			for (int i = 0; i <= segments; i++)
			{
				float angle = start + (end - start) * i / segments;
				points.Add(center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius);
			}
		}

		private static void ScaleForPair(ref float scale, float available, float requested)
		{
			if (requested > 0)
				scale = MathF.Min(scale, available / requested);
		}

		private static void Validate(Vector2 position, Vector2 size, int cornerSegments)
		{
			if (!float.IsFinite(position.X) || !float.IsFinite(position.Y))
				throw new ArgumentOutOfRangeException(nameof(position));
			ValidateSize(size);
			if (cornerSegments < 0)
				throw new ArgumentOutOfRangeException(nameof(cornerSegments));
		}

		private static void ValidateSize(Vector2 size)
		{
			if (!float.IsFinite(size.X) || !float.IsFinite(size.Y) || size.X < 0 || size.Y < 0)
				throw new ArgumentOutOfRangeException(
					nameof(size),
					"Rounded rectangle size must be finite and non-negative."
				);
		}
	}
}
