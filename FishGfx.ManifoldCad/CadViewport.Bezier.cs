using System.Numerics;
using FishGfx.Cad;
using FishGfx.Graphics;
using FishGfx.Im3d;

namespace FishGfx.ManifoldCad;

internal sealed partial class CadViewport
{
	private readonly CurveDisplayOverlayState curveOverlayState = new();
	private BezierDraftState bezierDraft;
	private RunnerPathPointKind? activeBezierHandle;
	private RunnerPathPointKind? selectedBezierHandle;
	private Vector3 bezierDragPlanePoint;
	private Vector3 bezierDragPlaneNormal;
	private Vector3 bezierDragIntersection;
	private CadPoint3 bezierDragPoint;

	internal event Action<BezierDraftState> BezierCommitRequested;
	internal event Action BezierDraftCancelled;
	internal event Action<BezierDraftState, RunnerPathPointKind> BezierDraftPreviewChanged;
	internal event Action<CurveDisplayOverlay, CurveDisplayControl> CurveControlSelected;

	internal CurveDisplayOverlay DisplayedCurveOverlay => curveOverlayState.Overlay;

	internal void SetCurveDisplaySelection(CurveOverlayIdentity? identity)
	{
		bool changedOwner = curveOverlayState.Selection.HasValue != identity.HasValue
			|| (curveOverlayState.Selection.HasValue
				&& identity.HasValue
				&& !curveOverlayState.Selection.Value.HasSameSelection(identity.Value));
		curveOverlayState.Select(identity);
		if (changedOwner)
		{
			bezierDraft = null;
			activeBezierHandle = null;
			selectedBezierHandle = null;
			activeIm3dBezierId = Im3dInteraction.InvalidId;
		}
	}

	internal bool TrySetCurveDisplayOverlay(CurveDisplayOverlay overlay)
	{
		return curveOverlayState.TryPublish(overlay);
	}

	internal void ClearBezierEditor()
	{
		curveOverlayState.Clear();
		bezierDraft = null;
		activeBezierHandle = null;
		selectedBezierHandle = null;
		activeIm3dBezierId = Im3dInteraction.InvalidId;
	}

	internal void SetBezierInvalid(Guid ownerId, Guid? elementId)
	{
		curveOverlayState.MarkInvalid(ownerId, elementId);
		if (bezierDraft?.RunnerId == ownerId
			&& (!elementId.HasValue || bezierDraft.NodeId == elementId.Value))
		{
			bezierDraft.IsInvalid = true;
		}
	}

	internal void EndBezierDraft()
	{
		curveOverlayState.EndDraft();
		bezierDraft = null;
		activeBezierHandle = null;
		activeIm3dBezierId = Im3dInteraction.InvalidId;
	}

	internal bool CancelBezierDraft()
	{
		if (!curveOverlayState.DraftActive)
		{
			return false;
		}
		activeBezierHandle = null;
		BezierDraftCancelled?.Invoke();
		return true;
	}

	private bool TryBeginBezierHandle(PickContext context)
	{
		CurveDisplayOverlay overlay = curveOverlayState.Overlay;
		if (overlay == null || !curveOverlayState.HasDisplayForSelection)
		{
			return false;
		}

		CurveDisplayControl selected = null;
		float best = 13;
		foreach (CurveDisplayControl control in overlay.Controls)
		{
			Vector3 projected = camera.WorldToScreen(ToVector(control.Position));
			if (!IsProjectedPointInClip(projected))
			{
				continue;
			}
			float screenDistance = Vector2.Distance(
				new Vector2(projected.X, projected.Y),
				context.LocalPoint);
			if (screenDistance < best)
			{
				best = screenDistance;
				selected = control;
			}
		}
		if (selected == null)
		{
			return false;
		}

		curveOverlayState.SelectControl(selected.Key);
		selectedBezierHandle = selected.RunnerPointKind;
		CurveControlSelected?.Invoke(overlay, selected);
		if (!selected.Editable
			|| !selected.RunnerPointKind.HasValue
			|| !curveOverlayState.TryBeginDraft())
		{
			return true;
		}

		bezierDraft = BezierDraftState.Create(overlay);
		RunnerPathPointKind pointKind = selected.RunnerPointKind.Value;
		activeBezierHandle = pointKind;
		selectedBezierHandle = pointKind;
		BezierDraftPreviewChanged?.Invoke(bezierDraft, pointKind);
		bezierDragPoint = bezierDraft.Point(pointKind);
		bezierDragPlanePoint = ToVector(bezierDragPoint);
		bezierDragPlaneNormal = Vector3.Normalize(focus - camera.Position);
		if (!TryIntersectPlane(
			context.Ray,
			bezierDragPlanePoint,
			bezierDragPlaneNormal,
			out bezierDragIntersection))
		{
			EndBezierDraft();
			return false;
		}
		return true;
	}

	private void UpdateBezierDrag(CadRect bounds, Vector2 mouse)
	{
		if (!activeBezierHandle.HasValue || bezierDraft == null)
		{
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
		bool wasDirty = bezierDraft.IsDirty;
		bool changed = bezierDraft.MoveWorldPoint(
			activeBezierHandle.Value,
			bezierDragPoint + CadPoint3.FromVector3(delta));
		if (changed)
		{
			if (!wasDirty)
			{
				MarkRunnerStale(bezierDraft.RunnerId);
			}
			BezierDraftPreviewChanged?.Invoke(bezierDraft, activeBezierHandle.Value);
		}
	}

	private void CompleteBezierDrag()
	{
		if (!activeBezierHandle.HasValue)
		{
			return;
		}
		activeBezierHandle = null;
		if (bezierDraft?.IsDirty == true)
		{
			BezierCommitRequested?.Invoke(bezierDraft);
		}
		else
		{
			EndBezierDraft();
		}
	}

	private void DrawBezierEditor(RenderPass pass)
	{
		CurveDisplayOverlay overlay = curveOverlayState.Overlay;
		if (bezierDraft == null
			&& (overlay == null || !curveOverlayState.HasDisplayForSelection))
		{
			return;
		}

		using IDisposable stateScope = pass.PushState(pass.State with
		{
			DepthTestEnabled = false,
			DepthWriteEnabled = false,
		});

		if (bezierDraft != null)
		{
			DrawBezierDraft(pass);
			return;
		}

		Color polygon = new(135, 140, 150);
		foreach (CurveDisplaySpan span in overlay.Spans)
		{
			Color curve = OverlayCurveColor(overlay, span);
			CadPoint3[] polygonPoints =
			{
				span.Start,
				span.Control1,
				span.Control2,
				span.End,
			};
			for (int index = 1; index < polygonPoints.Length; ++index)
			{
				pass.DrawLine(
					new Vertex3(ToVector(polygonPoints[index - 1]), polygon),
					new Vertex3(ToVector(polygonPoints[index]), polygon),
					2);
			}
			DrawSampledCurve(pass, span.Samples, curve);
		}

		float radius = Math.Max(distance * 0.009f, 2);
		foreach (CurveDisplayControl control in overlay.Controls)
		{
			candidateSphere.DefaultColor = control.Editable
				? EditableControlColor(control.RunnerPointKind)
				: new Color(145, 150, 160);
			using (pass.PushModel(
				Matrix4x4.CreateScale(radius)
					* Matrix4x4.CreateTranslation(ToVector(control.Position))))
			{
				pass.DrawMesh(candidateSphere);
			}
		}

		if (overlay.RunnerExitFrame.HasValue)
		{
			DrawBezierExitFrame(pass, overlay.RunnerExitFrame.Value, radius);
		}
	}

	private void DrawBezierDraft(RenderPass pass)
	{
		Color polygon = new(135, 155, 180);
		Color curve = bezierDraft.IsInvalid
			? new Color(245, 65, 70)
			: new Color(70, 215, 245);
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
				2);
		}

		CadPoint3[] samples = new CadPoint3[49];
		for (int segment = 0; segment < samples.Length; ++segment)
		{
			samples[segment] = bezierDraft.Sample(segment / 48.0);
		}
		DrawSampledCurve(pass, samples, curve);

		float radius = Math.Max(distance * 0.009f, 2);
		foreach (RunnerPathPointKind kind in Enum.GetValues<RunnerPathPointKind>())
		{
			candidateSphere.DefaultColor = bezierDraft.CanEdit(kind)
				? EditableControlColor(kind)
				: new Color(145, 150, 160);
			using (pass.PushModel(
				Matrix4x4.CreateScale(radius)
					* Matrix4x4.CreateTranslation(ToVector(bezierDraft.Point(kind)))))
			{
				pass.DrawMesh(candidateSphere);
			}
		}
		DrawBezierExitFrame(pass, bezierDraft.AuthoritativeExitFrame, radius);
	}

	private static Color EditableControlColor(RunnerPathPointKind? kind)
	{
		return kind switch
		{
			RunnerPathPointKind.Control1 => new Color(255, 190, 55),
			RunnerPathPointKind.Control2 => new Color(75, 220, 245),
			RunnerPathPointKind.End => new Color(235, 90, 205),
			_ => new Color(145, 150, 160),
		};
	}

	private Color OverlayCurveColor(
		CurveDisplayOverlay overlay,
		CurveDisplaySpan span)
	{
		if (span.Validity == CurveDisplayValidity.Invalid)
		{
			return new Color(245, 65, 70);
		}
		if (curveOverlayState.EffectiveFreshness == CurveDisplayFreshness.Stale)
		{
			return new Color(235, 145, 55);
		}
		return overlay.Identity.OwnerKind == CurveDisplayOwnerKind.RunnerBezier
			? new Color(70, 215, 245)
			: new Color(245, 90, 210);
	}

	private static void DrawSampledCurve(
		RenderPass pass,
		IReadOnlyList<CadPoint3> samples,
		Color color)
	{
		for (int index = 1; index < samples.Count; ++index)
		{
			pass.DrawLine(
				new Vertex3(ToVector(samples[index - 1]), color),
				new Vertex3(ToVector(samples[index]), color),
				4);
		}
	}

	private static void DrawBezierExitFrame(RenderPass pass, CadFrame exit, float radius)
	{
		float axisLength = radius * 4;
		Vector3 exitOrigin = ToVector(exit.Origin);
		pass.DrawLine(
			new Vertex3(exitOrigin, Color.Red),
			new Vertex3(exitOrigin + ToVector(exit.Tangent) * axisLength, Color.Red),
			3);
		pass.DrawLine(
			new Vertex3(exitOrigin, Color.Green),
			new Vertex3(exitOrigin + ToVector(exit.Normal) * axisLength, Color.Green),
			3);
		pass.DrawLine(
			new Vertex3(exitOrigin, Color.Blue),
			new Vertex3(exitOrigin + ToVector(exit.Binormal) * axisLength, Color.Blue),
			3);
	}

	private static bool TryIntersectPlane(
		PickingRay ray,
		Vector3 planePoint,
		Vector3 planeNormal,
		out Vector3 intersection)
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
