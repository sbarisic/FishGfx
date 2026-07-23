using System.Numerics;
using FishGfx.Cad;
using FishGfx.Graphics;

namespace FishGfx.ManifoldCad;

internal sealed partial class CadViewport
{
	private BezierDraftState bezierDraft;
	private RunnerPathPointKind? activeBezierHandle;
	private RunnerPathPointKind? selectedBezierHandle;
	private int activeBezierAxis = -1;
	private Vector2 bezierAxisDragStart;
	private Vector3 bezierDragPlanePoint;
	private Vector3 bezierDragPlaneNormal;
	private Vector3 bezierDragIntersection;
	private CadPoint3 bezierDragPoint;

	internal event Action<BezierDraftState> BezierCommitRequested;
	internal event Action BezierDraftCancelled;
	internal event Action<BezierDraftState, RunnerPathPointKind> BezierDraftPreviewChanged;

	internal void SetBezierEditor(Guid runnerId, RunnerNode node, RunnerFeature feature)
	{
		if (bezierDraft?.RunnerId == runnerId && bezierDraft.NodeId == node.Id
			&& !bezierDraft.IsDirty)
		{
			bezierDraft = BezierDraftState.Create(runnerId, node, feature);
			return;
		}
		if (bezierDraft?.IsDirty == true)
		{
			return;
		}
		bezierDraft = BezierDraftState.Create(runnerId, node, feature);
		activeBezierHandle = null;
		selectedBezierHandle = null;
	}

	internal void RestoreBezierEditor(Guid runnerId, RunnerNode node, RunnerFeature feature)
	{
		bezierDraft = BezierDraftState.Create(runnerId, node, feature);
		activeBezierHandle = null;
		selectedBezierHandle = null;
	}

	internal void ClearBezierEditor()
	{
		if (bezierDraft?.IsDirty == true)
		{
			return;
		}
		bezierDraft = null;
		activeBezierHandle = null;
		selectedBezierHandle = null;
	}

	internal void SetBezierInvalid(Guid runnerId, Guid? nodeId)
	{
		if (bezierDraft?.RunnerId == runnerId
			&& (!nodeId.HasValue || bezierDraft.NodeId == nodeId.Value))
		{
			bezierDraft.IsInvalid = true;
		}
	}

	internal void ReloadBezierCommittedProperties(Guid runnerId, RunnerNode node)
	{
		if (bezierDraft?.RunnerId == runnerId && bezierDraft.NodeId == node.Id)
		{
			bezierDraft.ReloadCommittedProperties(node);
		}
	}

	internal bool CancelBezierDraft()
	{
		if (bezierDraft?.IsDirty != true && !activeBezierHandle.HasValue)
		{
			return false;
		}
		activeBezierHandle = null;
		activeBezierAxis = -1;
		BezierDraftCancelled?.Invoke();
		return true;
	}

	private bool TryBeginBezierHandle(PickContext context)
	{
		if (bezierDraft == null)
		{
			return false;
		}
		if (TryBeginBezierAxis(context))
		{
			return true;
		}

		RunnerPathPointKind? selected = null;
		float best = 13;
		foreach (RunnerPathPointKind kind in Enum.GetValues<RunnerPathPointKind>())
		{
			Vector3 projected = camera.WorldToScreen(ToVector(bezierDraft.Point(kind)));
			if (!IsProjectedPointInClip(projected))
			{
				continue;
			}
			float screenDistance = Vector2.Distance(
				new Vector2(projected.X, projected.Y),
				context.LocalPoint
			);
			if (screenDistance < best)
			{
				best = screenDistance;
				selected = kind;
			}
		}
		if (!selected.HasValue)
		{
			return false;
		}
		if (selected.Value == RunnerPathPointKind.Start)
		{
			selectedBezierHandle = selected;
			BezierDraftPreviewChanged?.Invoke(bezierDraft, selected.Value);
			return true;
		}

		activeBezierHandle = selected;
		selectedBezierHandle = selected;
		BezierDraftPreviewChanged?.Invoke(bezierDraft, selected.Value);
		bezierDragPoint = bezierDraft.Point(selected.Value);
		bezierDragPlanePoint = ToVector(bezierDragPoint);
		bezierDragPlaneNormal = Vector3.Normalize(focus - camera.Position);
		if (!TryIntersectPlane(
			context.Ray,
			bezierDragPlanePoint,
			bezierDragPlaneNormal,
			out bezierDragIntersection))
		{
			activeBezierHandle = null;
			return false;
		}
		MarkRunnerStale(bezierDraft.RunnerId);
		return true;
	}

	private void UpdateBezierDrag(CadRect bounds, Vector2 mouse)
	{
		if (!activeBezierHandle.HasValue)
		{
			return;
		}
		if (activeBezierAxis >= 0)
		{
			UpdateBezierAxisDrag(bounds, mouse);
			return;
		}
		ConfigureCamera(Math.Max(1, (int)bounds.Width), Math.Max(1, (int)bounds.Height));
		PickingRay ray = camera.CreatePickingRay(ToCameraPoint(bounds, mouse));
		if (!TryIntersectPlane(
			ray,
			bezierDragPlanePoint,
			bezierDragPlaneNormal,
			out Vector3 intersection))
		{
			return;
		}
		Vector3 delta = intersection - bezierDragIntersection;
		bezierDraft.MoveWorldPoint(
			activeBezierHandle.Value,
			bezierDragPoint + CadPoint3.FromVector3(delta)
		);
		BezierDraftPreviewChanged?.Invoke(bezierDraft, activeBezierHandle.Value);
	}

	private void CompleteBezierDrag()
	{
		if (!activeBezierHandle.HasValue)
		{
			return;
		}
		activeBezierHandle = null;
		activeBezierAxis = -1;
		if (bezierDraft?.IsDirty == true)
		{
			BezierCommitRequested?.Invoke(bezierDraft);
		}
	}

	private void DrawBezierEditor(RenderPass pass)
	{
		if (bezierDraft == null)
		{
			return;
		}
		using IDisposable stateScope = pass.PushState(pass.State with
		{
			DepthTestEnabled = false,
			DepthWriteEnabled = false,
		});

		Color polygon = new(135, 155, 180);
		Color curve = bezierDraft.IsInvalid ? new Color(245, 65, 70) : new Color(70, 215, 245);
		CadPoint3[] controls =
		{
			bezierDraft.Start,
			bezierDraft.Control1,
			bezierDraft.Control2,
			bezierDraft.End,
		};
		for (int index = 1; index < controls.Length; ++index)
		{
			pass.DrawLine(
				new Vertex3(ToVector(controls[index - 1]), polygon),
				new Vertex3(ToVector(controls[index]), polygon),
				2
			);
		}

		CadPoint3 previous = bezierDraft.Start;
		for (int segment = 1; segment <= 48; ++segment)
		{
			CadPoint3 current = bezierDraft.Sample(segment / 48.0);
			pass.DrawLine(new Vertex3(ToVector(previous), curve), new Vertex3(ToVector(current), curve), 4);
			previous = current;
		}

		float radius = Math.Max(distance * 0.009f, 2);
		foreach (RunnerPathPointKind kind in Enum.GetValues<RunnerPathPointKind>())
		{
			candidateSphere.DefaultColor = kind switch
			{
				RunnerPathPointKind.Start => new Color(145, 150, 160),
				RunnerPathPointKind.Control1 => new Color(255, 190, 55),
				RunnerPathPointKind.Control2 => new Color(75, 220, 245),
				_ => new Color(235, 90, 205),
			};
			using (pass.PushModel(
				Matrix4x4.CreateScale(radius)
				* Matrix4x4.CreateTranslation(ToVector(bezierDraft.Point(kind)))))
			{
				pass.DrawMesh(candidateSphere);
			}
		}
		DrawBezierAxes(pass, radius);

		CadFrame exit = bezierDraft.AuthoritativeExitFrame;
		float axisLength = radius * 4;
		Vector3 exitOrigin = ToVector(exit.Origin);
		pass.DrawLine(
			new Vertex3(exitOrigin, Color.Red),
			new Vertex3(exitOrigin + ToVector(exit.Normal) * axisLength, Color.Red),
			3
		);
		pass.DrawLine(
			new Vertex3(exitOrigin, Color.Green),
			new Vertex3(exitOrigin + ToVector(exit.Binormal) * axisLength, Color.Green),
			3
		);
		pass.DrawLine(
			new Vertex3(exitOrigin, Color.Blue),
			new Vertex3(exitOrigin + ToVector(exit.Tangent) * axisLength, Color.Blue),
			3
		);
	}

	private bool TryBeginBezierAxis(PickContext context)
	{
		if (!selectedBezierHandle.HasValue
			|| selectedBezierHandle == RunnerPathPointKind.Start)
		{
			return false;
		}
		CadPoint3 point = bezierDraft.Point(selectedBezierHandle.Value);
		Vector3[] axes = selectedBezierHandle == RunnerPathPointKind.Control1
			? new[] { ToVector(bezierDraft.EntryFrame.Tangent) }
			: new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ };
		float length = Math.Max(distance * 0.07f, 18);
		float best = 7;
		int selected = -1;
		for (int index = 0; index < axes.Length; ++index)
		{
			Vector3 start = camera.WorldToScreen(ToVector(point) + axes[index] * length * 0.18f);
			Vector3 end = camera.WorldToScreen(ToVector(point) + axes[index] * length);
			float screenDistance = DistanceToSegment(
				context.LocalPoint,
				new Vector2(start.X, start.Y),
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
		activeBezierAxis = selectedBezierHandle == RunnerPathPointKind.Control1 ? 3 : selected;
		activeBezierHandle = selectedBezierHandle;
		bezierAxisDragStart = context.LocalPoint;
		bezierDragPoint = point;
		MarkRunnerStale(bezierDraft.RunnerId);
		return true;
	}

	private void UpdateBezierAxisDrag(CadRect bounds, Vector2 mouse)
	{
		Vector3 axis = activeBezierAxis == 3
			? ToVector(bezierDraft.EntryFrame.Tangent)
			: activeBezierAxis switch
			{
				0 => Vector3.UnitX,
				1 => Vector3.UnitY,
				_ => Vector3.UnitZ,
			};
		Vector3 origin = camera.WorldToScreen(ToVector(bezierDragPoint));
		Vector3 end = camera.WorldToScreen(
			ToVector(bezierDragPoint) + axis * Math.Max(distance * 0.07f, 18));
		Vector2 screenAxis = new(end.X - origin.X, end.Y - origin.Y);
		if (screenAxis.LengthSquared() <= 1.0e-6f)
		{
			return;
		}
		float pixels = Vector2.Dot(
			ToCameraPoint(bounds, mouse) - bezierAxisDragStart,
			Vector2.Normalize(screenAxis)
		);
		float worldAmount = pixels * Math.Max(distance, 1) / Math.Max(bounds.Height, 1);
		bezierDraft.MoveWorldPoint(
			activeBezierHandle.Value,
			bezierDragPoint + CadPoint3.FromVector3(axis * worldAmount)
		);
		BezierDraftPreviewChanged?.Invoke(bezierDraft, activeBezierHandle.Value);
	}

	private void DrawBezierAxes(RenderPass pass, float handleRadius)
	{
		if (!selectedBezierHandle.HasValue
			|| selectedBezierHandle == RunnerPathPointKind.Start)
		{
			return;
		}
		Vector3 origin = ToVector(bezierDraft.Point(selectedBezierHandle.Value));
		float length = Math.Max(distance * 0.07f, handleRadius * 6);
		if (selectedBezierHandle == RunnerPathPointKind.Control1)
		{
			pass.DrawLine(
				new Vertex3(origin, Color.Blue),
				new Vertex3(origin + ToVector(bezierDraft.EntryFrame.Tangent) * length, Color.Blue),
				4
			);
			return;
		}
		pass.DrawLine(new Vertex3(origin, Color.Red), new Vertex3(origin + Vector3.UnitX * length, Color.Red), 4);
		pass.DrawLine(new Vertex3(origin, Color.Green), new Vertex3(origin + Vector3.UnitY * length, Color.Green), 4);
		pass.DrawLine(new Vertex3(origin, Color.Blue), new Vertex3(origin + Vector3.UnitZ * length, Color.Blue), 4);
	}

	private static bool TryIntersectPlane(
		PickingRay ray,
		Vector3 planePoint,
		Vector3 planeNormal,
		out Vector3 intersection
	)
	{
		float denominator = Vector3.Dot(ray.Direction, planeNormal);
		if (MathF.Abs(denominator) <= 1.0e-6f)
		{
			intersection = default;
			return false;
		}
		float distance = Vector3.Dot(planePoint - ray.Origin, planeNormal) / denominator;
		intersection = ray.GetPoint(distance);
		return distance >= 0;
	}
}
