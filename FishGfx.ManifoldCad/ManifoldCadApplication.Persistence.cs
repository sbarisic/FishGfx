using FishGfx.Cad;

namespace FishGfx.ManifoldCad;

internal sealed partial class ManifoldCadApplication
{
	private void SaveProject(string path)
	{
		TryOperation(() =>
		{
			EnsureExactSnapshotCurrent();
			SaveArchive(path);
			ui.SetStatus($"Saved {Path.GetFileName(path)} atomically.");
		});
	}

	private void SaveDraft(string path)
	{
		TryOperation(() =>
		{
			WaitForRegeneration();
			SaveArchive(path, true);
			ui.SetStatus(
				$"Saved draft {Path.GetFileName(path)} with previous exact geometry marked stale."
			);
		});
	}

	private void SaveArchive(string path, bool draft = false)
	{
		viewport.CaptureView(project.View);
		nodeCanvas.CaptureView(project.View);
		string temporary = Path.Combine(Path.GetTempPath(), $"fishgfx-{Guid.NewGuid():N}.xbf");
		try
		{
			document.SaveXcafAsync(temporary).GetAwaiter().GetResult();
			CadProjectArchive.Save(
				path,
				project,
				File.ReadAllBytes(temporary),
				draft
			);
		}
		finally
		{
			File.Delete(temporary);
		}
	}

	private void OpenProject(string path)
	{
		TryOperation(() =>
		{
			WaitForRegeneration();
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
			ClearCollectorRunnerGeometry();
			Interlocked.Increment(ref projectEpoch);
			while (mainThreadActions.TryDequeue(out _))
			{
			}
			runnerBuildErrors.Clear();
			eulerByPart.Clear();
			hasSelectedTopology = false;
			selectedTopology = null;
			project = package.Project;
			RestoreResolvedMateSelectorsAsync(document, project)
				.GetAwaiter()
				.GetResult();
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
					viewport.AddOrReplace(
						null,
						runner.Id,
						stored,
						true,
						!runner.ExactBuild.IsCurrent
					);
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
					viewport.AddOrReplace(
						null,
						system.Id,
						stored,
						true,
						!system.ExactBuild.IsCurrent
					);
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
			RestoreArchivedEvaluations();

			viewport.SetActiveRunner(ActiveRunner?.Id);
			viewport.RestoreView(project.View);
			nodeCanvas.RestoreView(project.View);
			RefreshUi();
			ui.SetStatus(package.ExactGeometryFresh
				? $"Opened {Path.GetFileName(path)} with current archived exact geometry."
				: $"Opened {Path.GetFileName(path)}; archived exact geometry is stale.");
		});
	}

	internal static async Task RestoreResolvedMateSelectorsAsync(
		CadDocument document,
		ManifoldProject project,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(document);
		ArgumentNullException.ThrowIfNull(project);

		foreach (CadMate mate in project.Mates.Where(candidate => candidate.IsResolved))
		{
			await document.BindMateSelectorAsync(mate, cancellationToken);
		}
	}

	private void RestoreArchivedEvaluations()
	{
		foreach (CadRunner runner in project.Runners)
		{
			try
			{
				RunnerEvaluationResult restored = project.EvaluateRunnerAsync(document, runner)
					.GetAwaiter()
					.GetResult();
				evaluations[runner.Id] = restored;
				if (restored.Success)
				{
					runnerBuildErrors.Remove(runner.Id);
				}
				else
				{
					runnerBuildErrors[runner.Id] = string.Join(
						Environment.NewLine,
						restored.Diagnostics.Select(diagnostic => diagnostic.Message)
					);
				}
			}
			catch (Exception exception)
			{
				runnerBuildErrors[runner.Id] = exception.Message;
			}
		}
		evaluation = ActiveRunner != null
			&& evaluations.TryGetValue(ActiveRunner.Id, out RunnerEvaluationResult active)
				? active
				: null;
		foreach (CadCollectorSystem system in project.CollectorSystems)
		{
			string[] failures = system.Inlets
				.Select(inlet => inlet.Binding?.RunnerId)
				.Where(runnerId => runnerId.HasValue
					&& runnerBuildErrors.ContainsKey(runnerId.Value))
				.Select(runnerId => runnerBuildErrors[runnerId.Value])
				.Distinct(StringComparer.Ordinal)
				.ToArray();
			if (failures.Length > 0)
			{
				system.IsResolved = false;
				system.Diagnostic = string.Join(Environment.NewLine, failures);
			}
		}
	}

	private void ExportStep(string path)
	{
		TryOperation(() =>
		{
			EnsureExactSnapshotCurrent();
			if (!CanExportProject(project, evaluations, runnerBuildErrors))
			{
				throw new InvalidOperationException(
					"Export is disabled until every runner and collector has current exact geometry."
				);
			}
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
			runner.ExactBuild.IsCurrent
			&&
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
			|| project.CollectorSystems.Any(system =>
				!system.IsResolved || !system.ExactBuild.IsCurrent))
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
