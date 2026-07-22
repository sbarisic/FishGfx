using System.Numerics;
using FishGfx.Cad;
using FishGfx.Graphics;
using Xunit;

namespace FishGfx.ManifoldCad.Tests;

public sealed class CadLayoutTests
{
	[Fact]
	public void InputManagerCoordinatesSelectCorrectCadRegion()
	{
		const int width = 1600;
		const int height = 1000;
		CadRect viewport = CadLayout.Viewport(width, height);
		CadRect graph = CadLayout.Graph(width);
		Vector2 viewportPoint = viewport.Minimum + new Vector2(20);
		Vector2 graphPoint = graph.Minimum + new Vector2(20);

		Assert.True(viewport.Contains(viewportPoint));
		Assert.False(graph.Contains(viewportPoint));
		Assert.True(graph.Contains(graphPoint));
		Assert.False(viewport.Contains(graphPoint));
	}

	[Fact]
	public void ViewportLayoutAndCameraPointsRoundTripThroughFlippedComposite()
	{
		CadRect viewport = CadLayout.Viewport(1600, 1000);
		Vector2 bottomLeft = viewport.Minimum;
		Vector2 topRight = viewport.Maximum;

		Assert.Equal(Vector2.Zero, CadViewport.ToCameraPoint(
			viewport,
			bottomLeft
		));
		Assert.Equal(new Vector2(viewport.Width, viewport.Height), CadViewport.ToCameraPoint(
			viewport,
			topRight
		));
		Assert.Equal(bottomLeft, CadViewport.FromCameraPoint(viewport, Vector2.Zero));
		Assert.Equal(topRight, CadViewport.FromCameraPoint(
			viewport,
			new Vector2(viewport.Width, viewport.Height)
		));
	}

	[Fact]
	public void NativeCursorRoundTripsThroughInputAndFlippedViewport()
	{
		const int windowHeight = 1000;
		CadRect viewport = CadLayout.Viewport(1600, windowHeight);
		Vector2 nativeCursor = new(747, 590);
		Vector2 layoutCursor = new(nativeCursor.X, windowHeight - nativeCursor.Y);

		Vector2 cameraPoint = CadViewport.ToCameraPoint(viewport, layoutCursor);
		Vector2 restoredLayout = CadViewport.FromCameraPoint(viewport, cameraPoint);
		Vector2 restoredNative = new(restoredLayout.X, windowHeight - restoredLayout.Y);

		Assert.Equal(new Vector2(487, 90), cameraPoint);
		Assert.Equal(nativeCursor, restoredNative);
	}

	[Fact]
	public void PanPreservesHorizontalDirectionAndCorrectsVerticalDirection()
	{
		Vector3 focus = CadViewport.PanFocus(
			Vector3.Zero,
			Vector3.UnitX,
			Vector3.UnitY,
			new Vector2(20, 30),
			0.5f
		);

		Assert.Equal(new Vector3(-10, 15, 0), focus);
	}

	[Fact]
	public void CandidateSpherePickingUsesRenderedVolume()
	{
		PickingRay centered = new(new Vector3(0, 0, 10), -Vector3.UnitZ);
		PickingRay missed = new(new Vector3(2, 0, 10), -Vector3.UnitZ);

		Assert.True(CadViewport.TryIntersectSphere(centered, Vector3.Zero, 1, out float distance));
		Assert.Equal(9, distance, 4);
		Assert.False(CadViewport.TryIntersectSphere(missed, Vector3.Zero, 1, out _));
	}

	[Fact]
	public void EdgePickingInterpolatesBetweenPolylineVertices()
	{
		float amount = CadViewport.ClosestSegmentAmount(
			new Vector2(75, 15),
			new Vector2(0, 10),
			new Vector2(100, 10)
		);

		Assert.Equal(0.75f, amount, 4);
	}

	[Fact]
	public void ProjectedPickingRejectsClippedAndOccludedPoints()
	{
		Assert.True(CadViewport.IsProjectedPointVisible(new Vector3(20, 30, 0.4f), 0.5f));
		Assert.False(CadViewport.IsProjectedPointVisible(new Vector3(20, 30, 0.6f), 0.5f));
		Assert.False(CadViewport.IsProjectedPointInClip(new Vector3(20, 30, -0.1f)));
		Assert.False(CadViewport.IsProjectedPointInClip(new Vector3(20, 30, 1.1f)));
	}

	[Fact]
	public void OrthographicPixelRayHitsExpectedTessellatedFace()
	{
		Camera camera = new()
		{
			Position = new Vector3(0, 0, 10),
		};
		camera.LookAt(Vector3.Zero);
		camera.SetOrthogonal(
			-100,
			-50,
			100,
			50,
			new Vector2(1200, 600),
			0.1f,
			100
		);
		CadTessellation tessellation = new()
		{
			Vertices =
			[
				new CadMeshVertex(40, -10, 0, 0, 0, 1),
				new CadMeshVertex(60, -10, 0, 0, 0, 1),
				new CadMeshVertex(50, 10, 0, 0, 0, 1),
			],
			Indices = [0, 1, 2],
			Faces = [new CadFaceRange(42, null, 0, 3)],
		};
		CadTriangleBvh bvh = new(tessellation);

		PickingRay ray = camera.CreatePickingRay(new Vector2(900, 300));

		Assert.True(bvh.TryIntersect(ray, out CadPickHit hit));
		Assert.Equal((ulong)42, hit.TopologyId);
		Vector3 hitPoint = ray.GetPoint(hit.Distance);
		Assert.Equal(50, hitPoint.X, 4);
		Assert.Equal(0, hitPoint.Y, 4);
		Assert.Equal(0, hitPoint.Z, 4);
	}

	[Theory]
	[InlineData(38, 24)]
	[InlineData(-57, 31)]
	[InlineData(124, -42)]
	public void PerspectiveOrbitRayHitsProjectedCandidate(float yaw, float pitch)
	{
		const float distance = 450;
		float yawRadians = yaw * MathF.PI / 180;
		float pitchRadians = pitch * MathF.PI / 180;
		Vector3 direction = new(
			MathF.Sin(yawRadians) * MathF.Cos(pitchRadians),
			MathF.Sin(pitchRadians),
			MathF.Cos(yawRadians) * MathF.Cos(pitchRadians)
		);
		Camera camera = new()
		{
			Position = direction * distance,
			CameraUpNormal = Vector3.UnitY,
		};
		camera.LookAt(Vector3.Zero);
		camera.SetPerspective(1200, 600, MathF.PI / 3, 0.1f, 200000);
		Vector3 center = new(45, 12, -3);
		Vector3 screen = camera.WorldToScreen(center);

		PickingRay ray = camera.CreatePickingRay(new Vector2(screen.X, screen.Y));

		Assert.True(CadViewport.TryIntersectSphere(ray, center, 4, out _));
	}

	[Fact]
	public void EdgePolylinesBecomeOneLineList()
	{
		CadEdgePolyline[] edges =
		[
			new CadEdgePolyline(
				1,
				CadTopologyKind.Edge,
				[new CadPoint3(0, 0, 0), new CadPoint3(1, 0, 0), new CadPoint3(2, 0, 0)]
			),
			new CadEdgePolyline(
				2,
				CadTopologyKind.Edge,
				[new CadPoint3(0, 1, 0), new CadPoint3(1, 1, 0)]
			),
		];

		Vector3[] vertices = CadViewport.BuildEdgeLineVertices(edges);

		Assert.Equal(6, vertices.Length);
		Assert.Equal(new Vector3(0, 0, 0), vertices[0]);
		Assert.Equal(new Vector3(1, 0, 0), vertices[1]);
		Assert.Equal(new Vector3(1, 0, 0), vertices[2]);
		Assert.Equal(new Vector3(2, 0, 0), vertices[3]);
		Assert.Equal(new Vector3(0, 1, 0), vertices[4]);
		Assert.Equal(new Vector3(1, 1, 0), vertices[5]);
	}
}
