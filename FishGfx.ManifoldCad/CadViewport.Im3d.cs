using System.Numerics;
using FishGfx.Cad;
using FishGfx.Game;
using FishGfx.Graphics;
using FishGfx.Im3d;

namespace FishGfx.ManifoldCad;

internal sealed partial class CadViewport
{
	private uint activeIm3dBezierId;

	private void UpdateIm3d(
		CadRect bounds,
		InputManager input,
		Vector2 mouse,
		float deltaTime,
		bool inside)
	{
		bool continuingInteraction = im3dLifecycle.PreviousActiveId != Im3dInteraction.InvalidId
			|| im3dInteraction.ActiveId != Im3dInteraction.InvalidId;
		Vector2 local = inside || continuingInteraction
			? ToCameraPoint(bounds, mouse)
			: new Vector2(bounds.Width * 0.5f, bounds.Height * 0.5f);
		PickingRay ray = camera.CreatePickingRay(local);
		if (!inside && !continuingInteraction)
		{
			ray = new PickingRay(ray.Origin, -camera.WorldForwardNormal);
		}

		float projectionScale = orthographic
			? Math.Max(distance, 1)
			: 2 * MathF.Tan(camera.VerticalFOV * 0.5f);
		im3dContext.BeginFrame(new Im3dFrameInput(
			ray.Origin,
			ray.Direction,
			Vector3.UnitY,
			camera.Position,
			camera.WorldForwardNormal,
			new Vector2(Math.Max(1, bounds.Width), Math.Max(1, bounds.Height)),
			projectionScale,
			Math.Max(0, deltaTime),
			(inside || continuingInteraction) && input.IsMouseButtonDown(MouseButton.Left),
			orthographic));

		try
		{
			if (hasFrameGizmo)
			{
				(Im3dPose pose, Im3dInteraction interaction) = im3dContext.ManipulatePose(
					frameGizmoId,
					rotationGizmo ? Im3dPoseOperation.Rotate : Im3dPoseOperation.Translate,
					frameGizmoPose);
				HandleFrameGizmoResult(pose, interaction);
			}

			SubmitSelectedBezierGizmo();
		}
		finally
		{
			(im3dDrawData, im3dInteraction) = im3dContext.EndFrame();
		}
		Im3dReleaseTransition? release = im3dLifecycle.ObserveAfterEndFrame(
			im3dInteraction.ActiveId);
		if (release.HasValue)
		{
			HandleIm3dReleaseTransition(release.Value);
		}
	}

	private void HandleFrameGizmoResult(Im3dPose pose, Im3dInteraction interaction)
	{
		frameGizmoPose = pose;
		if (interaction.ActivatedId == frameGizmoId)
		{
			GizmoDraftActivated?.Invoke(pose);
		}
		if (interaction.Changed)
		{
			GizmoPoseChanged?.Invoke(pose);
		}
	}

	private void SubmitSelectedBezierGizmo()
	{
		if (activeBezierHandle.HasValue
			|| !selectedBezierHandle.HasValue
			|| selectedBezierHandle == RunnerPathPointKind.Start)
		{
			return;
		}

		RunnerPathPointKind pointKind = selectedBezierHandle.Value;
		CurveDisplayOverlay overlay = curveOverlayState.Overlay;
		if (bezierDraft == null
			&& (!curveOverlayState.OverlayMatchesSelection
				|| overlay == null
				|| !overlay.Controls.Any(control =>
					control.RunnerPointKind == pointKind && control.Editable)))
		{
			return;
		}
		if (bezierDraft != null && !bezierDraft.CanEdit(pointKind))
		{
			return;
		}
		Guid nodeId = bezierDraft?.NodeId ?? overlay.Identity.ElementId.Value;
		uint id = Im3dId.FromGuid(nodeId, 0x425A0000u + (uint)pointKind);
		CadPoint3 currentPoint = bezierDraft?.Point(pointKind)
			?? overlay.Controls.Single(control =>
				control.RunnerPointKind == pointKind).Position;
		CadFrame entryFrame = bezierDraft?.EntryFrame
			?? overlay.RunnerEntryFrame.Value;
		Vector3 nextPosition;
		Im3dInteraction interaction;
		if (pointKind == RunnerPathPointKind.Control1)
		{
			(nextPosition, interaction) = im3dContext.ManipulateAxisTranslation(
				id,
				ToVector(entryFrame.Origin),
				ToVector(entryFrame.Tangent),
				ToVector(currentPoint),
				Color.Red);
		}
		else
		{
			(Im3dPose pose, Im3dInteraction poseInteraction) = im3dContext.ManipulatePose(
				id,
				Im3dPoseOperation.Translate,
				new Im3dPose(ToVector(currentPoint), Quaternion.Identity));
			nextPosition = pose.Position;
			interaction = poseInteraction;
		}

		if (interaction.ActivatedId == id)
		{
			if (bezierDraft == null)
			{
				if (!curveOverlayState.TryBeginDraft())
				{
					return;
				}
				bezierDraft = BezierDraftState.Create(overlay);
				BezierDraftPreviewChanged?.Invoke(bezierDraft, pointKind);
			}
			activeIm3dBezierId = id;
		}
		if (!interaction.Changed || bezierDraft == null)
		{
			return;
		}

		bool wasDirty = bezierDraft.IsDirty;
		if (bezierDraft.MoveWorldPoint(pointKind, CadPoint3.FromVector3(nextPosition)))
		{
			if (!wasDirty)
			{
				MarkRunnerStale(bezierDraft.RunnerId);
			}
			BezierDraftPreviewChanged?.Invoke(bezierDraft, pointKind);
		}
	}

	private void HandleIm3dReleaseTransition(Im3dReleaseTransition transition)
	{
		uint releasedId = transition.Id;
		if (transition.Suppressed)
		{
			activeIm3dBezierId = Im3dInteraction.InvalidId;
			return;
		}

		if (releasedId == activeIm3dBezierId)
		{
			if (bezierDraft?.IsDirty == true)
			{
				BezierCommitRequested?.Invoke(bezierDraft);
			}
			else
			{
				EndBezierDraft();
			}
			activeIm3dBezierId = Im3dInteraction.InvalidId;
			return;
		}

		if (releasedId == frameGizmoId)
		{
			GizmoCommitRequested?.Invoke(frameGizmoPose);
		}
	}

	private void DrawOutletTangentArrow(RenderPass pass)
	{
		if (!hasFrameGizmo || selectedPart != null)
		{
			return;
		}

		Vector3 origin = frameGizmoPose.Position;
		Vector3 tangent = Vector3.Transform(Vector3.UnitX, frameGizmoPose.Rotation);
		float length = Math.Max(distance * 0.055f, 14);
		Color color = new(255, 155, 45);
		using IDisposable state = pass.PushState(pass.State with
		{
			DepthTestEnabled = false,
			DepthWriteEnabled = false,
		});
		pass.DrawLine(new Vertex3(origin, color), new Vertex3(origin + tangent * length, color), 4);
		pass.DrawPoint(new Vertex3(origin + tangent * length, color), 8);
	}

	private static Im3dPose ToIm3dPose(CadTransform transform)
	{
		CadQuaternion rotation = transform.Rotation.Normalized();
		return new Im3dPose(
			ToVector(transform.Translation),
			Quaternion.Normalize(new Quaternion(
				(float)rotation.X,
				(float)rotation.Y,
				(float)rotation.Z,
				(float)rotation.W)));
	}

	internal static Im3dPose ToIm3dPose(CadFrame frame)
	{
		Matrix4x4 basis = new(
			(float)frame.Tangent.X, (float)frame.Tangent.Y, (float)frame.Tangent.Z, 0,
			(float)frame.Normal.X, (float)frame.Normal.Y, (float)frame.Normal.Z, 0,
			(float)frame.Binormal.X, (float)frame.Binormal.Y, (float)frame.Binormal.Z, 0,
			0, 0, 0, 1);
		return new Im3dPose(ToVector(frame.Origin), Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(basis)));
	}

	internal static CadQuaternion ToCadQuaternion(Quaternion value)
	{
		Quaternion normalized = Quaternion.Normalize(value);
		return new CadQuaternion(normalized.X, normalized.Y, normalized.Z, normalized.W);
	}

	internal static CadFrame ToCadFrame(Im3dPose pose)
	{
		Quaternion rotation = Quaternion.Normalize(pose.Rotation);
		return new CadFrame(
			CadPoint3.FromVector3(pose.Position),
			CadPoint3.FromVector3(Vector3.Transform(Vector3.UnitX, rotation)),
			CadPoint3.FromVector3(Vector3.Transform(Vector3.UnitY, rotation)));
	}
}
