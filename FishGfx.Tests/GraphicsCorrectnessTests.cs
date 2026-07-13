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
		Assert.Equal(first, Color.Clamp(new Color(11, 20, 30, 40), new[] { first, second }));
		Assert.Equal(second, Color.Clamp(new Color(12, 20, 30, 40), new[] { first, second }));
		Assert.Throws<ArgumentNullException>(() => Color.Clamp(Color.Black, null));
		Assert.Throws<ArgumentException>(() => Color.Clamp(Color.Black, Array.Empty<Color>()));
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
	public void PerspectivePickingRayPointsThroughViewportCenter()
	{
		Camera camera = new();
		camera.SetPerspective(800, 600, NearPlane: 0.1f, FarPlane: 100);

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
