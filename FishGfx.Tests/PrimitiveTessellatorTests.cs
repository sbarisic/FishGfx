using System.Numerics;
using FishGfx.Graphics;
using Xunit;

namespace FishGfx.Tests;

public class PrimitiveTessellatorTests
{
	[Fact]
	public void OutlineIsClosedAndHasExpectedVertexCount()
	{
		Vector2[] vertices = PrimitiveTessellator.EllipseOutline(new Vector2(10, 20), new Vector2(30), 16);
		Assert.Equal(17, vertices.Length);
		Assert.Equal(vertices[0], vertices[^1]);
	}

	[Fact]
	public void FilledEllipseProducesCounterClockwiseTriangleFan()
	{
		Vector2 center = new Vector2(10, 20);
		Vector2[] vertices = PrimitiveTessellator.FilledEllipse(center, new Vector2(30, 15), 12);
		Assert.Equal(36, vertices.Length);

		for (int i = 0; i < vertices.Length; i += 3)
		{
			Assert.Equal(center, vertices[i]);
			Vector2 a = vertices[i + 1] - center;
			Vector2 b = vertices[i + 2] - center;
			Assert.True(a.X * b.Y - a.Y * b.X > 0);
		}
	}

	[Fact]
	public void CircleVerticesStayOnRequestedRadius()
	{
		Vector2 center = new Vector2(25, -40);
		Vector2[] vertices = PrimitiveTessellator.EllipseOutline(center, new Vector2(75), 32);

		foreach (Vector2 vertex in vertices)
		{
			Assert.InRange(Vector2.Distance(center, vertex), 74.999f, 75.001f);
		}
	}

	[Fact]
	public void EllipseContainsExpectedExtrema()
	{
		Vector2[] vertices = PrimitiveTessellator.EllipseOutline(Vector2.Zero, new Vector2(40, 20), 4);
		Assert.Contains(new Vector2(40, 0), vertices, ApproximateVectorComparer.Instance);
		Assert.Contains(new Vector2(-40, 0), vertices, ApproximateVectorComparer.Instance);
		Assert.Contains(new Vector2(0, 20), vertices, ApproximateVectorComparer.Instance);
		Assert.Contains(new Vector2(0, -20), vertices, ApproximateVectorComparer.Instance);
	}

	[Fact]
	public void QuadraticBezierHasExactEndpointsAndMidpoint()
	{
		Vector2[] vertices = PrimitiveTessellator.QuadraticBezier(
			Vector2.Zero,
			new Vector2(10, 20),
			new Vector2(20, 0),
			2
		);
		Assert.Equal(Vector2.Zero, vertices[0]);
		Assert.Equal(new Vector2(10, 10), vertices[1]);
		Assert.Equal(new Vector2(20, 0), vertices[^1]);
	}

	[Fact]
	public void CubicBezierHasExactEndpointsAndMidpoint()
	{
		Vector2[] vertices = PrimitiveTessellator.CubicBezier(
			Vector2.Zero,
			new Vector2(0, 20),
			new Vector2(20, 20),
			new Vector2(20, 0),
			2
		);
		Assert.Equal(Vector2.Zero, vertices[0]);
		Assert.Equal(new Vector2(10, 15), vertices[1]);
		Assert.Equal(new Vector2(20, 0), vertices[^1]);
	}

	[Fact]
	public void AutomaticSegmentCountsGrowAndStayBounded()
	{
		int smallArc = PrimitiveTessellator.ResolveArcSegments(5, 0);
		int largeArc = PrimitiveTessellator.ResolveArcSegments(500, 0);
		int smallCurve = PrimitiveTessellator.ResolveCurveSegments(
			0,
			Vector2.Zero,
			new Vector2(20, 0),
			new Vector2(40, 0)
		);
		int largeCurve = PrimitiveTessellator.ResolveCurveSegments(
			0,
			Vector2.Zero,
			new Vector2(1000, 0),
			new Vector2(2000, 0)
		);
		Assert.InRange(smallArc, 12, 512);
		Assert.InRange(largeArc, 12, 512);
		Assert.True(largeArc > smallArc);
		Assert.InRange(smallCurve, 8, 512);
		Assert.InRange(largeCurve, 8, 512);
		Assert.True(largeCurve > smallCurve);
	}

	[Fact]
	public void ZeroRadiusProducesNoGeometry()
	{
		Assert.Empty(PrimitiveTessellator.EllipseOutline(Vector2.Zero, Vector2.Zero));
		Assert.Empty(PrimitiveTessellator.FilledEllipse(Vector2.Zero, new Vector2(0, 10)));
	}

	[Fact]
	public void InvalidInputsThrow()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			PrimitiveTessellator.EllipseOutline(Vector2.Zero, new Vector2(-1, 1))
		);
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			PrimitiveTessellator.EllipseOutline(new Vector2(float.NaN, 0), Vector2.One)
		);
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			PrimitiveTessellator.EllipseOutline(Vector2.Zero, Vector2.One, 2)
		);
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			PrimitiveTessellator.QuadraticBezier(Vector2.Zero, Vector2.One, Vector2.One, 1)
		);
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			PrimitiveTessellator.CubicBezier(
				Vector2.Zero,
				Vector2.One,
				Vector2.One,
				new Vector2(float.PositiveInfinity, 0)
			)
		);
		Assert.Throws<ArgumentOutOfRangeException>(() => PrimitiveTessellator.ValidateThickness(0));
		Assert.Throws<ArgumentOutOfRangeException>(() => PrimitiveTessellator.ValidateThickness(float.NaN));
	}

	private sealed class ApproximateVectorComparer : IEqualityComparer<Vector2>
	{
		public static readonly ApproximateVectorComparer Instance = new();

		public bool Equals(Vector2 left, Vector2 right) => Vector2.Distance(left, right) < 0.001f;

		public int GetHashCode(Vector2 value) => 0;
	}
}
