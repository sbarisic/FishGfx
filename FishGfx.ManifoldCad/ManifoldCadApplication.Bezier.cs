using FishGfx.Cad;

namespace FishGfx.ManifoldCad;

internal sealed partial class ManifoldCadApplication
{
	private void UpdateBezierEditor(RunnerNode node)
	{
		if (node?.DefinitionId != RunnerNodes.CubicBezier || ActiveRunner == null)
		{
			bezierInspectorProperties = Array.Empty<string>();
			viewport.ClearBezierEditor();
			return;
		}
		RunnerFeature feature = evaluation?.Chain?.Features
			.FirstOrDefault(candidate => candidate.NodeId == node.Id
				&& candidate.Kind == RunnerFeatureKind.CubicBezier);
		if (feature != null)
		{
			viewport.SetBezierEditor(ActiveRunner.Id, node, feature);
		}
	}

	private void ShowBezierDraftInInspector(BezierDraftState draft, RunnerPathPointKind pointKind)
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
			ui.SetStatus("The edited Cubic Bezier node is no longer available.", true);
			return;
		}

		draft.Commit(node);
		runner.CommitEdit();
		RegenerateRunner(runner);
		if (runner == ActiveRunner)
		{
			UpdateBezierEditor(node);
			ui.SetNode(node);
		}
	}

	private void RestoreCommittedBezierDraft()
	{
		RunnerNode node = nodeCanvas.SelectedNode;
		if (node?.DefinitionId != RunnerNodes.CubicBezier || ActiveRunner == null)
		{
			viewport.ClearBezierEditor();
			return;
		}
		RunnerFeature feature = evaluation?.Chain?.Features
			.FirstOrDefault(candidate => candidate.NodeId == node.Id
				&& candidate.Kind == RunnerFeatureKind.CubicBezier);
		if (feature != null)
		{
			viewport.RestoreBezierEditor(ActiveRunner.Id, node, feature);
			viewport.MarkRunnerCurrent(ActiveRunner.Id);
			ui.SetNode(node);
		}
	}
}
