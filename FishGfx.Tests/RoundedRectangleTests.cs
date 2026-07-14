using System.Numerics;
using FishGfx;
using FishGfx.Graphics;
using Xunit;

namespace FishGfx.Tests;

public class RoundedRectangleTests
{
	[Fact]
	public void OutlineIsClosedAndUsesExplicitSegmentsPerCorner()
	{
		Vector2[] vertices = RoundedRectangleTessellator.Outline(
			Vector2.Zero,
			new Vector2(200, 100),
			new CornerRadii(20),
			2
		);
		Assert.Equal(13, vertices.Length);
		Assert.Equal(vertices[0], vertices[^1]);
		AssertVector(new Vector2(180, 0), vertices[0]);
		AssertVector(new Vector2(200, 20), vertices[2]);
		AssertVector(new Vector2(200, 80), vertices[3]);
	}

	[Fact]
	public void AsymmetricRadiiProduceExpectedCornerEndpoints()
	{
		Vector2[] vertices = RoundedRectangleTessellator.Outline(
			new Vector2(10, 20),
			new Vector2(300, 200),
			new CornerRadii(10, 20, 30, 40),
			1
		);
		AssertVector(new Vector2(280, 20), vertices[0]);
		AssertVector(new Vector2(310, 50), vertices[1]);
		AssertVector(new Vector2(310, 200), vertices[2]);
		AssertVector(new Vector2(290, 220), vertices[3]);
		AssertVector(new Vector2(20, 220), vertices[4]);
		AssertVector(new Vector2(10, 210), vertices[5]);
	}

	[Fact]
	public void CssStyleClampingPreservesRadiusProportions()
	{
		CornerRadii clamped = RoundedRectangleTessellator.Clamp(new Vector2(100, 50), new CornerRadii(100));
		Assert.Equal(new CornerRadii(25), clamped);

		CornerRadii asymmetric = RoundedRectangleTessellator.Clamp(
			new Vector2(100, 80),
			new CornerRadii(80, 40, 20, 20)
		);
		Assert.Equal(new CornerRadii(64, 32, 16, 16), asymmetric);
	}

	[Fact]
	public void ZeroRadiiProduceSharpCorners()
	{
		Vector2[] vertices = RoundedRectangleTessellator.Outline(
			new Vector2(10, 20),
			new Vector2(100, 50),
			new CornerRadii(0),
			0
		);
		Assert.Equal(
			new[]
			{
				new Vector2(110, 20),
				new Vector2(110, 70),
				new Vector2(10, 70),
				new Vector2(10, 20),
				new Vector2(110, 20),
			},
			vertices
		);
	}

	[Fact]
	public void FilledFanHasPositiveWindingAndExpectedCount()
	{
		Vector2[] vertices = RoundedRectangleTessellator.Filled(
			Vector2.Zero,
			new Vector2(200, 100),
			new CornerRadii(20),
			2
		);
		Assert.Equal(36, vertices.Length);

		for (int i = 0; i < vertices.Length; i += 3)
		{
			Vector2 a = vertices[i + 1] - vertices[i];
			Vector2 b = vertices[i + 2] - vertices[i];
			Assert.True(a.X * b.Y - a.Y * b.X > 0);
		}
	}

	[Fact]
	public void AdaptiveCornerQualityGrowsWithRadius()
	{
		int small = RoundedRectangleTessellator.ResolveCornerSegments(5, 0);
		int large = RoundedRectangleTessellator.ResolveCornerSegments(500, 0);
		Assert.True(large > small);
		Assert.Equal(7, RoundedRectangleTessellator.ResolveCornerSegments(500, 7));
	}

	[Fact]
	public void PlanarTextureMappingCoversBoundsAndPropagatesTint()
	{
		Vector2 min = new Vector2(10, 20);
		Vector2 size = new Vector2(100, 50);
		Color tint = new Color(10, 20, 30, 40);
		Vector2[] positions = { min, min + size, min + new Vector2(50, 25) };
		Vertex2[] vertices = PrimitiveTessellator.TextureVertices(
			positions,
			min,
			size,
			new Vector2(0.2f, 0.3f),
			new Vector2(0.8f, 0.9f),
			tint
		);
		Assert.Equal(new Vector2(0.2f, 0.3f), vertices[0].UV);
		Assert.Equal(new Vector2(0.8f, 0.9f), vertices[1].UV);
		Assert.Equal(new Vector2(0.5f, 0.6f), vertices[2].UV);
		Assert.All(vertices, vertex => Assert.Equal(tint, vertex.Color));
	}

	[Fact]
	public void ZeroSizedShapesProduceNoGeometry()
	{
		Assert.Empty(RoundedRectangleTessellator.Outline(Vector2.Zero, new Vector2(0, 20), new CornerRadii(5)));
		Assert.Empty(RoundedRectangleTessellator.Filled(Vector2.Zero, new Vector2(20, 0), new CornerRadii(5)));
		Assert.Empty(
			PrimitiveTessellator.TextureVertices(
				Array.Empty<Vector2>(),
				Vector2.Zero,
				Vector2.Zero,
				Vector2.Zero,
				Vector2.One,
				Color.White
			)
		);
	}

	[Fact]
	public void InvalidInputsThrow()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new CornerRadii(-1));
		Assert.Throws<ArgumentOutOfRangeException>(() => new CornerRadii(float.NaN));
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			RoundedRectangleTessellator.Outline(Vector2.Zero, new Vector2(-1, 10), new CornerRadii(1))
		);
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			RoundedRectangleTessellator.Outline(new Vector2(float.NaN, 0), Vector2.One, new CornerRadii(1))
		);
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			RoundedRectangleTessellator.Outline(Vector2.Zero, Vector2.One, new CornerRadii(1), -1)
		);
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			PrimitiveTessellator.TextureVertices(
				new[] { Vector2.Zero },
				Vector2.Zero,
				Vector2.One,
				new Vector2(1, 0),
				new Vector2(0, 1),
				Color.White
			)
		);
		Assert.Throws<ArgumentNullException>(() =>
			new TexturedEllipseCommand(
				Vector2.Zero,
				Vector2.One,
				null,
				Vector2.Zero,
				Vector2.One
			)
		);
		Assert.Throws<ArgumentNullException>(() =>
			new TexturedRoundedRectangleCommand(
				Vector2.Zero,
				new Vector2(10),
				new CornerRadii(2),
				null,
				Vector2.Zero,
				Vector2.One
			)
		);
	}

	private static void AssertVector(Vector2 expected, Vector2 actual)
	{
		Assert.InRange(Vector2.Distance(expected, actual), 0, 0.001f);
	}
}
