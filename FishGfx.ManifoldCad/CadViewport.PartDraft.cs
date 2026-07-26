using System.Numerics;
using FishGfx.Cad;
using FishGfx.Graphics;

namespace FishGfx.ManifoldCad;

internal sealed partial class CadViewport
{
	private PartDraftVisual? partDraftVisual;
	private readonly List<CadPoint3[]> partDraftRunnerCurves = [];

	internal void SetPartDraft(CadPart part, CadTransform original, CadTransform draft)
	{
		ArgumentNullException.ThrowIfNull(part);
		partDraftVisual = new PartDraftVisual(part.Id, original, draft);
	}

	internal void ClearPartDraft()
	{
		partDraftVisual = null;
		partDraftRunnerCurves.Clear();
	}

	internal void MarkAffectedGeometryStale(ManifoldProject project, Guid partId)
	{
		HashSet<Guid> mateIds = project.Mates
			.Where(mate => mate.PartId == partId)
			.Select(mate => mate.Id)
			.ToHashSet();
		HashSet<Guid> runnerIds = project.Runners
			.Where(runner => mateIds.Contains(runner.StartMateId))
			.Select(runner => runner.Id)
			.ToHashSet();
		foreach (Guid runnerId in runnerIds)
		{
			MarkRunnerStale(runnerId);
		}
		foreach (CadCollectorSystem system in project.CollectorSystems.Where(system =>
			system.Inlets.Any(inlet => runnerIds.Contains(inlet.Binding?.RunnerId ?? Guid.Empty))))
		{
			MarkRunnerStale(system.Id);
		}
	}

	internal void RestoreAffectedGeometryFreshness(ManifoldProject project, Guid partId)
	{
		HashSet<Guid> mateIds = project.Mates
			.Where(mate => mate.PartId == partId)
			.Select(mate => mate.Id)
			.ToHashSet();
		foreach (CadRunner runner in project.Runners.Where(runner => mateIds.Contains(runner.StartMateId)))
		{
			if (runner.ExactBuild.IsCurrent)
			{
				MarkRunnerCurrent(runner.Id);
			}
			CadCollectorSystem system = project.CollectorSystems.FirstOrDefault(candidate =>
				candidate.Inlets.Any(inlet => inlet.Binding?.RunnerId == runner.Id));
			if (system?.ExactBuild.IsCurrent == true)
			{
				MarkRunnerCurrent(system.Id);
			}
		}
	}

	internal void UpdatePartAttachedPreviews(
		ManifoldProject project,
		IReadOnlyDictionary<Guid, RunnerEvaluationResult> evaluations,
		Guid partId,
		CadTransform original,
		CadTransform draft)
	{
		partDraftRunnerCurves.Clear();
		HashSet<Guid> mateIds = project.Mates
			.Where(mate => mate.PartId == partId)
			.Select(mate => mate.Id)
			.ToHashSet();
		foreach (CadRunner runner in project.Runners.Where(runner => mateIds.Contains(runner.StartMateId)))
		{
			if (!evaluations.TryGetValue(runner.Id, out RunnerEvaluationResult evaluation)
				|| evaluation?.Success != true)
			{
				continue;
			}

			RunnerEndpointConstraint? constraint = project.GetEndpointConstraint(runner);
			foreach (RunnerFeature feature in evaluation.Chain.Features)
			{
				CadPoint3[] samples = SampleFeature(feature);
				for (int index = 0; index < samples.Length; index++)
				{
					samples[index] = TransformBetween(samples[index], original, draft);
				}

				if (feature.Kind == RunnerFeatureKind.CubicBezier
					&& constraint.HasValue
					&& feature.NodeId == constraint.Value.TerminalBezierNodeId)
				{
					CadPoint3 p0 = TransformBetween(feature.EntryFrame.Origin, original, draft);
					CadPoint3 p1 = TransformBetween(feature.Control1, original, draft);
					CadPoint3 p2 = feature.Control2;
					CadPoint3 p3 = feature.ExitFrame.Origin;
					for (int index = 0; index < samples.Length; index++)
					{
						double parameter = index / (double)(samples.Length - 1);
						samples[index] = SampleBezier(p0, p1, p2, p3, parameter);
					}
				}
				partDraftRunnerCurves.Add(samples);
			}
		}
	}

	private Matrix4x4 PartDraftModel(Guid? partId)
	{
		if (!partId.HasValue || partDraftVisual is not PartDraftVisual draft || draft.PartId != partId.Value)
		{
			return Matrix4x4.Identity;
		}

		Matrix4x4 original = ToMatrix(draft.OriginalTransform);
		if (!Matrix4x4.Invert(original, out Matrix4x4 inverse))
		{
			return Matrix4x4.Identity;
		}
		return inverse * ToMatrix(draft.Transform);
	}

	private CadFrame PartDraftFrame(Guid partId, CadFrame frame)
	{
		if (partDraftVisual is not PartDraftVisual draft || draft.PartId != partId)
		{
			return frame;
		}

		return new CadFrame(
			TransformBetween(frame.Origin, draft.OriginalTransform, draft.Transform),
			TransformDirectionBetween(frame.Tangent, draft.OriginalTransform, draft.Transform),
			TransformDirectionBetween(frame.Normal, draft.OriginalTransform, draft.Transform));
	}

	private CadPoint3 PartDraftPoint(Guid partId, CadPoint3 point)
	{
		return partDraftVisual is PartDraftVisual draft && draft.PartId == partId
			? TransformBetween(point, draft.OriginalTransform, draft.Transform)
			: point;
	}

	private CadPoint3 PartDraftDirection(Guid partId, CadPoint3 direction)
	{
		return partDraftVisual is PartDraftVisual draft && draft.PartId == partId
			? TransformDirectionBetween(direction, draft.OriginalTransform, draft.Transform)
			: direction;
	}

	private void DrawPartDraftRunnerCurves(RenderPass pass)
	{
		if (partDraftRunnerCurves.Count == 0)
		{
			return;
		}

		using IDisposable state = pass.PushState(pass.State with
		{
			DepthTestEnabled = false,
			DepthWriteEnabled = false,
		});
		Color color = new(70, 215, 245, 220);
		foreach (CadPoint3[] curve in partDraftRunnerCurves)
		{
			for (int index = 1; index < curve.Length; index++)
			{
				pass.DrawLine(
					new Vertex3(ToVector(curve[index - 1]), color),
					new Vertex3(ToVector(curve[index]), color),
					3);
			}
		}
	}

	private static CadPoint3[] SampleFeature(RunnerFeature feature)
	{
		if (feature.Kind == RunnerFeatureKind.CubicBezier)
		{
			return SampleCubicBezier(feature);
		}
		if (feature.Kind != RunnerFeatureKind.Bend || feature.RadiusMillimetres <= 0)
		{
			return [feature.EntryFrame.Origin, feature.ExitFrame.Origin];
		}

		const int sampleCount = 17;
		CadPoint3[] samples = new CadPoint3[sampleCount];
		CadPoint3 startRadius = feature.EntryFrame.Origin - feature.Center;
		CadPoint3 axis = CadPoint3.Cross(startRadius, feature.EntryFrame.Tangent).Normalized();
		for (int index = 0; index < sampleCount; index++)
		{
			double parameter = index / (double)(sampleCount - 1);
			samples[index] = feature.Center + CadFrame.RotateAround(
				startRadius,
				axis,
				feature.SweepRadians * parameter);
		}
		return samples;
	}

	private static CadPoint3 SampleBezier(
		CadPoint3 p0,
		CadPoint3 p1,
		CadPoint3 p2,
		CadPoint3 p3,
		double parameter)
	{
		double inverse = 1 - parameter;
		return p0 * (inverse * inverse * inverse)
			+ p1 * (3 * inverse * inverse * parameter)
			+ p2 * (3 * inverse * parameter * parameter)
			+ p3 * (parameter * parameter * parameter);
	}

	private static CadPoint3 TransformBetween(CadPoint3 point, CadTransform original, CadTransform draft)
	{
		CadQuaternion inverse = Conjugate(original.Rotation.Normalized());
		CadPoint3 local = inverse.Rotate(point - original.Translation);
		return draft.TransformPoint(local);
	}

	private static CadPoint3 TransformDirectionBetween(CadPoint3 direction, CadTransform original, CadTransform draft)
	{
		CadQuaternion inverse = Conjugate(original.Rotation.Normalized());
		return draft.TransformDirection(inverse.Rotate(direction));
	}

	private static CadQuaternion Conjugate(CadQuaternion value) => new(-value.X, -value.Y, -value.Z, value.W);

	private static Matrix4x4 ToMatrix(CadTransform transform)
	{
		CadQuaternion rotation = transform.Rotation.Normalized();
		return Matrix4x4.CreateFromQuaternion(new Quaternion(
			(float)rotation.X,
			(float)rotation.Y,
			(float)rotation.Z,
			(float)rotation.W)) * Matrix4x4.CreateTranslation(ToVector(transform.Translation));
	}

	private readonly record struct PartDraftVisual(
		Guid PartId,
		CadTransform OriginalTransform,
		CadTransform Transform);
}
