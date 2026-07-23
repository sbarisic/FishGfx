using System.Numerics;
using FishGfx.Cad;
using FishGfx.Graphics;
using FishGfx.Graphics.Drawables;

namespace FishGfx.ManifoldCad;

internal sealed partial class CadViewport
{
	internal RenderTarget Render(RenderFrame frame, CadRect bounds, Guid? highlightedNodeId)
	{
		EnsureTarget(checked((int)bounds.Width), checked((int)bounds.Height));
		ConfigureCamera(target.Width, target.Height);
		RenderState state = RenderState.Default with
		{
			CullMode = CullMode.None,
			DepthTestEnabled = true,
			DepthWriteEnabled = true,
		};

		using RenderPass pass = frame.BeginPass(target, new RenderPassDescriptor
		{
			View = new RenderView(camera),
			State = state,
			ColorLoadAction = RenderLoadAction.Clear,
			DepthLoadAction = RenderLoadAction.Clear,
			StencilLoadAction = RenderLoadAction.Clear,
			ClearColor = new Color(19, 23, 29),
		});

		pass.DrawMesh(gridMesh);

		foreach (SceneItem item in items)
		{
			pass.DrawMesh(item.Mesh);
			DrawEdges(pass, item, highlightedNodeId, Selection);

			if (highlightedNodeId.HasValue && item.HighlightMeshes.TryGetValue(highlightedNodeId.Value, out Mesh3D highlight))
			{
				pass.DrawMesh(highlight);
			}
		}

		if (selectedFaceMesh != null)
		{
			pass.DrawMesh(selectedFaceMesh);
		}

		DrawMateGlyphs(pass);
		DrawMateCandidates(pass);
		DrawPartGizmo(pass);
		DrawDebugPickingRay(pass);

		return target;
	}

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}

		disposed = true;

		foreach (SceneItem item in items)
		{
			item.Dispose();
		}

		items.Clear();
		selectedFaceMesh?.Dispose();
		candidateSphere.Dispose();
		gridMesh.Dispose();
		target?.Dispose();
	}

	private void ConfigureCamera(int width, int height)
	{
		float yawRadians = yaw * MathF.PI / 180;
		float pitchRadians = pitch * MathF.PI / 180;
		Vector3 direction = new(
			MathF.Sin(yawRadians) * MathF.Cos(pitchRadians),
			MathF.Sin(pitchRadians),
			MathF.Cos(yawRadians) * MathF.Cos(pitchRadians)
		);
		camera.Position = focus + direction * distance;
		camera.CameraUpNormal = MathF.Abs(Vector3.Dot(direction, Vector3.UnitY)) > 0.9999f
			? Vector3.UnitZ
			: Vector3.UnitY;
		camera.LookAt(focus);

		if (orthographic)
		{
			float halfHeight = distance * 0.5f;
			float halfWidth = halfHeight * width / Math.Max(1f, height);
			camera.SetOrthogonal(
				-halfWidth,
				-halfHeight,
				halfWidth,
				halfHeight,
				new Vector2(width, height),
				0.1f,
				200000
			);
		}
		else
		{
			camera.SetPerspective(width, height, MathF.PI / 3, 0.1f, 200000);
		}
	}

	private void EnsureTarget(int width, int height)
	{
		width = Math.Max(1, width);
		height = Math.Max(1, height);

		if (target?.Width == width && target.Height == height)
		{
			return;
		}

		target?.Dispose();
		target = graphics.CreateRenderTarget(new RenderTargetDescriptor(width, height));
	}

	private static void DrawEdges(
		RenderPass pass,
		SceneItem item,
		Guid? highlightedNodeId,
		CadViewportSelection selection
	)
	{
		item.EdgeMesh.DefaultColor = item.IsRunner && highlightedNodeId.HasValue
			? new Color(255, 210, 80)
			: new Color(44, 49, 55);
		pass.DrawMesh(item.EdgeMesh);

		if (selection.PartId != item.PartId)
		{
			return;
		}

		Color selectedColor = new(255, 205, 55);

		foreach (CadEdgePolyline edge in item.Tessellation.Edges.Where(edge =>
			edge.TopologyId == selection.TopologyId))
			for (int index = 1; index < edge.Points.Length; index++)
			{
				pass.DrawLine(
					new Vertex3(ToVector(edge.Points[index - 1]), selectedColor),
					new Vertex3(ToVector(edge.Points[index]), selectedColor),
					4
				);
			}
	}

	private void DrawMateGlyphs(RenderPass pass)
	{
		foreach (MateGlyph mate in mates)
		{
			Vector3 origin = ToVector(mate.Frame.Origin);
			float scale = Math.Max(distance * 0.035f, 8);
			pass.DrawLine(new Vertex3(origin, Color.Red), new Vertex3(origin + ToVector(mate.Frame.Normal) * scale, Color.Red), 3);
			pass.DrawLine(new Vertex3(origin, Color.Green), new Vertex3(origin + ToVector(mate.Frame.Binormal) * scale, Color.Green), 3);
			pass.DrawLine(new Vertex3(origin, Color.Blue), new Vertex3(origin + ToVector(mate.Frame.Tangent) * scale, Color.Blue), 3);
			pass.DrawPoint(new Vertex3(origin, Color.Yellow), 8);
		}
	}

	private void DrawMateCandidates(RenderPass pass)
	{
		foreach (MateCandidateGlyph candidate in mateCandidates)
		{
			float radius = CandidateDisplayRadius(candidate);
			Vector3 center = CandidateDisplayCenter(candidate);
			bool selected = Selection.PartId == candidate.PartId
				&& Selection.TopologyId == candidate.TopologyId;
			candidateSphere.DefaultColor = selected
				? new Color(255, 205, 55)
				: new Color(70, 205, 235);

			using (pass.PushModel(Matrix4x4.CreateScale(radius) * Matrix4x4.CreateTranslation(center)))
			{
				pass.DrawMesh(candidateSphere);
			}
		}
	}

	private float CandidateDisplayRadius(MateCandidateGlyph candidate)
	{
		float minimum = Math.Max(distance * 0.008f, 1.5f);
		float maximum = Math.Max(distance * 0.018f, minimum);
		return Math.Clamp((float)candidate.EquivalentRadius * 0.18f, minimum, maximum);
	}

	private Vector3 CandidateDisplayCenter(MateCandidateGlyph candidate)
	{
		float radius = CandidateDisplayRadius(candidate);
		return ToVector(candidate.Center) + ToVector(candidate.Axis) * radius * 0.65f;
	}

	private void DrawDebugPickingRay(RenderPass pass)
	{
		if (!pickingRayDebugEnabled || !hasDebugPickingRay)
		{
			return;
		}

		using IDisposable stateScope = pass.PushState(pass.State with
		{
			DepthTestEnabled = false,
			DepthWriteEnabled = false,
		});
		pass.DrawLine(
			new Vertex3(debugPickingRayStart, new Color(255, 40, 220)),
			new Vertex3(debugPickingRayEnd, new Color(255, 40, 220)),
			3
		);
		pass.DrawPoint(new Vertex3(debugPickingRayStart, new Color(80, 220, 255)), 8);

		if (debugPickingHit.HasValue)
		{
			pass.DrawPoint(new Vertex3(debugPickingHit.Value, Color.Yellow), 11);
		}
	}

	private static Mesh3D CreateCandidateSphere(GraphicsContext graphics)
	{
		const int latitudeSegments = 8;
		const int longitudeSegments = 12;
		List<Vector3> vertices = new();
		List<Vector3> normals = new();
		List<uint> indices = new();

		for (int latitude = 0; latitude <= latitudeSegments; latitude++)
		{
			float vertical = latitude * MathF.PI / latitudeSegments;
			float height = MathF.Cos(vertical);
			float ring = MathF.Sin(vertical);

			for (int longitude = 0; longitude <= longitudeSegments; longitude++)
			{
				float horizontal = longitude * MathF.Tau / longitudeSegments;
				Vector3 normal = new(
					ring * MathF.Cos(horizontal),
					height,
					ring * MathF.Sin(horizontal)
				);
				vertices.Add(normal);
				normals.Add(normal);
			}
		}

		int row = longitudeSegments + 1;

		for (int latitude = 0; latitude < latitudeSegments; latitude++)
			for (int longitude = 0; longitude < longitudeSegments; longitude++)
			{
				uint first = checked((uint)(latitude * row + longitude));
				uint second = checked(first + (uint)row);
				indices.Add(first);
				indices.Add(second);
				indices.Add(first + 1);
				indices.Add(first + 1);
				indices.Add(second);
				indices.Add(second + 1);
			}

		Mesh3D mesh = graphics.CreateMesh3D(BufferUsage.Static);
		mesh.SetVertices(vertices.ToArray());
		mesh.SetNormals(normals.ToArray());
		mesh.SetElements(indices.ToArray());
		return mesh;
	}

	private static Mesh3D CreateGridMesh(GraphicsContext graphics)
	{
		Color minor = new(46, 52, 60);
		Color major = new(76, 84, 94);
		List<Vertex3> vertices = new();

		for (int value = -500; value <= 500; value += 25)
		{
			Color color = value == 0 ? major : minor;
			vertices.Add(new Vertex3(value, 0, -500) { Color = color });
			vertices.Add(new Vertex3(value, 0, 500) { Color = color });
			vertices.Add(new Vertex3(-500, 0, value) { Color = color });
			vertices.Add(new Vertex3(500, 0, value) { Color = color });
		}

		Mesh3D mesh = graphics.CreateMesh3D(BufferUsage.Static);
		mesh.PrimitiveType = PrimitiveType.Lines;
		mesh.SetVertices(vertices.ToArray(), vertices.Count, hasUvs: false, hasColors: true);
		return mesh;
	}

	private static Color Shade(Color color, Vector3 normal)
	{
		Vector3 light = Vector3.Normalize(new Vector3(0.35f, 0.8f, 0.45f));
		float factor = 0.35f + 0.65f * MathF.Abs(Vector3.Dot(Vector3.Normalize(normal), light));
		return new Color((byte)(color.R * factor), (byte)(color.G * factor), (byte)(color.B * factor), color.A);
	}

	private static Vector3 ToVector(CadPoint3 point) => new((float)point.X, (float)point.Y, (float)point.Z);

	internal static Vector3[] BuildEdgeLineVertices(IEnumerable<CadEdgePolyline> edges)
	{
		ArgumentNullException.ThrowIfNull(edges);
		List<Vector3> vertices = new();

		foreach (CadEdgePolyline edge in edges)
			for (int index = 1; index < edge.Points.Length; index++)
			{
				vertices.Add(ToVector(edge.Points[index - 1]));
				vertices.Add(ToVector(edge.Points[index]));
			}

		return vertices.ToArray();
	}

	private sealed class SceneItem : IDisposable
	{
		internal SceneItem(
			GraphicsContext graphics,
			Guid? partId,
			Guid? runnerId,
			bool isRunner,
			bool stale,
			CadTessellation tessellation,
			Mesh3D mesh
		)
		{
			PartId = partId;
			RunnerId = runnerId;
			IsRunner = isRunner;
			Stale = stale;
			Tessellation = tessellation;
			Mesh = mesh;
			EdgeMesh = CreateEdgeMesh(graphics, tessellation);
			Bvh = new CadTriangleBvh(tessellation);
			HighlightMeshes = CreateHighlights(graphics, tessellation);
		}

		internal Guid? PartId { get; }
		internal Guid? RunnerId { get; }
		internal bool IsRunner { get; }
		internal bool Stale { get; private set; }
		internal CadTessellation Tessellation { get; }
		internal Mesh3D Mesh { get; }
		internal Mesh3D EdgeMesh { get; }
		internal CadTriangleBvh Bvh { get; }
		internal IReadOnlyDictionary<Guid, Mesh3D> HighlightMeshes { get; }

		internal void SetStale(bool stale)
		{
			Stale = stale;
		}

		internal void SetColor(Color baseColor)
		{
			Color[] colors = Tessellation.Vertices
				.Select(vertex => Shade(baseColor, new Vector3(vertex.NormalX, vertex.NormalY, vertex.NormalZ)))
				.ToArray();
			Mesh.SetColors(colors);
		}

		public void Dispose()
		{
			Mesh.Dispose();
			EdgeMesh.Dispose();

			foreach (Mesh3D highlight in HighlightMeshes.Values)
			{
				highlight.Dispose();
			}
		}

		private static Mesh3D CreateEdgeMesh(GraphicsContext graphics, CadTessellation tessellation)
		{
			Mesh3D mesh = graphics.CreateMesh3D(BufferUsage.Static);
			mesh.PrimitiveType = PrimitiveType.Lines;
			mesh.SetVertices(BuildEdgeLineVertices(tessellation.Edges));
			mesh.DefaultColor = new Color(44, 49, 55);
			return mesh;
		}

		private static IReadOnlyDictionary<Guid, Mesh3D> CreateHighlights(GraphicsContext graphics, CadTessellation tessellation)
		{
			Dictionary<Guid, Mesh3D> result = new();
			Vector3[] positions = tessellation.Vertices.Select(vertex => new Vector3(vertex.X, vertex.Y, vertex.Z)).ToArray();

			foreach (IGrouping<Guid, CadFaceRange> group in tessellation.Faces
				.Where(face => face.SourceNodeId.HasValue)
				.GroupBy(face => face.SourceNodeId.Value))
			{
				Mesh3D mesh = graphics.CreateMesh3D(BufferUsage.Dynamic);
				mesh.SetVertices(positions);
				mesh.DefaultColor = new Color(255, 220, 70);
				mesh.SetElements(group.SelectMany(face => tessellation.Indices.Skip(face.FirstIndex).Take(face.IndexCount)).ToArray());
				result[group.Key] = mesh;
			}

			return result;
		}
	}

	private readonly record struct MateGlyph(Guid Id, Guid PartId, ulong TopologyId, CadFrame Frame);

	private readonly record struct PickContext(
		Vector2 LocalPoint,
		PickingRay Ray,
		CadPickHit? NearestFace,
		SceneItem NearestFaceItem,
		float FaceDepth
	);

	private readonly record struct MateCandidateGlyph(
		Guid PartId,
		ulong TopologyId,
		CadPoint3 Center,
		CadPoint3 Axis,
		double EquivalentRadius
	);
}
