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
					CadTessellation stored = document.TessellateRunnerAsync(runner.Id)
						.GetAwaiter().GetResult().Value;
					viewport.AddOrReplace(null, runner.Id, stored, true, true);
				}
				catch (CadKernelException)
				{
					// A runner that never generated successfully has no archived exact shape.
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
		IReadOnlyList<RunnerEvaluationResult> results = project.EvaluateRunners();
		if (results.Count == 0
			|| results.Any(result => !result.Success)
			|| project.Runners.Any(runner => runnerBuildErrors.ContainsKey(runner.Id)
				|| !evaluations.ContainsKey(runner.Id)))
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

}
