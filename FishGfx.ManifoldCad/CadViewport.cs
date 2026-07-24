using System.Numerics;
using FishGfx.Cad;
using FishGfx.Game;
using FishGfx.Graphics;
using FishGfx.Graphics.Drawables;

namespace FishGfx.ManifoldCad;

internal readonly record struct CadViewportSelection(
	Guid? PartId,
	Guid? RunnerId,
	ulong TopologyId,
	Guid? SourceNodeId,
	CadPoint3 HitPoint,
	Guid? MateId,
	bool IsMateCandidate = false,
	IReadOnlyList<CadGeometrySourceRef> Sources = null
);

internal sealed partial class CadViewport : IDisposable
{
	private readonly GraphicsContext graphics;
	private readonly Camera camera = new();
	private readonly List<SceneItem> items = new();
	private readonly List<MateGlyph> mates = new();
	private readonly List<MateCandidateGlyph> mateCandidates = new();
	private readonly List<CollectorGlyph> collectorGlyphs = new();
	private readonly List<CadPoint3[]> collectorDraftCurves = new();
	private bool collectorDraftInvalid;
	private readonly Mesh3D candidateSphere;
	private readonly Mesh3D gridMesh;
	private RenderTarget target;
	private Vector3 focus;
	private float distance = 450;
	private float yaw = 35;
	private float pitch = 24;
	private Vector2 previousMouse;
	private bool orthographic;
	private float scrollDelta;
	private CadPart selectedPart;
	private bool hasFrameGizmo;
	private CadPoint3 frameGizmoOrigin;
	private CadPoint3 selectedEuler;
	private bool rotationGizmo;
	private int activeGizmoAxis = -1;
	private Vector2 gizmoDragStart;
	private CadPoint3 gizmoTranslationStart;
	private CadPoint3 gizmoEulerStart;
	private Vector2 gizmoRotationCenter;
	private float gizmoRotationStartAngle;
	private Mesh3D selectedFaceMesh;
	private bool pickingRayDebugEnabled;
	private bool hasDebugPickingRay;
	private Vector3 debugPickingRayStart;
	private Vector3 debugPickingRayEnd;
	private Vector3? debugPickingHit;
	private bool disposed;
	private Guid? activeRunnerId;

	internal CadViewport(GraphicsContext graphics)
	{
		this.graphics = graphics ?? throw new ArgumentNullException(nameof(graphics));
		candidateSphere = CreateCandidateSphere(graphics);
		gridMesh = CreateGridMesh(graphics);
	}

	internal event Action<CadViewportSelection> SelectionChanged;

	internal event Action<CadPoint3> GizmoTranslationChanged;
	internal event Action<CadPoint3> GizmoRotationChanged;
	internal event Action GizmoCommitRequested;

	internal CadViewportSelection Selection { get; private set; }
	internal int MateCandidateCount => mateCandidates.Count;

	internal void CaptureView(ManifoldViewState view)
	{
		ArgumentNullException.ThrowIfNull(view);
		view.CameraTarget = new CadPoint3(focus.X, focus.Y, focus.Z);
		view.CameraDistance = distance;
		view.YawDegrees = yaw;
		view.PitchDegrees = pitch;
		view.Orthographic = orthographic;
	}

	internal void RestoreView(ManifoldViewState view)
	{
		ArgumentNullException.ThrowIfNull(view);
		focus = ToVector(view.CameraTarget);
		distance = (float)Math.Clamp(view.CameraDistance, 1, 100000);
		yaw = (float)view.YawDegrees;
		pitch = Math.Clamp((float)view.PitchDegrees, -89, 89);
		orthographic = view.Orthographic;
	}

	internal void ClearScene()
	{
		foreach (SceneItem item in items)
		{
			item.Dispose();
		}
		items.Clear();
		mates.Clear();
		mateCandidates.Clear();
		collectorGlyphs.Clear();
		selectedFaceMesh?.Dispose();
		selectedFaceMesh = null;
		selectedPart = null;
		hasFrameGizmo = false;
		Selection = default;
		activeRunnerId = null;
		hasDebugPickingRay = false;
		debugPickingHit = null;
		bezierDraft = null;
		activeBezierHandle = null;
	}

	internal void SetSelectedPart(CadPart part, CadPoint3 euler)
	{
		selectedPart = part;
		selectedEuler = euler;
		hasFrameGizmo = part != null;
		frameGizmoOrigin = part?.Transform.Translation ?? default;
	}

	internal void SetSelectedFrame(CadFrame frame, CadPoint3 euler)
	{
		selectedPart = null;
		hasFrameGizmo = true;
		frameGizmoOrigin = frame.Origin;
		selectedEuler = euler;
	}

	internal void SetCollectorDraft(
		CadCollectorSystem system,
		Guid? inletId,
		CadFrame frame,
		bool invalid = false
	)
	{
		int index = collectorGlyphs.FindIndex(item =>
			item.SystemId == system.Id && item.InletId == inletId);
		if (index >= 0)
		{
			collectorGlyphs[index] = new CollectorGlyph(system.Id, inletId, frame);
		}
		collectorDraftCurves.Clear();
		collectorDraftInvalid = invalid;
		CadFrame outlet = inletId.HasValue ? system.OutletFrame : frame;
		double trunkLength = system.OutletStubLength
			+ system.MergeLength
			+ system.OverlapLength;
		collectorDraftCurves.Add(new[]
		{
			outlet.Origin - outlet.Tangent * trunkLength,
			outlet.Origin,
		});
		foreach (CadCollectorInlet inlet in system.Inlets)
		{
			CadFrame inletFrame = inlet.Id == inletId ? frame : system.GetWorldInletFrame(inlet);
			CadPoint3 p0 = inletFrame.Origin;
			CadPoint3 p1 = p0 + inletFrame.Tangent * inlet.BranchStartHandleLength;
			CadPoint3 junction = outlet.Origin - outlet.Tangent
				* (system.OutletStubLength + inlet.MergeStation * system.MergeLength);
			CadPoint3 p3 = junction + outlet.Tangent * system.OverlapLength;
			CadPoint3 p2 = p3 - outlet.Tangent * system.BranchEndHandleLength;
			CadPoint3[] samples = new CadPoint3[25];
			for (int sample = 0; sample < samples.Length; ++sample)
			{
				double t = sample / (double)(samples.Length - 1);
				double inverse = 1 - t;
				samples[sample] = p0 * (inverse * inverse * inverse)
					+ p1 * (3 * inverse * inverse * t)
					+ p2 * (3 * inverse * t * t)
					+ p3 * (t * t * t);
			}
			collectorDraftCurves.Add(samples);
		}
	}

	internal bool ToggleGizmoMode()
	{
		rotationGizmo = !rotationGizmo;
		activeGizmoAxis = -1;
		return rotationGizmo;
	}

	internal bool TogglePickingRayDebug()
	{
		pickingRayDebugEnabled = !pickingRayDebugEnabled;

		if (!pickingRayDebugEnabled)
		{
			hasDebugPickingRay = false;
			debugPickingHit = null;
		}

		return pickingRayDebugEnabled;
	}

	internal void AddOrReplace(Guid? partId, Guid? runnerId, CadTessellation tessellation, bool runner, bool stale = false)
	{
		SceneItem existing = items.FirstOrDefault(item => item.PartId == partId
			&& item.RunnerId == runnerId && item.IsRunner == runner);
		existing?.Dispose();
		items.Remove(existing);
		Mesh3D mesh = graphics.CreateMesh3D(BufferUsage.Dynamic);
		Vector3[] positions = tessellation.Vertices.Select(vertex => new Vector3(vertex.X, vertex.Y, vertex.Z)).ToArray();
		Vector3[] normals = tessellation.Vertices.Select(vertex => new Vector3(vertex.NormalX, vertex.NormalY, vertex.NormalZ)).ToArray();
		Color baseColor = runner
			? RunnerColor(runnerId, stale)
			: new Color(130, 145, 160);
		Color[] colors = normals.Select(normal => Shade(baseColor, normal)).ToArray();
		mesh.SetVertices(positions);
		mesh.SetNormals(normals);
		mesh.SetColors(colors);
		mesh.SetElements(tessellation.Indices);
		items.Add(new SceneItem(graphics, partId, runnerId, runner, stale, tessellation, mesh));
	}

	internal void SetActiveRunner(Guid? runnerId)
	{
		activeRunnerId = runnerId;
		foreach (SceneItem item in items.Where(item => item.IsRunner))
		{
			item.SetColor(RunnerColor(item.RunnerId, item.Stale));
		}
	}

	internal void RemoveRunner(Guid runnerId)
	{
		foreach (SceneItem item in items.Where(item => item.RunnerId == runnerId).ToArray())
		{
			item.Dispose();
			items.Remove(item);
		}
	}

	internal bool HasRunnerGeometry(Guid runnerId)
	{
		return items.Any(item =>
			item.IsRunner
				&& item.RunnerId == runnerId
				&& item.Tessellation.Vertices.Length > 0
				&& item.Tessellation.Indices.Length > 0);
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

	internal void SetCollectors(ManifoldProject project)
	{
		collectorGlyphs.Clear();
		collectorDraftCurves.Clear();
		collectorDraftInvalid = false;
		foreach (CadCollectorSystem system in project.CollectorSystems)
		{
			collectorGlyphs.Add(new CollectorGlyph(system.Id, null, system.OutletFrame));
			foreach (CadCollectorInlet inlet in system.Inlets)
			{
				collectorGlyphs.Add(new CollectorGlyph(
					system.Id,
					inlet.Id,
					system.GetWorldInletFrame(inlet)
				));
			}
		}
		CadCollectorSystem invalid = project.ActiveCollectorSystem;
		if (invalid?.IsResolved == false)
		{
			SetCollectorDraft(invalid, null, invalid.OutletFrame, true);
		}
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

	internal void MarkRunnerStale(Guid? runnerId = null)
	{
		foreach (SceneItem item in items.Where(item => item.IsRunner
			&& (!runnerId.HasValue || item.RunnerId == runnerId)))
		{
			item.SetStale(true);
			item.SetColor(RunnerColor(item.RunnerId, true));
		}
	}

	internal void MarkRunnerCurrent(Guid runnerId)
	{
		foreach (SceneItem item in items.Where(item => item.IsRunner && item.RunnerId == runnerId))
		{
			item.SetStale(false);
			item.SetColor(RunnerColor(item.RunnerId, false));
		}
	}

	internal void Update(CadRect bounds, InputManager input, Vector2 mouse)
	{
		Vector2 delta = mouse - previousMouse;
		previousMouse = mouse;

		if (activeGizmoAxis >= 0 && input.IsMouseButtonDown(MouseButton.Left))
		{
			UpdateGizmo(bounds, mouse);
		}

		if (activeBezierHandle.HasValue && input.IsMouseButtonDown(MouseButton.Left))
		{
			UpdateBezierDrag(bounds, mouse);
		}

		if (input.WasMouseButtonReleased(MouseButton.Left))
		{
			CompleteBezierDrag();
			if (activeGizmoAxis >= 0)
			{
				GizmoCommitRequested?.Invoke();
			}
			activeGizmoAxis = -1;
		}

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

		if (input.WasMouseButtonPressed(MouseButton.Left))
		{
			PickContext context = CreatePickContext(bounds, mouse);
			CaptureDebugPickingRay(context);

			if (TryBeginBezierHandle(context)
				|| TryPickCollectorGlyph(context)
				|| TryPickMateGlyph(context)
				|| TryPickMateCandidate(context))
			{
				return;
			}

			if (!TryBeginGizmo(bounds, mouse))
			{
				PickGeometry(context);
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

	internal void SetOrbit(float yawDegrees, float pitchDegrees, bool useOrthographic)
	{
		if (!float.IsFinite(yawDegrees))
		{
			throw new ArgumentOutOfRangeException(nameof(yawDegrees));
		}

		if (!float.IsFinite(pitchDegrees))
		{
			throw new ArgumentOutOfRangeException(nameof(pitchDegrees));
		}

		yaw = yawDegrees;
		pitch = Math.Clamp(pitchDegrees, -89, 89);
		orthographic = useOrthographic;
	}

	private Color RunnerColor(Guid? runnerId, bool stale)
	{
		if (stale)
			return new Color(140, 80, 45, 115);
		return runnerId == activeRunnerId ? new Color(220, 132, 55) : new Color(142, 102, 72);
	}

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

}
