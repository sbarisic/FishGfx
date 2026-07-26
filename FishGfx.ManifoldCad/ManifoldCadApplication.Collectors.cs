using System.Diagnostics;
using FishGfx.Cad;
using FishGfx.Im3d;

namespace FishGfx.ManifoldCad;

internal sealed partial class ManifoldCadApplication
{
	private void AddCollector(IReadOnlyList<Guid> eligible)
	{
		if (eligible == null || eligible.Count < 2)
		{
			ui.SetStatus("Add at least two unbound runners before creating a collector.", true);
			return;
		}
		if (!project.TryCreateCollectorSystem(
			eligible,
			eligible.Count >= 3
				? CollectorLayoutPreset.Radial
				: CollectorLayoutPreset.Row,
			null,
			evaluations,
			out CadCollectorSystem system,
			out string error
		))
		{
			ui.SetStatus(error, true);
			return;
		}
		project.SetActiveCollector(system.Id, system.Inlets[0].Id);
		project.SetActiveRunner(system.Inlets[0].Binding.RunnerId);
		viewport.SetActiveRunner(ActiveRunner.Id);
		RegenerateCollectorSystem(system);
		RefreshUi();
	}

	private void DeleteActiveCollector()
	{
		CadCollectorSystem system = project.ActiveCollectorSystem;
		if (system == null)
		{
			return;
		}
		WaitForRegeneration();
		Guid[] members = system.Inlets.Select(inlet => inlet.Binding.RunnerId).ToArray();
		if (!project.TryDeleteCollectorSystem(system.Id, evaluations, out string error))
		{
			ui.SetStatus(error, true);
			return;
		}
		document.RemoveCollectorSystemAsync(system.Id).GetAwaiter().GetResult();
		lock (collectorRunnerGeometry)
		{
			foreach ((Guid systemId, Guid runnerId) in collectorRunnerGeometry.Keys
				.Where(key => key.SystemId == system.Id)
				.ToArray())
			{
				collectorRunnerGeometry.Remove((systemId, runnerId));
			}
		}
		viewport.RemoveRunner(system.Id);
		foreach (Guid runnerId in members)
		{
			CadRunner runner = project.Runners.Single(item => item.Id == runnerId);
			RegenerateRunner(runner);
		}
		RefreshUi();
	}

	private void RenameCollector(string name)
	{
		CadCollectorSystem active = project.ActiveCollectorSystem;
		if (active == null
			|| string.IsNullOrWhiteSpace(name)
			|| string.Equals(active.Name, name.Trim(), StringComparison.Ordinal))
		{
			return;
		}
		WaitForRegeneration();
		CollectorSystemTransaction transaction = CollectorSystemTransaction.Begin(project);
		if (!transaction.TryRename(active.Id, name, out string error)
			|| !transaction.Commit(out error))
		{
			ui.SetStatus(error, true);
			RefreshUi();
			return;
		}
		CadCollectorSystem renamed = project.CollectorSystems.Single(system => system.Id == active.Id);
		try
		{
			document.RenameCollectorSystemAsync(renamed).GetAwaiter().GetResult();
			ui.SetStatus($"Renamed collector to {renamed.Name}.");
		}
		catch (Exception exception)
		{
			ui.SetStatus($"Collector renamed in project, but exact document metadata failed: {exception.Message}", true);
		}
		RefreshUi();
	}

	private void ApplyCollectorPreset(CollectorLayoutPreset preset)
	{
		CadCollectorSystem system = project.ActiveCollectorSystem;
		if (system == null)
		{
			ui.SetStatus("Select a collector system before applying a layout preset.", true);
			return;
		}
		CollectorSystemTransaction transaction = CollectorSystemTransaction.Begin(project);
		if (!transaction.TryApplyPreset(system.Id, preset, evaluations, out string error)
			|| !transaction.Commit(out error))
		{
			ui.SetStatus(error, true);
			return;
		}
		RegenerateCollectorSystem(project.CollectorSystems.Single(item => item.Id == system.Id));
		RefreshUi();
	}

	private void SelectCollector(Guid systemId)
	{
		DiscardCollectorDraft();
		DiscardPartDraft();
		CadCollectorSystem system = project.CollectorSystems.FirstOrDefault(item => item.Id == systemId);
		if (system == null)
		{
			return;
		}
		project.SetActiveCollector(system.Id);
		selectedPart = null;
		selectedMate = null;
		RefreshUi();
	}

	private void SelectCollectorInlet(Guid inletId)
	{
		DiscardCollectorDraft();
		DiscardPartDraft();
		CadCollectorSystem system = project.CollectorSystems.FirstOrDefault(candidate =>
			candidate.Inlets.Any(inlet => inlet.Id == inletId));
		if (system == null)
		{
			return;
		}
		CadCollectorInlet inlet = system.Inlets.Single(item => item.Id == inletId);
		project.SetActiveCollector(system.Id, inlet.Id);
		selectedPart = null;
		selectedMate = null;
		SelectRunner(inlet.Binding.RunnerId);
	}

	private void TransformSelection(CadPoint3 translation, CadPoint3 euler)
	{
		CadCollectorSystem active = project.ActiveCollectorSystem;
		if (active == null)
		{
			TransformPart(translation, euler);
			return;
		}
		CadQuaternion rotation = CadQuaternion.FromEulerDegrees(euler);
		CadFrame frame = new(
			translation,
			rotation.Rotate(new CadPoint3(1, 0, 0)),
			rotation.Rotate(new CadPoint3(0, 1, 0))
		);
		CollectorSystemTransaction transaction = CollectorSystemTransaction.Begin(project);
		Guid? inletId = project.View.ActiveCollectorInletId;
		if (!transaction.TryUpdate(
			active.Id,
			system =>
			{
				if (inletId.HasValue)
				{
					system.Inlets.Single(inlet => inlet.Id == inletId.Value).LocalFrame = frame;
				}
				else
				{
					system.SetOutletFramePreservingWorldInlets(frame);
				}
			},
			out string error
		) || !transaction.Commit(out error))
		{
			ui.SetStatus(error, true);
			return;
		}
		RegenerateCollectorSystem(project.CollectorSystems.Single(system => system.Id == active.Id));
		RefreshUi();
	}

	private void BeginGizmoDraft(Im3dPose _)
	{
		if (project.ActiveCollectorSystem != null)
		{
			UpdateCollectorDraftPreview(EnsureCollectorDraft());
			return;
		}
		if (selectedPart == null)
		{
			return;
		}

		partDraft ??= new PartTransformDraftState(selectedPart);
		viewport.SetPartDraft(selectedPart, partDraft.OriginalTransform, partDraft.Transform);
		viewport.MarkAffectedGeometryStale(project, selectedPart.Id);
		viewport.UpdatePartAttachedPreviews(
			project,
			evaluations,
			selectedPart.Id,
			partDraft.OriginalTransform,
			partDraft.Transform);
	}

	private void PreviewGizmoPose(Im3dPose pose)
	{
		if (project.ActiveCollectorSystem != null)
		{
			CollectorDraftState draft = EnsureCollectorDraft();
			draft.Frame = CadViewport.ToCadFrame(pose);
			UpdateCollectorDraftPreview(draft);
			return;
		}
		if (selectedPart == null)
		{
			return;
		}

		partDraft ??= new PartTransformDraftState(selectedPart);
		partDraft.Transform = new CadTransform(
			CadPoint3.FromVector3(pose.Position),
			CadViewport.ToCadQuaternion(pose.Rotation));
		viewport.SetPartDraft(selectedPart, partDraft.OriginalTransform, partDraft.Transform);
		viewport.UpdatePartAttachedPreviews(project, evaluations, selectedPart.Id, partDraft.OriginalTransform, partDraft.Transform);
		ui.SetPart(
			selectedPart,
			partDraft.Transform.Translation,
			partDraft.Transform.Rotation.ToEulerDegrees());
	}

	private void CommitGizmoDraft(Im3dPose pose)
	{
		PreviewGizmoPose(pose);
		if (project.ActiveCollectorSystem != null)
		{
			CommitCollectorDraft();
			return;
		}

		CommitPartDraft();
	}

	private CollectorDraftState EnsureCollectorDraft()
	{
		CadCollectorSystem system = project.ActiveCollectorSystem
			?? throw new InvalidOperationException("No collector system is selected.");
		Guid? inletId = project.View.ActiveCollectorInletId;
		if (collectorDraft?.SystemId == system.Id && collectorDraft.InletId == inletId)
		{
			return collectorDraft;
		}
		CadCollectorInlet inlet = inletId.HasValue
			? system.Inlets.Single(item => item.Id == inletId.Value)
			: null;
		CadFrame frame = inlet == null ? system.OutletFrame : system.GetWorldInletFrame(inlet);
		collectorDraft = new CollectorDraftState(system.Id, inletId, frame);
		viewport.MarkRunnerStale(system.Id);
		return collectorDraft;
	}

	private void UpdateCollectorDraftPreview(CollectorDraftState draft)
	{
		viewport.SetSelectedFrame(draft.SystemId, draft.InletId, draft.Frame);
		CadCollectorSystem system = project.CollectorSystems.Single(
			item => item.Id == draft.SystemId);
		viewport.SetCollectorDraft(
			system,
			draft.InletId,
			draft.Frame,
			false,
			evaluations
		);
		CadCollectorInlet inlet = draft.InletId.HasValue
			? system.Inlets.Single(item => item.Id == draft.InletId.Value)
			: null;
		CadFrame inspectorFrame = inlet == null
			? draft.Frame
			: draft.Frame.RelativeTo(system.OutletFrame);
		ui.SetCollector(
			system,
			inlet,
			inspectorFrame.Origin,
			inspectorFrame.ToEulerDegrees()
		);
	}

	private void CommitCollectorDraft()
	{
		CollectorDraftState draft = collectorDraft;
		if (draft == null)
		{
			return;
		}
		collectorDraft = null;
		CollectorSystemTransaction transaction = CollectorSystemTransaction.Begin(project);
		if (!transaction.TryUpdate(
			draft.SystemId,
			system =>
			{
				if (draft.InletId.HasValue)
				{
					system.Inlets.Single(inlet => inlet.Id == draft.InletId.Value).LocalFrame =
						draft.Frame.RelativeTo(system.OutletFrame);
				}
				else
				{
					system.SetOutletFramePreservingWorldInlets(draft.Frame);
				}
			},
			out string error
		) || !transaction.Commit(out error))
		{
			ui.SetStatus(error, true);
			RefreshUi();
			return;
		}
		RegenerateCollectorSystem(project.CollectorSystems.Single(system => system.Id == draft.SystemId));
		RefreshUi();
	}

	private bool CancelCollectorDraft()
	{
		if (collectorDraft == null)
		{
			return false;
		}
		Guid systemId = collectorDraft.SystemId;
		collectorDraft = null;
		RestoreCollectorVisualFreshness(systemId);
		RefreshUi();
		return true;
	}

	private bool CancelGizmoDraft()
	{
		bool cancelled = CancelCollectorDraft();
		if (partDraft == null)
		{
			return cancelled;
		}

		Guid partId = partDraft.PartId;
		partDraft = null;
		viewport.RestoreAffectedGeometryFreshness(project, partId);
		viewport.ClearPartDraft();
		RefreshUi();
		return true;
	}

	private void DiscardCollectorDraft()
	{
		if (collectorDraft != null)
		{
			RestoreCollectorVisualFreshness(collectorDraft.SystemId);
			collectorDraft = null;
		}
	}

	private void RestoreCollectorVisualFreshness(Guid systemId)
	{
		CadCollectorSystem system = project.CollectorSystems.FirstOrDefault(item => item.Id == systemId);
		if (system?.ExactBuild.IsCurrent == true)
		{
			viewport.MarkRunnerCurrent(systemId);
		}
		else
		{
			viewport.MarkRunnerStale(systemId);
		}
	}

	private void DiscardPartDraft()
	{
		if (partDraft == null)
		{
			return;
		}

		Guid partId = partDraft.PartId;
		partDraft = null;
		viewport.RestoreAffectedGeometryFreshness(project, partId);
		viewport.ClearPartDraft();
	}

	private void ChangeCollectorParameter(int index, double value)
	{
		CadCollectorSystem active = project.ActiveCollectorSystem;
		if (active == null || !double.IsFinite(value))
		{
			return;
		}
		Guid? inletId = project.View.ActiveCollectorInletId;
		CollectorSystemTransaction transaction = CollectorSystemTransaction.Begin(project);
		if (!transaction.TryUpdate(
			active.Id,
			system =>
			{
				if (inletId.HasValue)
				{
					CadCollectorInlet inlet = system.Inlets.Single(item => item.Id == inletId.Value);
					switch (index)
					{
						case 0:
							inlet.MergeStation = value;
							break;
						case 1:
							inlet.BranchStartHandleLength = value;
							break;
						case 2:
							inlet.ClockingTransitionLength = value;
							break;
					}
				}
				else
				{
						switch (index)
						{
							case 0:
								system.BranchEndHandleLength = value;
								break;
					}
				}
			},
			out string error
		) || !transaction.Commit(out error))
		{
			ui.SetStatus(error, true);
			RefreshUi();
			return;
		}
		RegenerateCollectorSystem(project.CollectorSystems.Single(system => system.Id == active.Id));
		RefreshUi();
	}

	private void RegenerateCollectorSystem(
		CadCollectorSystem system,
		bool exactBuild = false
	)
	{
		if (!autoMode)
		{
			QueueCollectorRegeneration(system, exactBuild);
			return;
		}
		RegenerateCollectorSystemSynchronously(system);
	}

	private void RegenerateCollectorSystemSynchronously(CadCollectorSystem system)
	{
		Stopwatch timing = Stopwatch.StartNew();
		Dictionary<Guid, RunnerEvaluationResult> members = new();
		bool nativeBuildStaged = false;
		long generationRevision = system.GenerationRevision;
		string dependencyHash = CadGeometryDependencyHash.Collector(project, system);
		system.ExactBuild.Request(generationRevision, dependencyHash);
		system.ExactBuild.TryBegin(generationRevision, dependencyHash);
		Dictionary<Guid, string> runnerHashes = new();
		foreach (CadCollectorInlet inlet in system.Inlets)
		{
			CadRunner runner = project.Runners.Single(item => item.Id == inlet.Binding.RunnerId);
			string runnerHash = CadGeometryDependencyHash.Runner(project, runner);
			runnerHashes[runner.Id] = runnerHash;
			runner.ExactBuild.Request(runner.EditRevision, runnerHash);
			runner.ExactBuild.TryBegin(runner.EditRevision, runnerHash);
		}
		ApplicationLog.Current?.Info(
			$"Collector regeneration started: id={system.Id}; name={system.Name}; "
				+ $"revision={generationRevision}; inlets={system.Inlets.Count}; "
				+ $"runners={string.Join(",", system.Inlets.Select(inlet => inlet.Binding?.RunnerId))}"
		);
		try
		{
			foreach (CadCollectorInlet inlet in system.Inlets)
			{
				CadRunner runner = project.Runners.Single(item => item.Id == inlet.Binding.RunnerId);
				RunnerNode terminalNode = runner.Graph.Nodes.Single(
					node => node.Id == inlet.Binding.TerminalBezierNodeId);
				CadFrame constrainedFrame = system.GetWorldInletFrame(inlet);
				string startHandle = terminalNode.Properties.TryGetValue(
					"startHandleLength",
					out string storedStartHandle)
						? storedStartHandle
						: "<missing>";
				string endHandle = terminalNode.Properties.TryGetValue(
					"endHandleLength",
					out string storedEndHandle)
						? storedEndHandle
						: "<missing>";
				ApplicationLog.Current?.Info(
					$"Collector terminal node: runner={runner.Id}; "
						+ $"target=({constrainedFrame.Origin.X:R},"
						+ $"{constrainedFrame.Origin.Y:R},{constrainedFrame.Origin.Z:R}); "
						+ $"startHandle={startHandle}; "
						+ $"endHandle={endHandle}; "
						+ $"cachedEvaluations={evaluations.Count}"
				);
				if (evaluations.TryGetValue(
						runner.Id,
						out RunnerEvaluationResult previousEvaluation
					)
					&& previousEvaluation.Success)
				{
					CadFrame previousEnd = previousEvaluation.Chain.EndFrame;
					CadPoint3 constraintChord =
						constrainedFrame.Origin - previousEnd.Origin;
					ApplicationLog.Current?.Info(
						$"Collector terminal constraint: runner={runner.Id}; "
							+ $"distance={constraintChord.Length:R}; "
							+ $"chordTangentDot={CadPoint3.Dot(
								constraintChord.Normalized(),
								previousEnd.Tangent):R}; "
							+ $"tangentDot={CadPoint3.Dot(previousEnd.Tangent, constrainedFrame.Tangent):R}; "
							+ $"startHandle={startHandle}; "
							+ $"endHandle={endHandle}"
					);
				}
				RunnerEvaluationResult result = project.EvaluateRunnerAsync(document, runner)
					.GetAwaiter()
					.GetResult();
				evaluations[runner.Id] = result;
				members[runner.Id] = result;
				if (runner == ActiveRunner)
				{
					evaluation = result;
				}
				if (!result.Success)
				{
					throw new InvalidOperationException(string.Join(
						Environment.NewLine,
						result.Diagnostics.Select(item => item.Message)
					));
				}
			}

			long evaluatedMilliseconds = timing.ElapsedMilliseconds;
			timing.Restart();
			nativeBuildStaged = true;
			document.BeginCollectorSystemBuildAsync(system).GetAwaiter().GetResult();
			int rebuiltRunnerCount = 0;
			foreach (CadCollectorInlet inlet in system.Inlets)
			{
				CadRunner runner = project.Runners.Single(item => item.Id == inlet.Binding.RunnerId);
				RunnerFeature[] currentGeometry = members[runner.Id].Chain.Features.ToArray();
				bool reuse;
				lock (collectorRunnerGeometry)
				{
					reuse = collectorRunnerGeometry.TryGetValue(
							(system.Id, runner.Id),
							out RunnerFeature[] previousGeometry
						)
						&& previousGeometry.SequenceEqual(currentGeometry);
				}
				if (!reuse)
				{
					document.BuildRunnerAsync(runner, members[runner.Id], system).GetAwaiter().GetResult();
					rebuiltRunnerCount++;
				}
			}
			long revision = document.BuildCollectorSystemAsync(system, members).GetAwaiter().GetResult();
			long buildMilliseconds = timing.ElapsedMilliseconds;
			timing.Restart();
			CadRevisioned<CadTessellation> preview = document.TessellateCollectorSystemAsync(
				system.Id,
				InteractiveLinearDeflection,
				InteractiveAngularDeflection
			).GetAwaiter().GetResult();
			if (preview.Revision != revision || revision != document.Revision)
			{
				throw new InvalidOperationException(
					"Collector regeneration was superseded by a newer document revision.");
			}
			document.CommitCollectorSystemBuildAsync(system.Id, generationRevision)
				.GetAwaiter()
				.GetResult();
			nativeBuildStaged = false;
			foreach (CadCollectorInlet inlet in system.Inlets)
			{
				viewport.RemoveRunner(inlet.Binding.RunnerId);
				runnerBuildErrors.Remove(inlet.Binding.RunnerId);
				lock (collectorRunnerGeometry)
				{
					collectorRunnerGeometry[(system.Id, inlet.Binding.RunnerId)] =
						members[inlet.Binding.RunnerId].Chain.Features.ToArray();
				}
			}
			viewport.AddOrReplace(null, system.Id, preview.Value, true);
			system.ExactBuild.TryPublish(generationRevision, dependencyHash);
			foreach ((Guid runnerId, string runnerHash) in runnerHashes)
			{
				CadRunner runner = project.Runners.Single(item => item.Id == runnerId);
				runner.ExactBuild.TryPublish(runner.EditRevision, runnerHash);
			}
			system.IsResolved = true;
			system.Diagnostic = null;
			ApplicationLog.Current?.Info(
				$"Collector regeneration completed: id={system.Id}; revision={generationRevision}; "
					+ $"rebuiltRunners={rebuiltRunnerCount}/{system.Inlets.Count}; "
					+ $"evalMs={evaluatedMilliseconds}; buildMs={buildMilliseconds}; "
					+ $"meshMs={timing.ElapsedMilliseconds}"
			);
			ui.SetStatus(
				$"{system.Name} fused {system.Inlets.Count}->1 | eval {evaluatedMilliseconds} ms, "
					+ $"runners {rebuiltRunnerCount}/{system.Inlets.Count}, "
					+ $"build {buildMilliseconds} ms, mesh {timing.ElapsedMilliseconds} ms"
			);
		}
		catch (Exception exception)
		{
			system.ExactBuild.Fail(generationRevision, exception.Message);
			foreach ((Guid runnerId, _) in runnerHashes)
			{
				CadRunner runner = project.Runners.Single(item => item.Id == runnerId);
				runner.ExactBuild.Fail(runner.EditRevision, exception.Message);
			}
			ApplicationLog.Current?.Exception(
				$"Collector regeneration failed: id={system.Id}; name={system.Name}; "
					+ $"revision={generationRevision}.",
				exception
			);
			if (nativeBuildStaged)
			{
				try
				{
					document.AbortCollectorSystemBuildAsync(system.Id, generationRevision)
						.GetAwaiter()
						.GetResult();
				}
				catch (Exception abortException)
				{
					exception = new AggregateException(exception, abortException);
				}
			}
			system.IsResolved = false;
			system.Diagnostic = exception.Message;
			viewport.MarkRunnerStale(system.Id);
			viewport.SetCollectorDraft(
				system,
				null,
				system.OutletFrame,
				true,
				evaluations
			);
			foreach (CadCollectorInlet inlet in system.Inlets)
			{
				runnerBuildErrors[inlet.Binding.RunnerId] = exception.Message;
			}
			ui.SetStatus($"{system.Name}: {exception.Message}", true);
		}
	}
}
