using System.Numerics;
using FishGfx.Cad;
using FishGfx.Game;
using FishGfx.Graphics;
using FishGfx.Graphics.Drawables;

namespace FishGfx.ManifoldCad;

internal readonly record struct CadViewportSelection(
	Guid? PartId,
	ulong TopologyId,
	Guid? SourceNodeId,
	CadPoint3 HitPoint,
	Guid? MateId,
	bool IsMateCandidate = false
);

internal sealed class CadViewport : IDisposable
{
	private readonly GraphicsContext graphics;
	private readonly Camera camera = new();
	private readonly List<SceneItem> items = new();
	private readonly List<MateGlyph> mates = new();
	private readonly List<MateCandidateGlyph> mateCandidates = new();
	private readonly Mesh3D candidateSphere;
	private RenderTarget target;
	private Vector3 focus;
	private float distance = 450;
	private float yaw = 35;
	private float pitch = 24;
	private Vector2 previousMouse;
	private bool orthographic;
	private float scrollDelta;
	private CadPart selectedPart;
	private CadPoint3 selectedEuler;
	private bool rotationGizmo;
	private int activeGizmoAxis = -1;
	private Vector2 gizmoDragStart;
	private CadPoint3 gizmoTranslationStart;
	private CadPoint3 gizmoEulerStart;
	private Vector2 gizmoRotationCenter;
	private float gizmoRotationStartAngle;
	private Mesh3D selectedFaceMesh;
	private bool disposed;

	internal CadViewport(GraphicsContext graphics)
	{
		this.graphics = graphics ?? throw new ArgumentNullException(nameof(graphics));
		candidateSphere = CreateCandidateSphere(graphics);
	}

	internal event Action<CadViewportSelection> SelectionChanged;

	internal event Action<CadPoint3> GizmoTranslationChanged;
	internal event Action<CadPoint3> GizmoRotationChanged;

	internal CadViewportSelection Selection { get; private set; }
	internal int MateCandidateCount => mateCandidates.Count;

	internal void SetSelectedPart(CadPart part, CadPoint3 euler)
	{
		selectedPart = part;
		selectedEuler = euler;
	}

	internal bool ToggleGizmoMode()
	{
		rotationGizmo = !rotationGizmo;
		activeGizmoAxis = -1;
		return rotationGizmo;
	}

	internal void AddOrReplace(Guid? partId, CadTessellation tessellation, bool runner, bool stale = false)
	{
		SceneItem existing = items.FirstOrDefault(item => item.PartId == partId && item.IsRunner == runner);
		existing?.Dispose();
		items.Remove(existing);
		Mesh3D mesh = graphics.CreateMesh3D(BufferUsage.Dynamic);
		Vector3[] positions = tessellation.Vertices.Select(vertex => new Vector3(vertex.X, vertex.Y, vertex.Z)).ToArray();
		Vector3[] normals = tessellation.Vertices.Select(vertex => new Vector3(vertex.NormalX, vertex.NormalY, vertex.NormalZ)).ToArray();
		Color baseColor = runner ? new Color(200, 119, 52, stale ? (byte)115 : (byte)255) : new Color(130, 145, 160);
		Color[] colors = normals.Select(normal => Shade(baseColor, normal)).ToArray();
		mesh.SetVertices(positions);
		mesh.SetNormals(normals);
		mesh.SetColors(colors);
		mesh.SetElements(tessellation.Indices);
		items.Add(new SceneItem(graphics, partId, runner, stale, tessellation, mesh));
	}

	internal void SetMates(ManifoldProject project)
	{
		mates.Clear();

		foreach (CadMate mate in project.Mates.Where(mate => mate.IsResolved))
		{
			CadPart part = project.Parts.Single(item => item.Id == mate.PartId);
			mates.Add(new MateGlyph(mate.Id, part.Id, mate.Topology.Value.TopologyId, mate.LocalFrame.Value.Transformed(part.Transform)));
		}

		HashSet<(Guid PartId, ulong TopologyId)> bound = project.Mates
			.Where(mate => mate.IsResolved)
			.Select(mate => (mate.PartId, mate.Topology.Value.TopologyId))
			.ToHashSet();
		mateCandidates.RemoveAll(candidate => bound.Contains((candidate.PartId, candidate.TopologyId)));
	}

	internal void SetMateCandidates(CadPart part, IReadOnlyList<NativeTopologyDescriptor> topology)
	{
		ArgumentNullException.ThrowIfNull(part);
		ArgumentNullException.ThrowIfNull(topology);
		mateCandidates.RemoveAll(candidate => candidate.PartId == part.Id);

		foreach (NativeTopologyDescriptor descriptor in topology.Where(item =>
			item.Topology.Kind == CadTopologyKind.ClosedProfile))
		{
			mateCandidates.Add(new MateCandidateGlyph(
				part.Id,
				descriptor.Topology.TopologyId,
				part.Transform.TransformPoint(descriptor.Center),
				part.Transform.TransformDirection(descriptor.Axis).Normalized(),
				descriptor.RadiusMillimetres
			));
		}
	}

	internal void MarkRunnerStale()
	{
		foreach (SceneItem item in items.Where(item => item.IsRunner))
		{
			item.SetStale(true);
		}
	}

	internal void RemoveRunner()
	{
		foreach (SceneItem item in items.Where(item => item.IsRunner).ToArray())
		{
			item.Dispose();
			items.Remove(item);
		}
	}

	internal void Update(CadRect bounds, InputManager input, Vector2 mouse)
	{
		Vector2 delta = mouse - previousMouse;
		previousMouse = mouse;

		if (!bounds.Contains(mouse))
		{
			return;
		}

		if (input.IsMouseButtonDown(MouseButton.Right))
		{
			yaw -= delta.X * 0.35f;
			pitch = Math.Clamp(pitch + delta.Y * 0.35f, -89, 89);
		}

		if (input.IsMouseButtonDown(MouseButton.Middle))
		{
			float scale = Math.Max(distance, 1) / Math.Max(bounds.Height, 1);
			focus = PanFocus(
				focus,
				camera.WorldRightNormal,
				camera.WorldUpNormal,
				delta,
				scale
			);
		}

		if (scrollDelta != 0)
		{
			distance = Math.Clamp(distance * (scrollDelta > 0 ? 0.88f : 1.14f), 1, 100000);
			scrollDelta = 0;
		}

		if (activeGizmoAxis >= 0 && input.IsMouseButtonDown(MouseButton.Left))
		{
			UpdateGizmo(bounds, mouse);
		}

		if (input.WasMouseButtonReleased(MouseButton.Left))
		{
			activeGizmoAxis = -1;
		}

		if (input.WasMouseButtonPressed(MouseButton.Left))
		{
			if (!TryBeginGizmo(bounds, mouse))
			{
				Pick(bounds, mouse);
			}
		}
	}

	internal void OnScroll(float delta) => scrollDelta += delta;

	internal void Fit()
	{
		if (items.Count == 0)
		{
			focus = Vector3.Zero;
			distance = 450;
			return;
		}

		Vector3 minimum = new(float.PositiveInfinity);
		Vector3 maximum = new(float.NegativeInfinity);

		foreach (SceneItem item in items)
		{
			minimum = Vector3.Min(minimum, ToVector(item.Tessellation.Minimum));
			maximum = Vector3.Max(maximum, ToVector(item.Tessellation.Maximum));
		}

		focus = (minimum + maximum) * 0.5f;
		distance = Math.Max((maximum - minimum).Length() * 1.35f, 25);
	}

	internal void ToggleOrthographic() => orthographic = !orthographic;

	internal void SetView(CadStandardView view)
	{
		(yaw, pitch) = view switch
		{
			CadStandardView.Top => (0, 89.9f),
			CadStandardView.Bottom => (0, -89.9f),
			CadStandardView.Front => (0, 0),
			CadStandardView.Back => (180, 0),
			CadStandardView.Left => (-90, 0),
			CadStandardView.Right => (90, 0),
			_ => (yaw, pitch),
		};
		orthographic = true;
	}

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

		DrawGrid(pass);

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
		target?.Dispose();
	}

	private void Pick(CadRect bounds, Vector2 mouse)
	{
		ConfigureCamera(Math.Max(1, (int)bounds.Width), Math.Max(1, (int)bounds.Height));
		Vector2 local = ToCameraPoint(bounds, mouse);
		PickingRay ray = camera.CreatePickingRay(local);
		CadPickHit? nearestFace = null;
		SceneItem nearestFaceItem = null;

		foreach (SceneItem item in items)
		{
			if (item.Bvh.TryIntersect(ray, out CadPickHit hit)
				&& (!nearestFace.HasValue || hit.Distance < nearestFace.Value.Distance))
			{
				nearestFace = hit;
				nearestFaceItem = item;
			}
		}

		float faceDepth = nearestFace.HasValue
			? camera.WorldToScreen(ray.GetPoint(nearestFace.Value.Distance)).Z
			: float.PositiveInfinity;
		MateGlyph? glyph = mates
			.Select(mate => (Mate: mate, Screen: camera.WorldToScreen(ToVector(mate.Frame.Origin))))
			.Select(item => (
				item.Mate,
				item.Screen,
				Distance: Vector2.Distance(new Vector2(item.Screen.X, item.Screen.Y), local)
			))
			.Where(item => IsProjectedPointVisible(item.Screen, faceDepth) && item.Distance <= 11)
			.OrderBy(item => item.Distance)
			.ThenBy(item => item.Screen.Z)
			.Select(item => (MateGlyph?)item.Mate)
			.FirstOrDefault();

		if (glyph.HasValue)
		{
			MateGlyph mate = glyph.Value;
			SetSelection(new CadViewportSelection(mate.PartId, mate.TopologyId, null, mate.Frame.Origin, mate.Id));
			return;
		}

		if (TryPickMateCandidate(ray, nearestFace?.Distance ?? float.PositiveInfinity, out MateCandidateGlyph candidate))
		{
			SetSelection(new CadViewportSelection(
				candidate.PartId,
				candidate.TopologyId,
				null,
				candidate.Center,
				null,
				true
			));
			return;
		}

		if (TryPickEdge(local, faceDepth, out SceneItem edgeItem, out CadEdgePolyline edge, out CadPoint3 edgePoint))
		{
			SetSelection(new CadViewportSelection(edgeItem.PartId, edge.TopologyId, null, edgePoint, null));
			return;
		}

		SetSelection(nearestFace.HasValue
			? new CadViewportSelection(
				nearestFaceItem.PartId,
				nearestFace.Value.TopologyId,
				nearestFace.Value.SourceNodeId,
				CadPoint3.FromVector3(ray.GetPoint(nearestFace.Value.Distance)),
				null
			)
			: default);
	}

	private void SetSelection(CadViewportSelection selection)
	{
		Selection = selection;
		selectedFaceMesh?.Dispose();
		selectedFaceMesh = null;

		if (selection.PartId.HasValue)
		{
			SceneItem item = items.FirstOrDefault(candidate => candidate.PartId == selection.PartId);
			CadFaceRange[] faces = item?.Tessellation.Faces
				.Where(face => face.TopologyId == selection.TopologyId)
				.ToArray();

			if (faces?.Length > 0)
			{
				selectedFaceMesh = graphics.CreateMesh3D(BufferUsage.Dynamic);
				selectedFaceMesh.SetVertices(item.Tessellation.Vertices
					.Select(vertex => new Vector3(vertex.X, vertex.Y, vertex.Z))
					.ToArray());
				selectedFaceMesh.DefaultColor = new Color(255, 205, 55, 190);
				selectedFaceMesh.SetElements(faces
					.SelectMany(face => item.Tessellation.Indices.Skip(face.FirstIndex).Take(face.IndexCount))
					.ToArray());
			}
		}

		SelectionChanged?.Invoke(Selection);
	}

	private bool TryPickMateCandidate(
		PickingRay ray,
		float faceDistance,
		out MateCandidateGlyph selectedCandidate
	)
	{
		selectedCandidate = default;
		float nearest = float.PositiveInfinity;

		foreach (MateCandidateGlyph candidate in mateCandidates)
		{
			Vector3 center = CandidateDisplayCenter(candidate);
			float radius = CandidateDisplayRadius(candidate);

			if (TryIntersectSphere(ray, center, radius, out float distance)
				&& distance <= faceDistance + Math.Max(radius * 0.05f, 0.01f)
				&& distance < nearest)
			{
				nearest = distance;
				selectedCandidate = candidate;
			}
		}

		return float.IsFinite(nearest);
	}

	private bool TryPickEdge(
		Vector2 mouse,
		float faceDepth,
		out SceneItem selectedItem,
		out CadEdgePolyline selectedEdge,
		out CadPoint3 selectedPoint
	)
	{
		selectedItem = null;
		selectedEdge = null;
		selectedPoint = default;
		float best = 7;
		float bestDepth = float.PositiveInfinity;

		foreach (SceneItem item in items)
			foreach (CadEdgePolyline edge in item.Tessellation.Edges)
			{
				for (int index = 1; index < edge.Points.Length; index++)
				{
					Vector3 a = camera.WorldToScreen(ToVector(edge.Points[index - 1]));
					Vector3 b = camera.WorldToScreen(ToVector(edge.Points[index]));

					if (!IsProjectedPointInClip(a) || !IsProjectedPointInClip(b))
					{
						continue;
					}

					float amount = ClosestSegmentAmount(
						mouse,
						new Vector2(a.X, a.Y),
						new Vector2(b.X, b.Y)
					);
					Vector2 projected = Vector2.Lerp(new Vector2(a.X, a.Y), new Vector2(b.X, b.Y), amount);
					float distance = Vector2.Distance(mouse, projected);
					float depth = float.Lerp(a.Z, b.Z, amount);

					if (depth <= faceDepth + 0.002f
						&& (distance < best || (MathF.Abs(distance - best) < 0.25f && depth < bestDepth)))
					{
						best = distance;
						bestDepth = depth;
						selectedItem = item;
						selectedEdge = edge;
						selectedPoint = edge.Points[index - 1] + (edge.Points[index] - edge.Points[index - 1]) * amount;
					}
				}
			}

		return selectedEdge != null;
	}

	private bool TryBeginGizmo(CadRect bounds, Vector2 mouse)
	{
		if (selectedPart == null)
		{
			return false;
		}

		if (rotationGizmo)
		{
			return TryBeginRotationGizmo(bounds, mouse);
		}

		Vector2 local = ToCameraPoint(bounds, mouse);
		Vector3 origin = camera.WorldToScreen(ToVector(selectedPart.Transform.Translation));
		Vector3[] axes = { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ };
		float length = Math.Max(distance * 0.08f, 20);
		float best = 7;
		int selected = -1;

		for (int index = 0; index < axes.Length; index++)
		{
			Vector3 end = camera.WorldToScreen(ToVector(selectedPart.Transform.Translation) + axes[index] * length);
			float screenDistance = DistanceToSegment(
				local,
				new Vector2(origin.X, origin.Y),
				new Vector2(end.X, end.Y)
			);

			if (screenDistance < best)
			{
				best = screenDistance;
				selected = index;
			}
		}

		if (selected < 0)
		{
			return false;
		}

		activeGizmoAxis = selected;
		gizmoDragStart = mouse;
		gizmoTranslationStart = selectedPart.Transform.Translation;
		return true;
	}

	private void UpdateGizmo(CadRect bounds, Vector2 mouse)
	{
		if (rotationGizmo)
		{
			UpdateRotationGizmo(bounds, mouse);
			return;
		}

		Vector3[] axes = { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ };
		Vector3 axis = axes[activeGizmoAxis];
		Vector3 origin = camera.WorldToScreen(ToVector(gizmoTranslationStart));
		Vector3 end = camera.WorldToScreen(ToVector(gizmoTranslationStart) + axis * Math.Max(distance * 0.08f, 20));
		Vector2 screenAxis = new(end.X - origin.X, -(end.Y - origin.Y));

		if (screenAxis.LengthSquared() <= 1e-6f)
		{
			return;
		}

		float pixels = Vector2.Dot(mouse - gizmoDragStart, Vector2.Normalize(screenAxis));
		float worldAmount = pixels * Math.Max(distance, 1) / Math.Max(bounds.Height, 1);
		CadPoint3 translation = gizmoTranslationStart + CadPoint3.FromVector3(axis * worldAmount);
		GizmoTranslationChanged?.Invoke(translation);
	}

	private bool TryBeginRotationGizmo(CadRect bounds, Vector2 mouse)
	{
		Vector2 local = ToCameraPoint(bounds, mouse);
		Vector3 center = ToVector(selectedPart.Transform.Translation);
		Vector3[] axes = { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ };
		float radius = Math.Max(distance * 0.065f, 16);
		float best = 7;
		int selected = -1;

		for (int axisIndex = 0; axisIndex < axes.Length; axisIndex++)
		{
			Vector3 previous = camera.WorldToScreen(RingPoint(center, axes[axisIndex], radius, 0));

			for (int segment = 1; segment <= 48; segment++)
			{
				float angle = segment * MathF.Tau / 48;
				Vector3 current = camera.WorldToScreen(RingPoint(center, axes[axisIndex], radius, angle));
				float screenDistance = DistanceToSegment(
					local,
					new Vector2(previous.X, previous.Y),
					new Vector2(current.X, current.Y)
				);

				if (screenDistance < best)
				{
					best = screenDistance;
					selected = axisIndex;
				}

				previous = current;
			}
		}

		if (selected < 0)
		{
			return false;
		}

		Vector3 projectedCenter = camera.WorldToScreen(center);
		activeGizmoAxis = selected;
		gizmoEulerStart = selectedEuler;
		gizmoRotationCenter = new Vector2(projectedCenter.X, projectedCenter.Y);
		gizmoRotationStartAngle = MathF.Atan2(
			local.Y - gizmoRotationCenter.Y,
			local.X - gizmoRotationCenter.X
		);
		return true;
	}

	private void UpdateRotationGizmo(CadRect bounds, Vector2 mouse)
	{
		Vector2 local = ToCameraPoint(bounds, mouse);
		float currentAngle = MathF.Atan2(
			local.Y - gizmoRotationCenter.Y,
			local.X - gizmoRotationCenter.X
		);
		float delta = (currentAngle - gizmoRotationStartAngle) * 180 / MathF.PI;

		if (delta > 180)
		{
			delta -= 360;
		}
		else if (delta < -180)
		{
			delta += 360;
		}

		CadPoint3 euler = activeGizmoAxis switch
		{
			0 => gizmoEulerStart with { X = gizmoEulerStart.X + delta },
			1 => gizmoEulerStart with { Y = gizmoEulerStart.Y + delta },
			_ => gizmoEulerStart with { Z = gizmoEulerStart.Z + delta },
		};
		GizmoRotationChanged?.Invoke(euler);
	}

	internal static bool TryIntersectSphere(PickingRay ray, Vector3 center, float radius, out float distance)
	{
		Vector3 offset = ray.Origin - center;
		float along = Vector3.Dot(offset, ray.Direction);
		float discriminant = along * along - (offset.LengthSquared() - radius * radius);

		if (discriminant < 0)
		{
			distance = 0;
			return false;
		}

		float root = MathF.Sqrt(discriminant);
		float near = -along - root;
		float far = -along + root;
		distance = near >= 0 ? near : far;
		return distance >= 0;
	}

	private static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
	{
		float amount = ClosestSegmentAmount(point, start, end);
		return Vector2.Distance(point, start + (end - start) * amount);
	}

	internal static float ClosestSegmentAmount(Vector2 point, Vector2 start, Vector2 end)
	{
		Vector2 segment = end - start;
		float lengthSquared = segment.LengthSquared();
		return lengthSquared <= 1e-8f
			? 0
			: Math.Clamp(Vector2.Dot(point - start, segment) / lengthSquared, 0, 1);
	}

	internal static bool IsProjectedPointInClip(Vector3 point)
	{
		return float.IsFinite(point.X)
			&& float.IsFinite(point.Y)
			&& float.IsFinite(point.Z)
			&& point.Z >= 0
			&& point.Z <= 1;
	}

	internal static bool IsProjectedPointVisible(Vector3 point, float surfaceDepth)
	{
		return IsProjectedPointInClip(point) && point.Z <= surfaceDepth + 0.002f;
	}

	internal static Vector2 ToCameraPoint(CadRect bounds, Vector2 layoutPoint)
	{
		return new Vector2(
			layoutPoint.X - bounds.X,
			bounds.Height - (layoutPoint.Y - bounds.Y)
		);
	}

	internal static Vector3 PanFocus(
		Vector3 currentFocus,
		Vector3 cameraRight,
		Vector3 cameraUp,
		Vector2 layoutDelta,
		float scale
	)
	{
		return currentFocus
			- cameraRight * layoutDelta.X * scale
			+ cameraUp * layoutDelta.Y * scale;
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

	private static void DrawGrid(RenderPass pass)
	{
		Color minor = new(46, 52, 60);
		Color major = new(76, 84, 94);

		for (int value = -500; value <= 500; value += 25)
		{
			Color color = value == 0 ? major : minor;
			pass.DrawLine(new Vertex3(value, 0, -500) { Color = color }, new Vertex3(value, 0, 500) { Color = color });
			pass.DrawLine(new Vertex3(-500, 0, value) { Color = color }, new Vertex3(500, 0, value) { Color = color });
		}
	}

	private static void DrawEdges(
		RenderPass pass,
		SceneItem item,
		Guid? highlightedNodeId,
		CadViewportSelection selection
	)
	{
		foreach (CadEdgePolyline edge in item.Tessellation.Edges)
		{
			bool selected = item.PartId == selection.PartId && edge.TopologyId == selection.TopologyId;
			Color color = selected
				? new Color(255, 205, 55)
				: item.IsRunner && highlightedNodeId.HasValue
				? new Color(255, 210, 80)
				: new Color(44, 49, 55);

			for (int index = 1; index < edge.Points.Length; index++)
			{
				pass.DrawLine(
					new Vertex3(ToVector(edge.Points[index - 1]), color),
					new Vertex3(ToVector(edge.Points[index]), color),
					selected ? 4 : item.IsRunner ? 2 : 1
				);
			}
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

	private void DrawPartGizmo(RenderPass pass)
	{
		if (selectedPart == null)
		{
			return;
		}

		Vector3 origin = ToVector(selectedPart.Transform.Translation);
		float length = Math.Max(distance * 0.08f, 20);

		if (rotationGizmo)
		{
			DrawRing(pass, origin, Vector3.UnitX, length * 0.8f, Color.Red);
			DrawRing(pass, origin, Vector3.UnitY, length * 0.8f, Color.Green);
			DrawRing(pass, origin, Vector3.UnitZ, length * 0.8f, Color.Blue);
			return;
		}

		pass.DrawLine(new Vertex3(origin, Color.Red), new Vertex3(origin + Vector3.UnitX * length, Color.Red), 4);
		pass.DrawLine(new Vertex3(origin, Color.Green), new Vertex3(origin + Vector3.UnitY * length, Color.Green), 4);
		pass.DrawLine(new Vertex3(origin, Color.Blue), new Vertex3(origin + Vector3.UnitZ * length, Color.Blue), 4);
	}

	private static void DrawRing(RenderPass pass, Vector3 center, Vector3 axis, float radius, Color color)
	{
		Vector3 previous = RingPoint(center, axis, radius, 0);

		for (int segment = 1; segment <= 48; segment++)
		{
			Vector3 current = RingPoint(center, axis, radius, segment * MathF.Tau / 48);
			pass.DrawLine(new Vertex3(previous, color), new Vertex3(current, color), 3);
			previous = current;
		}
	}

	private static Vector3 RingPoint(Vector3 center, Vector3 axis, float radius, float angle)
	{
		Vector3 reference = MathF.Abs(axis.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitY;
		Vector3 first = Vector3.Normalize(Vector3.Cross(axis, reference));
		Vector3 second = Vector3.Normalize(Vector3.Cross(axis, first));
		return center + radius * (first * MathF.Cos(angle) + second * MathF.Sin(angle));
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

	private static Color Shade(Color color, Vector3 normal)
	{
		Vector3 light = Vector3.Normalize(new Vector3(0.35f, 0.8f, 0.45f));
		float factor = 0.35f + 0.65f * MathF.Abs(Vector3.Dot(Vector3.Normalize(normal), light));
		return new Color((byte)(color.R * factor), (byte)(color.G * factor), (byte)(color.B * factor), color.A);
	}

	private static Vector3 ToVector(CadPoint3 point) => new((float)point.X, (float)point.Y, (float)point.Z);

	private sealed class SceneItem : IDisposable
	{
		internal SceneItem(GraphicsContext graphics, Guid? partId, bool isRunner, bool stale, CadTessellation tessellation, Mesh3D mesh)
		{
			PartId = partId;
			IsRunner = isRunner;
			Stale = stale;
			Tessellation = tessellation;
			Mesh = mesh;
			Bvh = new CadTriangleBvh(tessellation);
			HighlightMeshes = CreateHighlights(graphics, tessellation);
		}

		internal Guid? PartId { get; }
		internal bool IsRunner { get; }
		internal bool Stale { get; private set; }
		internal CadTessellation Tessellation { get; }
		internal Mesh3D Mesh { get; }
		internal CadTriangleBvh Bvh { get; }
		internal IReadOnlyDictionary<Guid, Mesh3D> HighlightMeshes { get; }

		internal void SetStale(bool stale)
		{
			Stale = stale;
			Color baseColor = stale ? new Color(140, 80, 45, 115) : new Color(200, 119, 52);
			Color[] colors = Tessellation.Vertices
				.Select(vertex => Shade(baseColor, new Vector3(vertex.NormalX, vertex.NormalY, vertex.NormalZ)))
				.ToArray();
			Mesh.SetColors(colors);
		}

		public void Dispose()
		{
			Mesh.Dispose();

			foreach (Mesh3D highlight in HighlightMeshes.Values)
			{
				highlight.Dispose();
			}
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

	private readonly record struct MateCandidateGlyph(
		Guid PartId,
		ulong TopologyId,
		CadPoint3 Center,
		CadPoint3 Axis,
		double EquivalentRadius
	);
}
