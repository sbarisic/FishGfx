using System;
using System.Collections.Generic;
using System.Numerics;

namespace FishGfx.Graphics {
	internal static class PrimitiveTessellator {
		private const float ArcError = 0.25f;
		private const float CurveSegmentLength = 8f;

		internal static int ResolveArcSegments(float radius, int requestedSegments) {
			ValidateNonNegativeFinite(radius, nameof(radius));
			if (requestedSegments < 0 || requestedSegments is > 0 and < 3)
				throw new ArgumentOutOfRangeException(nameof(requestedSegments), "Arc segment counts must be zero or at least three.");
			if (requestedSegments > 0)
				return requestedSegments;
			if (radius == 0)
				return 0;

			float cosine = Math.Clamp(1f - ArcError / radius, -1f, 1f);
			float angle = MathF.Acos(cosine);
			int segments = angle > 0 ? (int)MathF.Ceiling(MathF.PI / angle) : 512;
			return Math.Clamp(segments, 12, 512);
		}

		internal static int ResolveCurveSegments(int requestedSegments, params Vector2[] controlPoints) {
			if (requestedSegments < 0 || requestedSegments == 1)
				throw new ArgumentOutOfRangeException(nameof(requestedSegments), "Curve segment counts must be zero or at least two.");
			foreach (Vector2 point in controlPoints)
				ValidateFinite(point, nameof(controlPoints));
			if (requestedSegments > 0)
				return requestedSegments;

			float polygonLength = 0;
			for (int i = 1; i < controlPoints.Length; i++)
				polygonLength += Vector2.Distance(controlPoints[i - 1], controlPoints[i]);
			return Math.Clamp((int)MathF.Ceiling(polygonLength / CurveSegmentLength), 8, 512);
		}

		internal static Vector2[] EllipseOutline(Vector2 center, Vector2 radii, int segments = 0) {
			ValidateEllipse(center, radii);
			int resolvedSegments = ResolveArcSegments(MathF.Max(radii.X, radii.Y), segments);
			if (radii.X == 0 || radii.Y == 0)
				return Array.Empty<Vector2>();

			Vector2[] vertices = new Vector2[resolvedSegments + 1];
			for (int i = 0; i < resolvedSegments; i++) {
				float angle = MathF.Tau * i / resolvedSegments;
				vertices[i] = center + new Vector2(MathF.Cos(angle) * radii.X, MathF.Sin(angle) * radii.Y);
			}
			vertices[resolvedSegments] = vertices[0];
			return vertices;
		}

		internal static Vector2[] FilledEllipse(Vector2 center, Vector2 radii, int segments = 0) {
			ValidateEllipse(center, radii);
			int resolvedSegments = ResolveArcSegments(MathF.Max(radii.X, radii.Y), segments);
			if (radii.X == 0 || radii.Y == 0)
				return Array.Empty<Vector2>();

			Vector2[] vertices = new Vector2[resolvedSegments * 3];
			for (int i = 0; i < resolvedSegments; i++) {
				float angle0 = MathF.Tau * i / resolvedSegments;
				float angle1 = MathF.Tau * (i + 1) / resolvedSegments;
				int offset = i * 3;
				vertices[offset] = center;
				vertices[offset + 1] = center + new Vector2(MathF.Cos(angle0) * radii.X, MathF.Sin(angle0) * radii.Y);
				vertices[offset + 2] = center + new Vector2(MathF.Cos(angle1) * radii.X, MathF.Sin(angle1) * radii.Y);
			}
			return vertices;
		}

		internal static Vector2[] QuadraticBezier(Vector2 start, Vector2 control, Vector2 end, int segments = 0) {
			int resolvedSegments = ResolveCurveSegments(segments, start, control, end);
			Vector2[] vertices = new Vector2[resolvedSegments + 1];
			for (int i = 0; i <= resolvedSegments; i++) {
				float t = i / (float)resolvedSegments;
				float inverse = 1 - t;
				vertices[i] = inverse * inverse * start + 2 * inverse * t * control + t * t * end;
			}
			return vertices;
		}

		internal static Vector2[] CubicBezier(Vector2 start, Vector2 control1, Vector2 control2, Vector2 end, int segments = 0) {
			int resolvedSegments = ResolveCurveSegments(segments, start, control1, control2, end);
			Vector2[] vertices = new Vector2[resolvedSegments + 1];
			for (int i = 0; i <= resolvedSegments; i++) {
				float t = i / (float)resolvedSegments;
				float inverse = 1 - t;
				vertices[i] = inverse * inverse * inverse * start
					+ 3 * inverse * inverse * t * control1
					+ 3 * inverse * t * t * control2
					+ t * t * t * end;
			}
			return vertices;
		}

		internal static void ValidateThickness(float thickness) {
			if (!float.IsFinite(thickness) || thickness <= 0)
				throw new ArgumentOutOfRangeException(nameof(thickness), "Thickness must be finite and greater than zero.");
		}

		internal static Vertex2[] TextureVertices(Vector2[] positions, Vector2 boundsMin, Vector2 boundsSize,
			Vector2 uvMin, Vector2 uvMax, Color color) {
			ValidateFinite(boundsMin, nameof(boundsMin));
			ValidateFinite(boundsSize, nameof(boundsSize));
			ValidateFinite(uvMin, nameof(uvMin));
			ValidateFinite(uvMax, nameof(uvMax));
			if (boundsSize.X < 0 || boundsSize.Y < 0)
				throw new ArgumentOutOfRangeException(nameof(boundsSize));
			if (uvMin.X > uvMax.X || uvMin.Y > uvMax.Y)
				throw new ArgumentOutOfRangeException(nameof(uvMax), "UV maximum must not be less than UV minimum.");
			if (positions.Length == 0)
				return Array.Empty<Vertex2>();
			if (boundsSize.X == 0 || boundsSize.Y == 0)
				throw new ArgumentOutOfRangeException(nameof(boundsSize));

			Vector2 uvSize = uvMax - uvMin;
			Vertex2[] vertices = new Vertex2[positions.Length];
			for (int i = 0; i < positions.Length; i++) {
				ValidateFinite(positions[i], nameof(positions));
				Vector2 normalized = (positions[i] - boundsMin) / boundsSize;
				vertices[i] = new Vertex2(positions[i], uvMin + normalized * uvSize, color);
			}
			return vertices;
		}

		private static void ValidateEllipse(Vector2 center, Vector2 radii) {
			ValidateFinite(center, nameof(center));
			ValidateNonNegativeFinite(radii.X, nameof(radii));
			ValidateNonNegativeFinite(radii.Y, nameof(radii));
		}

		private static void ValidateFinite(Vector2 value, string parameterName) {
			if (!float.IsFinite(value.X) || !float.IsFinite(value.Y))
				throw new ArgumentOutOfRangeException(parameterName, "Coordinates must be finite.");
		}

		private static void ValidateNonNegativeFinite(float value, string parameterName) {
			if (!float.IsFinite(value) || value < 0)
				throw new ArgumentOutOfRangeException(parameterName, "Values must be finite and non-negative.");
		}
	}
}
