using System.Diagnostics;
using FishGfx.Cad;

namespace FishGfx.ManifoldCad;

internal sealed partial class ManifoldCadApplication
{
	private static readonly TimeSpan PreviewDebounce = TimeSpan.FromMilliseconds(16);

	private abstract record RegenerationRequest(
		Guid OwnerId,
		long Revision,
		long ProjectEpoch,
		DateTimeOffset NotBefore,
		bool ExactBuild,
		string DependencyHash,
		CadExactBuildState ExactState
	);

	private sealed record RunnerRegenerationRequest(
		CadRunner Runner,
		RunnerGraphPlan Plan,
		long ProjectEpoch,
		DateTimeOffset NotBefore,
		bool ExactBuild,
		string DependencyHash,
		CadExactBuildState ExactState
	) : RegenerationRequest(
		Runner.Id,
		Runner.EditRevision,
		ProjectEpoch,
		NotBefore,
		ExactBuild,
		DependencyHash,
		ExactState
	);

	private sealed record CollectorRegenerationRequest(
		CadCollectorSystem System,
		IReadOnlyDictionary<Guid, CadRunner> Runners,
		IReadOnlyDictionary<Guid, RunnerGraphPlan> Plans,
		IReadOnlyDictionary<Guid, string> RunnerDependencyHashes,
		IReadOnlyDictionary<Guid, CadExactBuildState> RunnerExactStates,
		long ProjectEpoch,
		DateTimeOffset NotBefore,
		bool ExactBuild,
		string DependencyHash,
		CadExactBuildState ExactState
	) : RegenerationRequest(
		System.Id,
		System.GenerationRevision,
		ProjectEpoch,
		NotBefore,
		ExactBuild,
		DependencyHash,
		ExactState
	);

	private sealed record RunnerRegenerationCompletion(
		RunnerRegenerationRequest Request,
		RunnerEvaluationResult Evaluation,
		CadTessellation Preview,
		long EvaluationMilliseconds,
		long BuildMilliseconds,
		long TessellationMilliseconds,
		Exception Error
	);

	private sealed record CollectorRegenerationCompletion(
		CollectorRegenerationRequest Request,
		IReadOnlyDictionary<Guid, RunnerEvaluationResult> Evaluations,
		CadTessellation Preview,
		int RebuiltRunnerCount,
		long EvaluationMilliseconds,
		long BuildMilliseconds,
		long TessellationMilliseconds,
		Exception Error
	);

	private void QueueRunnerRegeneration(CadRunner runner, bool exactBuild = false)
	{
		if (runner == null || disposed)
		{
			return;
		}
		CadCollectorSystem collector = project.CollectorSystems.FirstOrDefault(system =>
			system.Inlets.Any(inlet => inlet.Binding?.RunnerId == runner.Id));
		if (collector != null)
		{
			QueueCollectorRegeneration(collector, exactBuild);
			return;
		}

		string dependencyHash = CadGeometryDependencyHash.Runner(project, runner);
		if (exactBuild)
		{
			runner.ExactBuild.Request(runner.EditRevision, dependencyHash);
		}
		else
		{
			runner.ExactBuild.MarkStale(runner.EditRevision, dependencyHash);
		}
		CadRunner snapshot = runner.DeepClone();
		RunnerGraphPlan plan = project.PlanRunner(snapshot);
		viewport.MarkRunnerStale(runner.Id);
		ui.SetStatus($"{runner.Name}: regeneration queued.");
		QueueRegeneration(new RunnerRegenerationRequest(
			snapshot,
			plan,
			Interlocked.Read(ref projectEpoch),
			DateTimeOffset.UtcNow + (exactBuild ? TimeSpan.Zero : PreviewDebounce),
			exactBuild,
			dependencyHash,
			runner.ExactBuild
		));
	}

	private void QueueCollectorRegeneration(CadCollectorSystem system, bool exactBuild = false)
	{
		if (system == null || disposed)
		{
			return;
		}
		CadCollectorSystem snapshot = system.DeepClone();
		if (snapshot.Inlets.Any(inlet => inlet.Binding == null))
		{
			RejectCollectorRegeneration(
				system,
				"Collector regeneration requires every inlet to have a runner binding."
			);
			return;
		}
		Guid[] runnerIds = snapshot.Inlets
			.Select(inlet => inlet.Binding.RunnerId)
			.ToArray();
		if (runnerIds.Distinct().Count() != runnerIds.Length)
		{
			RejectCollectorRegeneration(
				system,
				"A runner cannot be bound to more than one inlet in the same collector."
			);
			return;
		}
		if (runnerIds.Any(runnerId =>
			project.Runners.All(runner => runner.Id != runnerId)))
		{
			RejectCollectorRegeneration(
				system,
				"Collector regeneration references a runner that is no longer in the project."
			);
			return;
		}
		Dictionary<Guid, CadRunner> runners = new();
		Dictionary<Guid, RunnerGraphPlan> plans = new();
		Dictionary<Guid, string> runnerDependencyHashes = new();
		Dictionary<Guid, CadExactBuildState> runnerExactStates = new();
		foreach (CadCollectorInlet inlet in snapshot.Inlets)
		{
			CadRunner current = project.Runners.First(
				runner => runner.Id == inlet.Binding.RunnerId);
			CadRunner runnerSnapshot = current.DeepClone();
			runners.Add(runnerSnapshot.Id, runnerSnapshot);
			plans.Add(runnerSnapshot.Id, project.PlanRunner(runnerSnapshot));
			string runnerHash = CadGeometryDependencyHash.Runner(project, current);
			runnerDependencyHashes.Add(current.Id, runnerHash);
			runnerExactStates.Add(current.Id, current.ExactBuild);
			if (exactBuild)
			{
				current.ExactBuild.Request(current.EditRevision, runnerHash);
			}
			else
			{
				current.ExactBuild.MarkStale(current.EditRevision, runnerHash);
			}
		}
		string dependencyHash = CadGeometryDependencyHash.Collector(project, system);
		if (exactBuild)
		{
			system.ExactBuild.Request(system.GenerationRevision, dependencyHash);
		}
		else
		{
			system.ExactBuild.MarkStale(system.GenerationRevision, dependencyHash);
		}

		system.IsResolved = false;
		system.Diagnostic = "Regeneration is pending.";
		viewport.MarkRunnerStale(system.Id);
		foreach (CadCollectorInlet inlet in system.Inlets)
		{
			viewport.MarkRunnerStale(inlet.Binding.RunnerId);
		}
		ui.SetStatus($"{system.Name}: regeneration queued.");
		QueueRegeneration(new CollectorRegenerationRequest(
			snapshot,
			runners,
			plans,
			runnerDependencyHashes,
			runnerExactStates,
			Interlocked.Read(ref projectEpoch),
			DateTimeOffset.UtcNow + (exactBuild ? TimeSpan.Zero : PreviewDebounce),
			exactBuild,
			dependencyHash,
			system.ExactBuild
		));
	}

	private void RejectCollectorRegeneration(
		CadCollectorSystem system,
		string diagnostic
	)
	{
		system.IsResolved = false;
		system.Diagnostic = diagnostic;
		viewport.MarkRunnerStale(system.Id);
		foreach (CadCollectorInlet inlet in system.Inlets.Where(inlet =>
			inlet.Binding != null))
		{
			viewport.MarkRunnerStale(inlet.Binding.RunnerId);
		}
		ApplicationLog.Current?.Error(
			$"Collector regeneration rejected: id={system.Id}; "
				+ $"revision={system.GenerationRevision}; diagnostic={diagnostic}"
		);
		ui.SetStatus($"{system.Name}: {diagnostic}", true);
	}

	private void QueueRegeneration(RegenerationRequest request)
	{
		lock (regenerationLock)
		{
			pendingRegenerations[request.OwnerId] = request;
			if (!regenerationWorkerRunning)
			{
				regenerationWorkerRunning = true;
				regenerationWorker = Task.Run(
					() => ProcessRegenerationQueueAsync(regenerationCancellation.Token)
				);
			}
		}
	}

	private async Task ProcessRegenerationQueueAsync(CancellationToken cancellationToken)
	{
		while (true)
		{
			cancellationToken.ThrowIfCancellationRequested();
			RegenerationRequest request;
			TimeSpan delay;
			lock (regenerationLock)
			{
				if (pendingRegenerations.Count == 0)
				{
					regenerationWorkerRunning = false;
					return;
				}
				request = pendingRegenerations.Values
					.OrderBy(candidate => candidate.NotBefore)
					.First();
				delay = request.NotBefore - DateTimeOffset.UtcNow;
				if (delay <= TimeSpan.Zero)
				{
					pendingRegenerations.Remove(request.OwnerId);
				}
			}
			if (delay > TimeSpan.Zero)
			{
				await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
				continue;
			}

			if (request is RunnerRegenerationRequest runnerRequest)
			{
				RunnerRegenerationCompletion completion =
					await ExecuteRunnerRegenerationAsync(runnerRequest, cancellationToken)
						.ConfigureAwait(false);
				mainThreadActions.Enqueue(() => ApplyRunnerRegeneration(completion));
			}
			else
			{
				CollectorRegenerationCompletion completion =
					await ExecuteCollectorRegenerationAsync(
						(CollectorRegenerationRequest)request,
						cancellationToken
					).ConfigureAwait(false);
				mainThreadActions.Enqueue(() => ApplyCollectorRegeneration(completion));
			}
		}
	}

	private async Task<RunnerRegenerationCompletion> ExecuteRunnerRegenerationAsync(
		RunnerRegenerationRequest request,
		CancellationToken cancellationToken
	)
	{
		Stopwatch timing = Stopwatch.StartNew();
		RunnerEvaluationResult result = null;
		long evaluationMilliseconds = 0;
		long buildMilliseconds = 0;
		long tessellationMilliseconds = 0;
		bool nativeBuildStaged = false;
		try
		{
			if (request.ExactBuild
				&& !request.ExactState.TryBegin(request.Revision, request.DependencyHash))
			{
				throw new OperationCanceledException(
					"Runner exact build was superseded before it started."
				);
			}
			result = await document.EvaluateRunnerAsync(
				request.Runner,
				request.Plan,
				cancellationToken).ConfigureAwait(false);
			evaluationMilliseconds = timing.ElapsedMilliseconds;
			if (!result.Success)
			{
				throw new InvalidOperationException(string.Join(
					Environment.NewLine,
					result.Diagnostics.Select(diagnostic => diagnostic.Message)
				));
			}
			if (IsSuperseded(request))
			{
				throw new OperationCanceledException("Runner regeneration was superseded.");
			}
			if (!request.ExactBuild)
			{
				return new RunnerRegenerationCompletion(
					request,
					result,
					null,
					evaluationMilliseconds,
					0,
					0,
					null
				);
			}
			cancellationToken.ThrowIfCancellationRequested();
			timing.Restart();
			nativeBuildStaged = true;
			await document.BeginRunnerBuildAsync(
				request.Runner.Id,
				cancellationToken
			).ConfigureAwait(false);
			await document.BuildRunnerAsync(
				request.Runner,
				result,
				cancellationToken
			).ConfigureAwait(false);
			buildMilliseconds = timing.ElapsedMilliseconds;
			if (IsSuperseded(request))
			{
				throw new OperationCanceledException("Runner regeneration was superseded.");
			}
			cancellationToken.ThrowIfCancellationRequested();
			timing.Restart();
			CadTessellation preview = (await document.TessellateRunnerAsync(
				request.Runner.Id,
				InteractiveLinearDeflection,
				InteractiveAngularDeflection,
				cancellationToken
			).ConfigureAwait(false)).Value;
			tessellationMilliseconds = timing.ElapsedMilliseconds;
			if (IsSuperseded(request))
			{
				throw new OperationCanceledException("Runner regeneration was superseded.");
			}
			cancellationToken.ThrowIfCancellationRequested();
			bool published = request.ExactState.TryPublish(
				request.Revision,
				request.DependencyHash,
				() => document.CommitRunnerBuildAsync(
					request.Runner.Id,
					CancellationToken.None
				).GetAwaiter().GetResult()
			);
			if (!published)
			{
				throw new OperationCanceledException(
					"Runner exact publication was superseded before commit."
				);
			}
			nativeBuildStaged = false;
			return new RunnerRegenerationCompletion(
				request,
				result,
				preview,
				evaluationMilliseconds,
				buildMilliseconds,
				tessellationMilliseconds,
				null
			);
		}
		catch (Exception exception)
		{
			if (nativeBuildStaged)
			{
				try
				{
					await document.AbortRunnerBuildAsync(
						request.Runner.Id,
						CancellationToken.None
					).ConfigureAwait(false);
				}
				catch (Exception abortException)
				{
					exception = new AggregateException(exception, abortException);
				}
			}
			return new RunnerRegenerationCompletion(
				request,
				result,
				null,
				evaluationMilliseconds,
				buildMilliseconds,
				tessellationMilliseconds,
				exception
			);
		}
	}

	private async Task<CollectorRegenerationCompletion> ExecuteCollectorRegenerationAsync(
		CollectorRegenerationRequest request,
		CancellationToken cancellationToken
	)
	{
		ApplicationLog.Current?.Info(
			$"Collector regeneration started asynchronously: id={request.System.Id}; "
				+ $"name={request.System.Name}; revision={request.System.GenerationRevision}; "
				+ $"inlets={request.System.Inlets.Count}"
		);
		Stopwatch timing = Stopwatch.StartNew();
		Dictionary<Guid, RunnerEvaluationResult> results = new();
		bool staged = false;
		int rebuiltRunnerCount = 0;
		long evaluationMilliseconds = 0;
		long buildMilliseconds = 0;
		long tessellationMilliseconds = 0;
		try
		{
			if (request.ExactBuild)
			{
				if (!request.ExactState.TryBegin(request.Revision, request.DependencyHash))
				{
					throw new OperationCanceledException(
						"Collector exact build was superseded before it started."
					);
				}
				foreach ((Guid runnerId, CadExactBuildState state) in request.RunnerExactStates)
				{
					CadRunner runner = request.Runners[runnerId];
					if (!state.TryBegin(
						runner.EditRevision,
						request.RunnerDependencyHashes[runnerId]
					))
					{
						throw new OperationCanceledException(
							"A collector member exact build was superseded before it started."
						);
					}
				}
			}
			foreach ((Guid runnerId, RunnerGraphPlan plan) in request.Plans)
			{
				RunnerEvaluationResult result = await document.EvaluateRunnerAsync(
					request.Runners[runnerId],
					plan,
					cancellationToken).ConfigureAwait(false);
				results.Add(runnerId, result);
				if (!result.Success)
				{
					throw new InvalidOperationException(string.Join(
						Environment.NewLine,
						result.Diagnostics.Select(diagnostic => diagnostic.Message)
					));
				}
			}
			evaluationMilliseconds = timing.ElapsedMilliseconds;
			if (IsSuperseded(request))
			{
				throw new OperationCanceledException("Collector regeneration was superseded.");
			}
			cancellationToken.ThrowIfCancellationRequested();
			Dictionary<Guid, RunnerEvaluationResult> previewResults = new(results);
			mainThreadActions.Enqueue(
				() => ApplyCollectorDraftPreview(request, previewResults)
			);
			if (!request.ExactBuild)
			{
				return new CollectorRegenerationCompletion(
					request,
					results,
					null,
					0,
					evaluationMilliseconds,
					0,
					0,
					null
				);
			}
			timing.Restart();
			staged = true;
			await document.BeginCollectorSystemBuildAsync(
				request.System,
				cancellationToken
			).ConfigureAwait(false);
			foreach (CadCollectorInlet inlet in request.System.Inlets)
			{
				Guid runnerId = inlet.Binding.RunnerId;
				RunnerFeature[] currentGeometry = results[runnerId].Chain.Features.ToArray();
				bool reuse;
				lock (collectorRunnerGeometry)
				{
					reuse = collectorRunnerGeometry.TryGetValue(
							(request.System.Id, runnerId),
							out RunnerFeature[] previousGeometry
						)
						&& previousGeometry.SequenceEqual(currentGeometry);
				}
				if (!reuse)
				{
					await document.BuildRunnerAsync(
						request.Runners[runnerId],
						results[runnerId],
						request.System,
						cancellationToken
					).ConfigureAwait(false);
					rebuiltRunnerCount++;
				}
				if (IsSuperseded(request))
				{
					throw new OperationCanceledException(
						"Collector regeneration was superseded.");
				}
				cancellationToken.ThrowIfCancellationRequested();
			}
			await document.BuildCollectorSystemAsync(
				request.System,
				results,
				cancellationToken
			).ConfigureAwait(false);
			buildMilliseconds = timing.ElapsedMilliseconds;
			if (IsSuperseded(request))
			{
				throw new OperationCanceledException("Collector regeneration was superseded.");
			}
			cancellationToken.ThrowIfCancellationRequested();
			timing.Restart();
			CadTessellation preview = (await document.TessellateCollectorSystemAsync(
				request.System.Id,
				InteractiveLinearDeflection,
				InteractiveAngularDeflection,
				cancellationToken
			).ConfigureAwait(false)).Value;
			tessellationMilliseconds = timing.ElapsedMilliseconds;
			if (IsSuperseded(request))
			{
				throw new OperationCanceledException("Collector regeneration was superseded.");
			}
			cancellationToken.ThrowIfCancellationRequested();
			bool published = request.ExactState.TryPublish(
				request.Revision,
				request.DependencyHash,
				() => document.CommitCollectorSystemBuildAsync(
					request.System.Id,
					request.System.GenerationRevision,
					CancellationToken.None
				).GetAwaiter().GetResult()
			);
			if (!published)
			{
				throw new OperationCanceledException(
					"Collector exact publication was superseded before commit."
				);
			}
			staged = false;
			lock (collectorRunnerGeometry)
			{
				foreach ((Guid runnerId, RunnerEvaluationResult result) in results)
				{
					collectorRunnerGeometry[(request.System.Id, runnerId)] =
						result.Chain.Features.ToArray();
				}
			}
			return new CollectorRegenerationCompletion(
				request,
				results,
				preview,
				rebuiltRunnerCount,
				evaluationMilliseconds,
				buildMilliseconds,
				tessellationMilliseconds,
				null
			);
		}
		catch (Exception exception)
		{
			if (staged)
			{
				try
				{
					await document.AbortCollectorSystemBuildAsync(
						request.System.Id,
						request.System.GenerationRevision
					).ConfigureAwait(false);
				}
				catch (Exception abortException)
				{
					exception = new AggregateException(exception, abortException);
				}
			}
			return new CollectorRegenerationCompletion(
				request,
				results,
				null,
				rebuiltRunnerCount,
				evaluationMilliseconds,
				buildMilliseconds,
				tessellationMilliseconds,
				exception
			);
		}
	}

	private bool IsSuperseded(RegenerationRequest request)
	{
		lock (regenerationLock)
		{
			return pendingRegenerations.TryGetValue(
					request.OwnerId,
					out RegenerationRequest pending
				)
				&& (pending.Revision != request.Revision
					|| pending.ProjectEpoch != request.ProjectEpoch
					|| pending.ExactBuild != request.ExactBuild
					|| !string.Equals(
						pending.DependencyHash,
						request.DependencyHash,
						StringComparison.OrdinalIgnoreCase
					));
		}
	}
}
