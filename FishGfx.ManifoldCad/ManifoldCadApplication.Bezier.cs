using FishGfx.Cad;

namespace FishGfx.ManifoldCad;

internal sealed partial class ManifoldCadApplication
{
	private void UpdateBezierEditor(RunnerNode node)
	{
		bezierInspectorProperties = Array.Empty<string>();
		RefreshCurveDisplayOverlay();
		if (node?.DefinitionId != RunnerNodes.CubicBezier || ActiveRunner == null)
		{
			return;
		}

		CadCollectorInlet boundInlet = FindBoundInlet(ActiveRunner.Id, node.Id);
		if (boundInlet != null)
		{
			ui.SetStatus(
				"The terminal Bézier P2/P3 controls are collector-constrained. "
					+ "P1 remains editable; select the inlet to move the endpoint.");
		}
	}

	private void RefreshCurveDisplayOverlay()
	{
		long epoch = Interlocked.Read(ref projectEpoch);
		RunnerNode node = nodeCanvas.SelectedNode;
		if (node?.DefinitionId == RunnerNodes.CubicBezier
			&& ActiveRunner != null
			&& ActiveRunner.Graph.Nodes.Any(candidate => candidate.Id == node.Id))
		{
			CadRunner runner = ActiveRunner;
			CadCollectorSystem system = project.CollectorSystems.FirstOrDefault(candidate =>
				candidate.Inlets.Any(inlet => inlet.Binding?.RunnerId == runner.Id));
			CadGenerationStamp stamp = system?.GenerationStamp
				?? new CadGenerationStamp(
					CadGenerationOwnerKind.Runner,
					runner.Id,
					runner.EditRevision);
			CurveOverlayIdentity identity = new(
				CurveDisplayOwnerKind.RunnerBezier,
				runner.Id,
				node.Id,
				epoch,
				runner.EditRevision,
				stamp);
			viewport.SetCurveDisplaySelection(identity);

			RunnerEvaluationResult candidate = evaluations.TryGetValue(
				runner.Id,
				out RunnerEvaluationResult evaluated)
					? evaluated
					: evaluation;
			bool matching = candidate?.RunnerId == runner.Id
				&& candidate.EditRevision == runner.EditRevision
				&& candidate.GenerationStamp == stamp;
			RunnerFeature feature = matching
				? candidate.Chain?.Features.FirstOrDefault(item =>
					item.NodeId == node.Id
					&& item.Kind == RunnerFeatureKind.CubicBezier)
				: null;
			bool invalid = matching && candidate.Diagnostics.Any(diagnostic =>
				diagnostic.Severity == CadDiagnosticSeverity.Error
				&& diagnostic.NodeId == node.Id);
			if (feature != null)
			{
				bool exactCurrent = system?.ExactBuild.Snapshot.IsCurrent
					?? runner.ExactBuild.Snapshot.IsCurrent;
				viewport.TrySetCurveDisplayOverlay(CurveDisplayOverlay.Runner(
					identity,
					feature,
					FindBoundInlet(runner.Id, node.Id) != null,
					exactCurrent,
					invalid));
			}
			else if (invalid)
			{
				viewport.SetBezierInvalid(runner.Id, node.Id);
			}
			return;
		}

		CadCollectorSystem activeSystem = project.ActiveCollectorSystem;
		if (activeSystem != null)
		{
			Guid? inletId = project.View.ActiveCollectorInletId;
			CurveDisplayOwnerKind ownerKind = inletId.HasValue
				? CurveDisplayOwnerKind.CollectorBranch
				: CurveDisplayOwnerKind.CollectorSystem;
			CurveOverlayIdentity identity = new(
				ownerKind,
				activeSystem.Id,
				inletId,
				epoch,
				activeSystem.GenerationRevision,
				activeSystem.GenerationStamp);
			viewport.SetCurveDisplaySelection(identity);
			bool exactCurrent = activeSystem.ExactBuild.Snapshot.IsCurrent;
			CurveDisplayOverlay overlay;
			if (inletId.HasValue)
			{
				CadCollectorInlet inlet = activeSystem.Inlets.FirstOrDefault(
					item => item.Id == inletId.Value);
				if (inlet == null)
				{
					viewport.ClearBezierEditor();
					return;
				}
				overlay = CurveDisplayOverlay.CollectorBranch(
					identity,
					activeSystem,
					inlet,
					exactCurrent);
			}
			else
			{
				overlay = CurveDisplayOverlay.CollectorSystem(
					identity,
					activeSystem,
					exactCurrent);
			}
			viewport.TrySetCurveDisplayOverlay(overlay);
			return;
		}

		viewport.ClearBezierEditor();
	}

	private CadCollectorInlet FindBoundInlet(Guid runnerId, Guid nodeId)
	{
		return project.CollectorSystems
			.SelectMany(system => system.Inlets)
			.FirstOrDefault(inlet => inlet.Binding?.RunnerId == runnerId
				&& inlet.Binding.TerminalBezierNodeId == nodeId);
	}

	private void SelectCurveControl(
		CurveDisplayOverlay overlay,
		CurveDisplayControl control)
	{
		if (!control.Editable)
		{
			ui.SetStatus(overlay.Identity.OwnerKind == CurveDisplayOwnerKind.RunnerBezier
				? "This control is collector-constrained. Select the collector inlet to move it."
				: "Collector branch controls are read-only; edit the inlet/outlet frame or numeric parameters.");
			return;
		}
		if (overlay.Freshness == CurveDisplayFreshness.Stale)
		{
			ui.SetStatus("Wait for the committed curve evaluation before editing this control.");
		}
	}

	private void ShowBezierDraftInInspector(
		BezierDraftState draft,
		RunnerPathPointKind pointKind)
	{
		bezierInspectorProperties = pointKind switch
		{
			RunnerPathPointKind.Control1 => new[] { "startHandleLength" },
			RunnerPathPointKind.Control2 => new[] { "control2T", "control2U", "control2V" },
			RunnerPathPointKind.End => new[] { "endT", "endU", "endV" },
			_ => Array.Empty<string>(),
		};
		ui.SetBezierDraft(draft, pointKind);
	}

	private void CommitBezierDraft(BezierDraftState draft)
	{
		CadRunner runner = project.Runners.FirstOrDefault(candidate => candidate.Id == draft.RunnerId);
		RunnerNode node = runner?.Graph.Nodes.FirstOrDefault(candidate => candidate.Id == draft.NodeId);
		if (runner == null || node == null)
		{
			viewport.EndBezierDraft();
			ui.SetStatus("The edited Cubic Bezier node is no longer available.", true);
			RefreshCurveDisplayOverlay();
			return;
		}

		draft.Commit(node);
		viewport.EndBezierDraft();
		CommitRunnerOrSystemEdit(runner);
		RefreshCurveDisplayOverlay();
		RegenerateRunner(runner);
		if (runner == ActiveRunner)
		{
			ui.SetNode(node);
		}
	}

	private void RestoreCommittedBezierDraft()
	{
		viewport.EndBezierDraft();
		if (ActiveRunner?.ExactBuild.Snapshot.IsCurrent == true)
		{
			viewport.MarkRunnerCurrent(ActiveRunner.Id);
		}
		else if (ActiveRunner != null)
		{
			viewport.MarkRunnerStale(ActiveRunner.Id);
		}
		RefreshCurveDisplayOverlay();
		RunnerNode node = nodeCanvas.SelectedNode;
		if (node != null)
		{
			ui.SetNode(node);
		}
	}
}
