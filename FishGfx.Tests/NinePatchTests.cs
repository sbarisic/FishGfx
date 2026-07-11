using System.Numerics;
using FishGfx;
using FishGfx.Graphics;
using Xunit;

namespace FishGfx.Tests;

public class NinePatchTests
{
	private static readonly NinePatchInsets Insets = new(64);

	[Fact]
	public void ProducesNineQuadsInOneTriangleList()
	{
		Vertex2[] vertices = Create(new Vector2(512, 384));
		Assert.Equal(54, vertices.Length);
		for (int patch = 0; patch < 9; patch++)
		{
			Vertex2[] quad = vertices.Skip(patch * 6).Take(6).ToArray();
			Assert.Equal(quad[0].Position, quad[3].Position);
			Assert.Equal(quad[1].Position, quad[5].Position);
		}
	}

	[Fact]
	public void UsesExpectedDestinationAndUvBoundaries()
	{
		Vertex2[] vertices = Create(new Vector2(512, 384));
		Assert.Equal(new Vector2(10, 20), vertices[0].Position);
		Assert.Equal(new Vector2(74, 84), vertices[1].Position);
		Assert.Equal(Vector2.Zero, vertices[0].UV);
		Assert.Equal(new Vector2(0.25f), vertices[1].UV);

		Vertex2[] topRight = vertices.Skip(8 * 6).Take(6).ToArray();
		Assert.Equal(new Vector2(458, 340), topRight[0].Position);
		Assert.Equal(new Vector2(522, 404), topRight[1].Position);
		Assert.Equal(new Vector2(0.75f), topRight[0].UV);
		Assert.Equal(Vector2.One, topRight[1].UV);
	}

	[Fact]
	public void KeepsCornersUnscaledWhenDestinationFits()
	{
		Vertex2[] vertices = Create(new Vector2(600, 400));
		(Vector2 min, Vector2 max) = Bounds(vertices, 0);
		Assert.Equal(new Vector2(10, 20), min);
		Assert.Equal(new Vector2(74, 84), max);
		(min, max) = Bounds(vertices, 8);
		Assert.Equal(new Vector2(546, 356), min);
		Assert.Equal(new Vector2(610, 420), max);
	}

	[Fact]
	public void ScalesBordersAndCollapsesCenterWhenUndersized()
	{
		Vertex2[] vertices = Create(new Vector2(96, 64));
		Assert.Equal(58, Bounds(vertices, 0).max.X);
		Assert.Equal(58, Bounds(vertices, 1).min.X);
		Assert.Equal(58, Bounds(vertices, 1).max.X);
		Assert.Equal(58, Bounds(vertices, 2).min.X);
		Assert.Equal(52, Bounds(vertices, 0).max.Y);
		Assert.Equal(52, Bounds(vertices, 3).min.Y);
		Assert.Equal(52, Bounds(vertices, 3).max.Y);
		Assert.Equal(52, Bounds(vertices, 6).min.Y);
	}

	[Fact]
	public void AdjacentPatchesShareBoundaries()
	{
		Vertex2[] vertices = Create(new Vector2(700, 500));
		for (int row = 0; row < 3; row++)
		{
			Assert.Equal(Bounds(vertices, row * 3).max.X, Bounds(vertices, row * 3 + 1).min.X);
			Assert.Equal(Bounds(vertices, row * 3 + 1).max.X, Bounds(vertices, row * 3 + 2).min.X);
		}
		for (int column = 0; column < 3; column++)
		{
			Assert.Equal(Bounds(vertices, column).max.Y, Bounds(vertices, column + 3).min.Y);
			Assert.Equal(Bounds(vertices, column + 3).max.Y, Bounds(vertices, column + 6).min.Y);
		}
	}

	[Fact]
	public void BottomAndTopInsetsMapToCorrectVCoordinates()
	{
		Vertex2[] vertices = NinePatchTessellator.Create(
			Vector2.Zero,
			new Vector2(300),
			new Vector2(200),
			new NinePatchInsets(20, 50, 30, 40),
			Color.White
		);
		Assert.Equal(0, BoundsUv(vertices, 0).min.Y);
		Assert.Equal(0.2f, BoundsUv(vertices, 0).max.Y);
		Assert.Equal(0.75f, BoundsUv(vertices, 6).min.Y);
		Assert.Equal(1, BoundsUv(vertices, 6).max.Y);
	}

	[Fact]
	public void PropagatesTintToEveryVertex()
	{
		Color tint = new Color(12, 34, 56, 78);
		Vertex2[] vertices = NinePatchTessellator.Create(
			Vector2.Zero,
			new Vector2(300),
			new Vector2(256),
			Insets,
			tint
		);
		Assert.All(vertices, vertex => Assert.Equal(tint, vertex.Color));
	}

	[Fact]
	public void ZeroDestinationProducesNoGeometry()
	{
		Assert.Empty(
			NinePatchTessellator.Create(Vector2.Zero, new Vector2(0, 100), new Vector2(256), Insets, Color.White)
		);
		Assert.Empty(
			NinePatchTessellator.Create(Vector2.Zero, new Vector2(100, 0), new Vector2(256), Insets, Color.White)
		);
	}

	[Fact]
	public void InvalidInputsThrow()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new NinePatchInsets(-1));
		Assert.Throws<ArgumentOutOfRangeException>(() => new NinePatchInsets(float.NaN));
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			NinePatchTessellator.Create(Vector2.Zero, new Vector2(-1, 20), new Vector2(256), Insets, Color.White)
		);
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			NinePatchTessellator.Create(new Vector2(float.NaN, 0), Vector2.One, new Vector2(256), Insets, Color.White)
		);
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			NinePatchTessellator.Create(Vector2.Zero, Vector2.One, new Vector2(100), Insets, Color.White)
		);
		Assert.Throws<ArgumentNullException>(() => Gfx.NinePatch(0, 0, 100, 100, null, Insets));
	}

	private static Vertex2[] Create(Vector2 size) =>
		NinePatchTessellator.Create(new Vector2(10, 20), size, new Vector2(256), Insets, Color.White);

	private static (Vector2 min, Vector2 max) Bounds(Vertex2[] vertices, int patch) =>
		Bounds(vertices.Skip(patch * 6).Take(6));

	private static (Vector2 min, Vector2 max) BoundsUv(Vertex2[] vertices, int patch) =>
		Bounds(vertices.Skip(patch * 6).Take(6).Select(vertex => vertex.UV));

	private static (Vector2 min, Vector2 max) Bounds(IEnumerable<Vertex2> vertices) =>
		Bounds(vertices.Select(vertex => vertex.Position));

	private static (Vector2 min, Vector2 max) Bounds(IEnumerable<Vector2> values)
	{
		return (
			new Vector2(values.Min(value => value.X), values.Min(value => value.Y)),
			new Vector2(values.Max(value => value.X), values.Max(value => value.Y))
		);
	}
}
