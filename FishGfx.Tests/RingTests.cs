using System.Numerics;
using FishGfx.Graphics;
using Xunit;

namespace FishGfx.Tests;

public class RingTests
{
	[Fact]
	public void FilledAnnularSectorHasExpectedCountEndpointsAndWinding()
	{
		Vector2 center = new Vector2(10, 20);
		Vector2[] vertices = RingTessellator.Filled(center, 20, 40, 0, MathF.PI / 2, 2);
		Assert.Equal(12, vertices.Length);
		AssertVector(center + new Vector2(20, 0), vertices[0]);
		AssertVector(center + new Vector2(40, 0), vertices[1]);
		AssertVector(center + new Vector2(0, 40), vertices[8]);
		AssertVector(center + new Vector2(0, 20), vertices[^1]);

		for (int i = 0; i < vertices.Length; i += 3)
			AssertPositiveWinding(vertices[i], vertices[i + 1], vertices[i + 2]);
	}

	[Fact]
	public void ZeroInnerRadiusProducesNonDegenerateFan()
	{
		Vector2[] vertices = RingTessellator.Filled(Vector2.Zero, 0, 50, 0, MathF.PI, 4);
		Assert.Equal(12, vertices.Length);

		for (int i = 0; i < vertices.Length; i += 3)
		{
			Assert.Equal(Vector2.Zero, vertices[i]);
			AssertPositiveWinding(vertices[i], vertices[i + 1], vertices[i + 2]);
		}
	}

	[Fact]
	public void PartialLinesContainBothRadialCapsInOneClosedContour()
	{
		Vector2[][] paths = RingTessellator.Lines(Vector2.Zero, 20, 40, 0, MathF.PI / 2, 2);
		Assert.Single(paths);
		Vector2[] contour = paths[0];
		Assert.Equal(7, contour.Length);
		AssertVector(new Vector2(40, 0), contour[0]);
		AssertVector(new Vector2(0, 40), contour[2]);
		AssertVector(new Vector2(0, 20), contour[3]);
		AssertVector(new Vector2(20, 0), contour[5]);
		Assert.Equal(contour[0], contour[^1]);
	}

	[Fact]
	public void FullRingLinesAreSeparateClosedLoopsWithoutSeam()
	{
		Vector2[][] paths = RingTessellator.Lines(Vector2.Zero, 20, 40, 0, MathF.Tau, 12);
		Assert.Equal(2, paths.Length);
		Assert.All(
			paths,
			path =>
			{
				Assert.Equal(13, path.Length);
				Assert.Equal(path[0], path[^1]);
			}
		);
		Assert.InRange(paths[0].Select(point => point.Length()).Min(), 39.999f, 40.001f);
		Assert.InRange(paths[1].Select(point => point.Length()).Max(), 19.999f, 20.001f);
	}

	[Fact]
	public void CoincidentRadiiHaveNoFillAndOneLinePath()
	{
		Assert.Empty(RingTessellator.Filled(Vector2.Zero, 30, 30, 0, MathF.Tau, 8));
		Vector2[][] paths = RingTessellator.Lines(Vector2.Zero, 30, 30, 0, MathF.Tau, 8);
		Assert.Single(paths);
		Assert.Equal(paths[0][0], paths[0][^1]);
	}

	[Fact]
	public void AutomaticSegmentsScaleWithRadiusAndSweep()
	{
		int small = RingTessellator.ResolveSegments(10, MathF.Tau, 0);
		int large = RingTessellator.ResolveSegments(500, MathF.Tau, 0);
		int quarter = RingTessellator.ResolveSegments(500, MathF.PI / 2, 0);
		Assert.True(large > small);
		Assert.True(quarter < large);
		Assert.Equal(7, RingTessellator.ResolveSegments(500, MathF.PI / 2, 7));
	}

	[Fact]
	public void SupportsArbitraryAndZeroCrossingAngles()
	{
		Vector2[] arbitrary = RingTessellator.Filled(Vector2.Zero, 10, 20, MathF.PI / 4, MathF.PI * 3 / 4, 2);
		AssertVector(new Vector2(MathF.Sqrt(200), MathF.Sqrt(200)), arbitrary[1]);

		Vector2[][] crossing = RingTessellator.Lines(Vector2.Zero, 10, 20, MathF.PI * 1.5f, MathF.PI * 2.5f, 8);
		Assert.Single(crossing);
		Assert.Equal(crossing[0][0], crossing[0][^1]);

		Vector2[][] fullFromArbitraryStart = RingTessellator.Lines(
			Vector2.Zero,
			10,
			20,
			MathF.PI * 1.5f,
			MathF.PI * 3.5f,
			8
		);
		Assert.Equal(2, fullFromArbitraryStart.Length);
	}

	[Fact]
	public void ZeroSweepAndRadiusProduceNoGeometry()
	{
		Assert.Empty(RingTessellator.Filled(Vector2.Zero, 10, 20, 1, 1));
		Assert.Empty(RingTessellator.Lines(Vector2.Zero, 10, 20, 1, 1));
		Assert.Empty(RingTessellator.Filled(Vector2.Zero, 0, 0, 0, MathF.Tau));
		Assert.Empty(RingTessellator.Lines(Vector2.Zero, 0, 0, 0, MathF.Tau));
	}

	[Fact]
	public void InvalidInputsThrow()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => RingTessellator.Filled(new Vector2(float.NaN, 0), 1, 2, 0, 1));
		Assert.Throws<ArgumentOutOfRangeException>(() => RingTessellator.Filled(Vector2.Zero, -1, 2, 0, 1));
		Assert.Throws<ArgumentOutOfRangeException>(() => RingTessellator.Filled(Vector2.Zero, 3, 2, 0, 1));
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			RingTessellator.Filled(Vector2.Zero, 1, float.PositiveInfinity, 0, 1)
		);
		Assert.Throws<ArgumentOutOfRangeException>(() => RingTessellator.Filled(Vector2.Zero, 1, 2, 2, 1));
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			RingTessellator.Filled(Vector2.Zero, 1, 2, 0, MathF.Tau + 0.1f)
		);
		Assert.Throws<ArgumentOutOfRangeException>(() => RingTessellator.Filled(Vector2.Zero, 1, 2, 0, 1, -1));
	}

	private static void AssertPositiveWinding(Vector2 a, Vector2 b, Vector2 c)
	{
		Vector2 ab = b - a;
		Vector2 ac = c - a;
		Assert.True(ab.X * ac.Y - ab.Y * ac.X > 0);
	}

	private static void AssertVector(Vector2 expected, Vector2 actual)
	{
		Assert.InRange(Vector2.Distance(expected, actual), 0, 0.001f);
	}
}
