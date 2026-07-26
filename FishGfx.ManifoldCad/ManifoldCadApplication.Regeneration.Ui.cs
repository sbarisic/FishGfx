using FishGfx.Cad;

namespace FishGfx.ManifoldCad;

internal sealed partial class ManifoldCadApplication
{
	private void ApplyRunnerRegeneration(RunnerRegenerationCompletion completion)
	{
		if (completion.Request.ProjectEpoch != Interlocked.Read(ref projectEpoch))
		{
			return;
		}
		CadRunner current = project.Runners.FirstOrDefault(
			runner => runner.Id == completion.Request.Runner.Id);
		if (current == null
			|| current.EditRevision != completion.Request.Revision
			|| project.CollectorSystems.Any(system =>
				system.Inlets.Any(inlet => inlet.Binding?.RunnerId == current.Id)))
		{
			return;
		}
		if (completion.Error != null)
		{
			if (completion.Error is OperationCanceledException
				&& completion.Request.ExactBuild
				&& IsPublished(
					completion.Request.ExactState.Snapshot,
					new ExactArtifactSnapshot(
						completion.Request.Revision,
						completion.Request.DependencyHash
					)
				))
			{
				return;
			}
			if (completion.Request.ExactBuild)
			{
				if (completion.Error is OperationCanceledException)
				{
					completion.Request.ExactState.Cancel(completion.Request.Revision);
				}
				else
				{
					completion.Request.ExactState.Fail(
						completion.Request.Revision,
						completion.Error.Message
					);
				}
			}
			ApplicationLog.Current?.Exception(
				$"Runner regeneration failed: id={current.Id}; name={current.Name}.",
				completion.Error
			);
			runnerBuildErrors[current.Id] = completion.Error.Message;
			viewport.MarkRunnerStale(current.Id);
			viewport.SetBezierInvalid(
				current.Id,
				completion.Evaluation?.Diagnostics
					.FirstOrDefault(diagnostic => diagnostic.NodeId.HasValue)
					?.NodeId
			);
			ui.SetStatus($"{current.Name}: {completion.Error.Message}", true);
			RefreshUi();
			return;
		}

		evaluations[current.Id] = completion.Evaluation;
		if (current == ActiveRunner)
		{
			evaluation = completion.Evaluation;
		}
		if (!completion.Request.ExactBuild)
		{
			runnerBuildErrors.Remove(current.Id);
			ui.SetStatus(
				$"{current.Name}: preview evaluated in "
					+ $"{completion.EvaluationMilliseconds} ms; exact geometry is stale."
			);
			RefreshUi();
			return;
		}
		if (!completion.Request.ExactState.TryPublish(
			completion.Request.Revision,
			completion.Request.DependencyHash
		))
		{
			viewport.MarkRunnerStale(current.Id);
			return;
		}
		viewport.AddOrReplace(null, current.Id, completion.Preview, true);
		if (current == ActiveRunner)
		{
			UpdateBezierEditor(nodeCanvas.SelectedNode);
		}
		runnerBuildErrors.Remove(current.Id);
		ApplicationLog.Current?.Info(
			$"Runner regeneration completed asynchronously: id={current.Id}; "
				+ $"revision={current.EditRevision}; "
				+ $"evalMs={completion.EvaluationMilliseconds}; "
				+ $"buildMs={completion.BuildMilliseconds}; "
				+ $"meshMs={completion.TessellationMilliseconds}"
		);
		ui.SetStatus(
			$"{current.Name} {completion.Evaluation.LengthMillimetres:F2} mm | "
				+ $"eval {completion.EvaluationMilliseconds} ms, "
				+ $"build {completion.BuildMilliseconds} ms, "
				+ $"mesh {completion.TessellationMilliseconds} ms"
		);
		RefreshUi();
	}

	private void ApplyCollectorDraftPreview(
		CollectorRegenerationRequest request,
		IReadOnlyDictionary<Guid, RunnerEvaluationResult> previewEvaluations
	)
	{
		if (request.ProjectEpoch != Interlocked.Read(ref projectEpoch))
		{
			return;
		}
		CadCollectorSystem current = project.CollectorSystems.FirstOrDefault(
			system => system.Id == request.System.Id);
		if (current == null || current.GenerationRevision != request.Revision)
		{
			return;
		}
		viewport.SetCollectorDraft(
			current,
			null,
			current.OutletFrame,
			false,
			previewEvaluations
		);
		ApplicationLog.Current?.Info(
			$"Collector shaded preview published: id={current.Id}; "
				+ $"revision={current.GenerationRevision}; "
				+ $"evaluatedRunners={previewEvaluations.Count}; "
				+ $"previewMeshes={viewport.CollectorDraftMeshCount}"
		);
		ui.SetStatus(request.ExactBuild
			? $"{current.Name}: preview ready; building exact collector..."
			: $"{current.Name}: preview ready; exact collector is stale.");
	}

	private void ApplyCollectorRegeneration(CollectorRegenerationCompletion completion)
	{
		if (completion.Request.ProjectEpoch != Interlocked.Read(ref projectEpoch))
		{
			return;
		}
		CadCollectorSystem current = project.CollectorSystems.FirstOrDefault(
			system => system.Id == completion.Request.System.Id);
		if (current == null || current.GenerationRevision != completion.Request.Revision)
		{
			return;
		}
		if (completion.Error != null)
		{
			if (completion.Error is OperationCanceledException
				&& completion.Request.ExactBuild
				&& IsPublished(
					completion.Request.ExactState.Snapshot,
					new ExactArtifactSnapshot(
						completion.Request.Revision,
						completion.Request.DependencyHash
					)
				))
			{
				return;
			}
			if (completion.Request.ExactBuild)
			{
				if (completion.Error is OperationCanceledException)
				{
					completion.Request.ExactState.Cancel(completion.Request.Revision);
				}
				else
				{
					completion.Request.ExactState.Fail(
						completion.Request.Revision,
						completion.Error.Message
					);
				}
				foreach ((Guid runnerId, CadExactBuildState state) in
					completion.Request.RunnerExactStates)
				{
					long revision = completion.Request.Runners[runnerId].EditRevision;
					if (completion.Error is OperationCanceledException)
					{
						state.Cancel(revision);
					}
					else
					{
						state.Fail(revision, completion.Error.Message);
					}
				}
			}
			ApplicationLog.Current?.Exception(
				$"Collector regeneration failed: id={current.Id}; name={current.Name}; "
					+ $"revision={current.GenerationRevision}.",
				completion.Error
			);
			current.IsResolved = false;
			current.Diagnostic = completion.Error.Message;
			viewport.MarkRunnerStale(current.Id);
			viewport.SetCollectorDraft(
				current,
				null,
				current.OutletFrame,
				true,
				evaluations
			);
			foreach (CadCollectorInlet inlet in current.Inlets)
			{
				runnerBuildErrors[inlet.Binding.RunnerId] = completion.Error.Message;
			}
			ui.SetStatus($"{current.Name}: {completion.Error.Message}", true);
			RefreshUi();
			return;
		}

		if (!completion.Request.ExactBuild)
		{
			foreach ((Guid runnerId, RunnerEvaluationResult result) in completion.Evaluations)
			{
				evaluations[runnerId] = result;
				runnerBuildErrors.Remove(runnerId);
			}
			ui.SetStatus(
				$"{current.Name}: preview evaluated in "
					+ $"{completion.EvaluationMilliseconds} ms; exact geometry is stale."
			);
			RefreshUi();
			return;
		}
		if (!completion.Request.ExactState.TryPublish(
			completion.Request.Revision,
			completion.Request.DependencyHash
		))
		{
			viewport.MarkRunnerStale(current.Id);
			return;
		}
		foreach ((Guid runnerId, CadExactBuildState state) in
			completion.Request.RunnerExactStates)
		{
			state.TryPublish(
				completion.Request.Runners[runnerId].EditRevision,
				completion.Request.RunnerDependencyHashes[runnerId]
			);
		}

		foreach (CadCollectorInlet inlet in current.Inlets)
		{
			Guid runnerId = inlet.Binding.RunnerId;
			evaluations[runnerId] = completion.Evaluations[runnerId];
			if (ActiveRunner?.Id == runnerId)
			{
				evaluation = completion.Evaluations[runnerId];
			}
			viewport.RemoveRunner(runnerId);
			runnerBuildErrors.Remove(runnerId);
		}
		viewport.AddOrReplace(null, current.Id, completion.Preview, true);
		current.IsResolved = true;
		current.Diagnostic = null;
		ApplicationLog.Current?.Info(
			$"Collector regeneration completed: id={current.Id}; "
				+ $"revision={current.GenerationRevision}; "
				+ $"rebuiltRunners={completion.RebuiltRunnerCount}/{current.Inlets.Count}; "
				+ $"evalMs={completion.EvaluationMilliseconds}; "
				+ $"buildMs={completion.BuildMilliseconds}; "
				+ $"meshMs={completion.TessellationMilliseconds}"
		);
		ui.SetStatus(
			$"{current.Name} fused {current.Inlets.Count}->1 | "
				+ $"runners {completion.RebuiltRunnerCount}/{current.Inlets.Count}, "
				+ $"build {completion.BuildMilliseconds} ms, "
				+ $"mesh {completion.TessellationMilliseconds} ms"
		);
		RefreshUi();
	}

	private void WaitForRegeneration()
	{
		while (true)
		{
			Task worker;
			bool idle;
			lock (regenerationLock)
			{
				idle = !regenerationWorkerRunning && pendingRegenerations.Count == 0;
				worker = regenerationWorker;
			}
			if (!idle)
			{
				worker?.GetAwaiter().GetResult();
				continue;
			}
			while (mainThreadActions.TryDequeue(out Action action))
			{
				TryOperation(action);
			}
			lock (regenerationLock)
			{
				if (!regenerationWorkerRunning
					&& pendingRegenerations.Count == 0
					&& mainThreadActions.IsEmpty)
				{
					return;
				}
			}
		}
	}

	private bool HasPendingRegeneration()
	{
		lock (regenerationLock)
		{
			return regenerationWorkerRunning
				|| pendingRegenerations.Count > 0
				|| !mainThreadActions.IsEmpty;
		}
	}

	private void ClearCollectorRunnerGeometry()
	{
		lock (collectorRunnerGeometry)
		{
			collectorRunnerGeometry.Clear();
		}
	}
}
