using System;
using System.Numerics;
using FishGfx;
using FishGfx.Graphics;
using Xunit;

namespace FishGfx.Tests;

public class GraphicsCorrectnessTests
{
	[Fact]
	public void PaletteClampUsesRgbaDistanceAndFirstTie()
	{
		Color first = new(10, 20, 30, 40);
		Color second = new(12, 20, 30, 40);
		Assert.Equal(first, Color.ClampToPalette(new Color(11, 20, 30, 40), new[] { first, second }));
		Assert.Equal(second, Color.ClampToPalette(new Color(12, 20, 30, 40), new[] { first, second }));
		Assert.Throws<ArgumentNullException>(() => Color.ClampToPalette(Color.Black, null));
		Assert.Throws<ArgumentException>(() => Color.ClampToPalette(Color.Black, Array.Empty<Color>()));
	}

	[Fact]
	public void QuaternionEulerConversionIncludesRoll()
	{
		const float pitch = 20;
		const float yaw = -15;
		const float roll = 35;
		Quaternion quaternion = Quaternion.CreateFromYawPitchRoll(
			Degrees(yaw),
			Degrees(pitch),
			Degrees(roll)
		);

		quaternion.GetEulerAngles(out float actualPitch, out float actualYaw, out float actualRoll);

		Assert.Equal(pitch, actualPitch, 3);
		Assert.Equal(yaw, actualYaw, 3);
		Assert.Equal(roll, actualRoll, 3);
		Assert.Throws<ArgumentException>(() => Quaternion.Zero.GetEulerAngles(out _, out _, out _));
	}

	[Fact]
	public void CameraProjectsAndUnprojectsViewportCoordinates()
	{
		Camera camera = new();
		camera.SetOrthogonal(0, 0, 100, 100, -1, 1);
		Vector3 world = new(25, 75, 0);

		Vector3 screen = camera.ProjectToViewport(world);

		Assert.Equal(25, screen.X, 4);
		Assert.Equal(25, screen.Y, 4);
		Assert.True(camera.TryUnproject(screen, out Vector3 restored));
		AssertVector(world, restored);
	}

	[Fact]
	public void OrthographicProjectionUsesPixelViewportIndependentlyFromWorldExtents()
	{
		Camera camera = new();
		camera.SetOrthogonal(
			-100,
			-50,
			100,
			50,
			new Vector2(1200, 600),
			-1,
			1
		);

		Vector3 topLeft = camera.ProjectToViewport(new Vector3(-100, 50, 0));
		Vector3 bottomRight = camera.ProjectToViewport(new Vector3(100, -50, 0));

		Assert.Equal(new Vector2(1200, 600), camera.ViewportSize);
		Assert.Equal(0, topLeft.X, 4);
		Assert.Equal(0, topLeft.Y, 4);
		Assert.Equal(1200, bottomRight.X, 4);
		Assert.Equal(600, bottomRight.Y, 4);
		Assert.True(camera.TryUnproject(topLeft, out Vector3 restoredTopLeft));
		Assert.True(camera.TryUnproject(bottomRight, out Vector3 restoredBottomRight));
		AssertVector(new Vector3(-100, 50, 0), restoredTopLeft);
		AssertVector(new Vector3(100, -50, 0), restoredBottomRight);
	}

	[Fact]
	public void OrthographicPickingRayUsesPixelViewportCenter()
	{
		Camera camera = new();
		camera.SetOrthogonal(
			-200,
			-100,
			200,
			100,
			new Vector2(1000, 500),
			0.1f,
			100
		);

		PickingRay ray = camera.CreatePickingRay(new Vector2(500, 250));

		Assert.InRange(ray.Origin.X, -0.0001f, 0.0001f);
		Assert.InRange(ray.Origin.Y, -0.0001f, 0.0001f);
		Assert.InRange(ray.Direction.X, -0.0001f, 0.0001f);
		Assert.InRange(ray.Direction.Y, -0.0001f, 0.0001f);
		Assert.InRange(ray.Direction.Z, -1.0001f, -0.9999f);
	}

	[Fact]
	public void PerspectivePickingRayPointsThroughViewportCenter()
	{
		Camera camera = new();
		camera.SetPerspective(800, 600, nearPlane: 0.1f, farPlane: 100);

		PickingRay ray = camera.CreatePickingRay(new Vector2(400, 300));

		Assert.InRange(ray.Direction.X, -0.0001f, 0.0001f);
		Assert.InRange(ray.Direction.Y, -0.0001f, 0.0001f);
		Assert.InRange(ray.Direction.Z, -1.0001f, -0.9999f);
	}

	private static float Degrees(float value) => value * MathF.PI / 180;

	private static void AssertVector(Vector3 expected, Vector3 actual)
	{
		Assert.Equal(expected.X, actual.X, 4);
		Assert.Equal(expected.Y, actual.Y, 4);
		Assert.Equal(expected.Z, actual.Z, 4);
	}
}
