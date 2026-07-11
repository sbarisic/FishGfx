using System;
using System.Numerics;

namespace FishGfx.Graphics
{
	internal static class RingTessellator
	{
		private const float SweepTolerance = 0.00001f;

		internal static Vector2[] Filled(
			Vector2 center,
			float innerRadius,
			float outerRadius,
			float startAngle,
			float endAngle,
			int segments = 0
		)
		{
			float sweep = Validate(center, innerRadius, outerRadius, startAngle, endAngle, segments);
			if (sweep == 0 || outerRadius == 0 || innerRadius == outerRadius)
				return Array.Empty<Vector2>();

			int resolvedSegments = ResolveSegments(outerRadius, sweep, segments);
			int verticesPerSegment = innerRadius == 0 ? 3 : 6;
			Vector2[] vertices = new Vector2[resolvedSegments * verticesPerSegment];
			for (int i = 0; i < resolvedSegments; i++)
			{
				float angle0 = startAngle + sweep * i / resolvedSegments;
				float angle1 = startAngle + sweep * (i + 1) / resolvedSegments;
				Vector2 direction0 = new Vector2(MathF.Cos(angle0), MathF.Sin(angle0));
				Vector2 direction1 = new Vector2(MathF.Cos(angle1), MathF.Sin(angle1));
				Vector2 outer0 = center + direction0 * outerRadius;
				Vector2 outer1 = center + direction1 * outerRadius;
				int offset = i * verticesPerSegment;

				if (innerRadius == 0)
				{
					vertices[offset] = center;
					vertices[offset + 1] = outer0;
					vertices[offset + 2] = outer1;
				}
				else
				{
					Vector2 inner0 = center + direction0 * innerRadius;
					Vector2 inner1 = center + direction1 * innerRadius;
					vertices[offset] = inner0;
					vertices[offset + 1] = outer0;
					vertices[offset + 2] = outer1;
					vertices[offset + 3] = inner0;
					vertices[offset + 4] = outer1;
					vertices[offset + 5] = inner1;
				}
			}
			return vertices;
		}

		internal static Vector2[][] Lines(
			Vector2 center,
			float innerRadius,
			float outerRadius,
			float startAngle,
			float endAngle,
			int segments = 0
		)
		{
			float sweep = Validate(center, innerRadius, outerRadius, startAngle, endAngle, segments);
			if (sweep == 0 || outerRadius == 0)
				return Array.Empty<Vector2[]>();

			int resolvedSegments = ResolveSegments(outerRadius, sweep, segments);
			Vector2[] outer = Arc(center, outerRadius, startAngle, sweep, resolvedSegments);
			bool fullRing = MathF.Abs(sweep - MathF.Tau) <= SweepTolerance;
			if (innerRadius == outerRadius)
			{
				if (fullRing)
					outer[^1] = outer[0];
				return new[] { outer };
			}

			if (fullRing)
			{
				outer[^1] = outer[0];
				if (innerRadius == 0)
					return new[] { outer };
				Vector2[] innerLoop = Arc(center, innerRadius, startAngle, sweep, resolvedSegments);
				innerLoop[^1] = innerLoop[0];
				return new[] { outer, innerLoop };
			}

			Vector2[] contour;
			if (innerRadius == 0)
			{
				contour = new Vector2[outer.Length + 2];
				Array.Copy(outer, contour, outer.Length);
				contour[^2] = center;
				contour[^1] = outer[0];
			}
			else
			{
				Vector2[] inner = Arc(center, innerRadius, startAngle, sweep, resolvedSegments);
				contour = new Vector2[outer.Length + inner.Length + 1];
				Array.Copy(outer, contour, outer.Length);
				for (int i = 0; i < inner.Length; i++)
					contour[outer.Length + i] = inner[inner.Length - 1 - i];
				contour[^1] = outer[0];
			}
			return new[] { contour };
		}

		internal static int ResolveSegments(float outerRadius, float sweep, int requestedSegments)
		{
			if (requestedSegments < 0)
				throw new ArgumentOutOfRangeException(nameof(requestedSegments));
			if (requestedSegments > 0)
				return requestedSegments;
			if (outerRadius == 0 || sweep == 0)
				return 0;
			int fullSegments = PrimitiveTessellator.ResolveArcSegments(outerRadius, 0);
			return Math.Max(1, (int)MathF.Ceiling(fullSegments * sweep / MathF.Tau));
		}

		private static Vector2[] Arc(Vector2 center, float radius, float startAngle, float sweep, int segments)
		{
			Vector2[] points = new Vector2[segments + 1];
			for (int i = 0; i <= segments; i++)
			{
				float angle = startAngle + sweep * i / segments;
				points[i] = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
			}
			return points;
		}

		private static float Validate(
			Vector2 center,
			float innerRadius,
			float outerRadius,
			float startAngle,
			float endAngle,
			int segments
		)
		{
			if (!float.IsFinite(center.X) || !float.IsFinite(center.Y))
				throw new ArgumentOutOfRangeException(nameof(center));
			if (!float.IsFinite(innerRadius) || innerRadius < 0)
				throw new ArgumentOutOfRangeException(nameof(innerRadius));
			if (!float.IsFinite(outerRadius) || outerRadius < 0)
				throw new ArgumentOutOfRangeException(nameof(outerRadius));
			if (innerRadius > outerRadius)
				throw new ArgumentOutOfRangeException(nameof(innerRadius), "Inner radius cannot exceed outer radius.");
			if (!float.IsFinite(startAngle) || !float.IsFinite(endAngle))
				throw new ArgumentOutOfRangeException(nameof(startAngle), "Ring angles must be finite.");
			if (segments < 0)
				throw new ArgumentOutOfRangeException(nameof(segments));

			float sweep = endAngle - startAngle;
			if (sweep < 0 || sweep > MathF.Tau + SweepTolerance)
				throw new ArgumentOutOfRangeException(
					nameof(endAngle),
					"Ring sweep must be between zero and one revolution."
				);
			return MathF.Min(sweep, MathF.Tau);
		}
	}
}
