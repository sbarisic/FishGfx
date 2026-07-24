using FishGfx.Cad;

namespace FishGfx.ManifoldCad;

internal sealed partial class ManifoldCadApplication
{
	private void SaveProject(string path)
	{
		TryOperation(() =>
		{
			viewport.CaptureView(project.View);
			nodeCanvas.CaptureView(project.View);
			string temporary = Path.Combine(Path.GetTempPath(), $"fishgfx-{Guid.NewGuid():N}.xbf");

			try
			{
				document.SaveXcafAsync(temporary).GetAwaiter().GetResult();
				CadProjectArchive.Save(path, project, File.ReadAllBytes(temporary));
			}
			finally
			{
				File.Delete(temporary);
			}

			ui.SetStatus($"Saved {Path.GetFileName(path)} atomically.");
		});
	}

	private void OpenProject(string path)
	{
		TryOperation(() =>
		{
			CadProjectPackage package = CadProjectArchive.Load(path);
			string temporary = Path.Combine(Path.GetTempPath(), $"fishgfx-{Guid.NewGuid():N}.xbf");

			try
			{
				File.WriteAllBytes(temporary, package.ModelDocument);
				document.LoadXcafAsync(temporary).GetAwaiter().GetResult();
			}
			finally
			{
				File.Delete(temporary);
			}

			viewport.ClearScene();
			evaluations.Clear();
			runnerBuildErrors.Clear();
			eulerByPart.Clear();
			hasSelectedTopology = false;
			selectedTopology = null;
			project = package.Project;
			foreach (CadPart part in project.Parts)
			{
				eulerByPart[part.Id] = part.Transform.Rotation.ToEulerDegrees();
			}
			selectedPart = project.Parts.FirstOrDefault();
			selectedMate = project.Mates.FirstOrDefault();

			foreach (CadPart part in project.Parts)
			{
				UploadPart(part);
			}
			foreach (CadRunner runner in project.Runners)
			{
				try
				{
					CadTessellation stored = document.TessellateRunnerAsync(
						runner.Id,
						InteractiveLinearDeflection,
						InteractiveAngularDeflection
					)
						.GetAwaiter().GetResult().Value;
					viewport.AddOrReplace(null, runner.Id, stored, true, true);
				}
				catch (CadKernelException)
				{
					// A runner that never generated successfully has no archived exact shape.
				}
			}
			foreach (CadCollectorSystem system in project.CollectorSystems)
			{
				try
				{
					CadTessellation stored = document.TessellateCollectorSystemAsync(
						system.Id,
						InteractiveLinearDeflection,
						InteractiveAngularDeflection
					)
						.GetAwaiter().GetResult().Value;
					viewport.AddOrReplace(null, system.Id, stored, true, true);
					foreach (CadCollectorInlet inlet in system.Inlets)
					{
						viewport.RemoveRunner(inlet.Binding.RunnerId);
					}
				}
				catch (CadKernelException)
				{
					// A collector that never generated successfully has no archived exact shape.
				}
			}

			RegenerateAllRunners();
			viewport.SetActiveRunner(ActiveRunner?.Id);
			viewport.RestoreView(project.View);
			nodeCanvas.RestoreView(project.View);
			RefreshUi();
			ui.SetStatus($"Opened {Path.GetFileName(path)} without source STEP dependencies.");
		});
	}

	private void ExportStep(string path)
	{
		if (!CanExportProject(project, evaluations, runnerBuildErrors))
		{
			ui.SetStatus("Export is disabled until every runner regenerates successfully.", true);
			return;
		}

		TryOperation(() =>
		{
			document.ExportStepAsync(path).GetAwaiter().GetResult();
			ui.SetStatus($"Exported complete AP242 assembly to {Path.GetFileName(path)}.");
		});
	}

	internal static bool CanExportRunners(
		IReadOnlyList<CadRunner> runners,
		IReadOnlyDictionary<Guid, RunnerEvaluationResult> runnerEvaluations,
		IReadOnlyDictionary<Guid, string> buildErrors
	)
	{
		ArgumentNullException.ThrowIfNull(runners);
		ArgumentNullException.ThrowIfNull(runnerEvaluations);
		ArgumentNullException.ThrowIfNull(buildErrors);

		return runners.Count > 0 && runners.All(runner =>
			!buildErrors.ContainsKey(runner.Id)
			&& runnerEvaluations.TryGetValue(runner.Id, out RunnerEvaluationResult result)
			&& result.Success
			&& result.RunnerId == runner.Id
			&& result.EditRevision == runner.EditRevision);
	}

	internal static bool CanExportProject(
		ManifoldProject project,
		IReadOnlyDictionary<Guid, RunnerEvaluationResult> runnerEvaluations,
		IReadOnlyDictionary<Guid, string> buildErrors
	)
	{
		ArgumentNullException.ThrowIfNull(project);
		if (!CanExportRunners(project.Runners, runnerEvaluations, buildErrors)
			|| project.Runners.Any(runner =>
				project.Mates.All(mate =>
					mate.Id != runner.StartMateId || !mate.IsResolved))
			|| project.CollectorSystems.Any(system => !system.IsResolved))
		{
			return false;
		}
		foreach (CadCollectorSystem system in project.CollectorSystems)
		{
			foreach (CadCollectorInlet inlet in system.Inlets)
			{
				if (!runnerEvaluations.TryGetValue(
					inlet.Binding.RunnerId,
					out RunnerEvaluationResult evaluation)
					|| evaluation.GenerationStamp.OwnerKind
						!= CadGenerationOwnerKind.CollectorSystem
					|| evaluation.GenerationStamp.OwnerId != system.Id
					|| evaluation.GenerationStamp.Revision != system.GenerationRevision)
				{
					return false;
				}
			}
		}
		return true;
	}

}
