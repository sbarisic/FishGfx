using System.Diagnostics;
using FishGfx.Cad;

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
			CollectorLayoutPreset.Row,
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
		Guid[] members = system.Inlets.Select(inlet => inlet.Binding.RunnerId).ToArray();
		if (!project.TryDeleteCollectorSystem(system.Id, evaluations, out string error))
		{
			ui.SetStatus(error, true);
			return;
		}
		document.RemoveCollectorSystemAsync(system.Id).GetAwaiter().GetResult();
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
		if (!transaction.TryApplyPreset(system.Id, preset, out string error)
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
					system.OutletFrame = frame;
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

	private void PreviewGizmoTranslation(CadPoint3 translation)
	{
		if (project.ActiveCollectorSystem == null)
		{
			CadPoint3 euler = selectedPart != null
				&& eulerByPart.TryGetValue(selectedPart.Id, out CadPoint3 value)
				? value
				: default;
			TransformPart(translation, euler);
			return;
		}
		CollectorDraftState draft = EnsureCollectorDraft();
		draft.Frame = new CadFrame(translation, draft.Frame.Tangent, draft.Frame.Normal);
		UpdateCollectorDraftPreview(draft);
	}

	private void PreviewGizmoRotation(CadPoint3 euler)
	{
		if (project.ActiveCollectorSystem == null)
		{
			TransformPart(selectedPart?.Transform.Translation ?? default, euler);
			return;
		}
		CollectorDraftState draft = EnsureCollectorDraft();
		CadQuaternion rotation = CadQuaternion.FromEulerDegrees(euler);
		draft.EulerDegrees = euler;
		draft.Frame = new CadFrame(
			draft.Frame.Origin,
			rotation.Rotate(new CadPoint3(1, 0, 0)),
			rotation.Rotate(new CadPoint3(0, 1, 0))
		);
		UpdateCollectorDraftPreview(draft);
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
		collectorDraft = new CollectorDraftState(system.Id, inletId, frame, frame.ToEulerDegrees());
		viewport.MarkRunnerStale(system.Id);
		return collectorDraft;
	}

	private void UpdateCollectorDraftPreview(CollectorDraftState draft)
	{
		viewport.SetSelectedFrame(draft.Frame, draft.EulerDegrees);
		CadCollectorSystem system = project.CollectorSystems.Single(
			item => item.Id == draft.SystemId);
		viewport.SetCollectorDraft(system, draft.InletId, draft.Frame);
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
					system.OutletFrame = draft.Frame;
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
		viewport.MarkRunnerCurrent(systemId);
		RefreshUi();
		return true;
	}

	private void DiscardCollectorDraft()
	{
		if (collectorDraft != null)
		{
			viewport.MarkRunnerCurrent(collectorDraft.SystemId);
			collectorDraft = null;
		}
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
							system.OutletProfile = new PipeProfile(
								value,
								system.OutletProfile.WallThicknessMillimetres);
							break;
						case 1:
							system.OutletProfile = new PipeProfile(
								system.OutletProfile.OuterDiameterMillimetres,
								value);
							break;
						case 2:
							system.OutletStubLength = value;
							break;
						case 3:
							system.MergeLength = value;
							break;
						case 4:
							system.OverlapLength = value;
							break;
						case 5:
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

	private void RegenerateCollectorSystem(CadCollectorSystem system)
	{
		Stopwatch timing = Stopwatch.StartNew();
		Dictionary<Guid, RunnerEvaluationResult> members = new();
		bool nativeBuildStaged = false;
		long generationRevision = system.GenerationRevision;
		try
		{
			foreach (CadCollectorInlet inlet in system.Inlets)
			{
				CadRunner runner = project.Runners.Single(item => item.Id == inlet.Binding.RunnerId);
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
			document.BeginCollectorSystemBuildAsync(system).GetAwaiter().GetResult();
			nativeBuildStaged = true;
			foreach (CadCollectorInlet inlet in system.Inlets)
			{
				CadRunner runner = project.Runners.Single(item => item.Id == inlet.Binding.RunnerId);
				document.BuildRunnerAsync(runner, members[runner.Id], system).GetAwaiter().GetResult();
			}
			long revision = document.BuildCollectorSystemAsync(system, members).GetAwaiter().GetResult();
			nativeBuildStaged = false;
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
			foreach (CadCollectorInlet inlet in system.Inlets)
			{
				viewport.RemoveRunner(inlet.Binding.RunnerId);
				runnerBuildErrors.Remove(inlet.Binding.RunnerId);
			}
			viewport.AddOrReplace(null, system.Id, preview.Value, true);
			system.IsResolved = true;
			system.Diagnostic = null;
			ui.SetStatus(
				$"{system.Name} fused {system.Inlets.Count}->1 | eval {evaluatedMilliseconds} ms, "
				+ $"build {buildMilliseconds} ms, mesh {timing.ElapsedMilliseconds} ms"
			);
		}
		catch (Exception exception)
		{
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
				true
			);
			foreach (CadCollectorInlet inlet in system.Inlets)
			{
				runnerBuildErrors[inlet.Binding.RunnerId] = exception.Message;
			}
			ui.SetStatus($"{system.Name}: {exception.Message}", true);
		}
	}
}
