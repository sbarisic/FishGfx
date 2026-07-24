using System.Diagnostics;
using System.Globalization;
using FishGfx.Cad;

namespace FishGfx.ManifoldCad;

internal sealed partial class ManifoldCadApplication
{
	private void ImportStep(string path)
	{
		TryOperation(() =>
		{
			CadPart pending = new()
			{
				Name = Path.GetFileNameWithoutExtension(path),
				SourcePath = Path.GetFullPath(path),
			};
			document.ImportStepAsync(pending, path).GetAwaiter().GetResult();
			CadPart part = project.AddPart(pending.Name, pending.SourcePath, pending.Id);
			selectedPart = part;
			eulerByPart[part.Id] = default;
			UploadPart(part);
			viewport.Fit();
			RefreshUi();
			ui.SetStatus($"Imported {part.Name}");
		});
	}

	private void ReplaceStep(string path)
	{
		if (selectedPart == null)
		{
			ui.SetStatus("Select a part before replacing it.", true);
			return;
		}

		TryOperation(() =>
		{
			string replacementPath = Path.GetFullPath(path);
			CadPart replacement = new()
			{
				Id = selectedPart.Id,
				Name = selectedPart.Name,
				SourcePath = replacementPath,
				Transform = selectedPart.Transform,
			};
			document.ReplaceStepAsync(replacement, path).GetAwaiter().GetResult();
			project.ReplacePart(selectedPart.Id, replacementPath);
			UploadPart(selectedPart);
			RegenerateRunnersForPart(selectedPart.Id);
			RefreshUi();
			ui.SetStatus("Part replaced; attached mates require explicit rebinding.", true);
		});
	}

	private void TransformPart(CadPoint3 translation, CadPoint3 euler)
	{
		if (selectedPart == null)
		{
			return;
		}

		TryOperation(() =>
		{
			CadTransform transform = new(translation, CadQuaternion.FromEulerDegrees(euler));
			CadPart transformed = new()
			{
				Id = selectedPart.Id,
				Name = selectedPart.Name,
				SourcePath = selectedPart.SourcePath,
				Transform = transform,
			};
			document.SetPartTransformAsync(transformed).GetAwaiter().GetResult();
			selectedPart.Transform = transform;
			eulerByPart[selectedPart.Id] = euler;
			UploadPart(selectedPart);
			RegenerateRunnersForPart(selectedPart.Id);
			ui.SetStatus("Placement updated; attached runner regenerated.");
		});
	}

	private void CreateOrRebindMate()
	{
		if (!hasSelectedTopology || selectedPart == null)
		{
			ui.SetStatus("Select a cyan mate candidate or supported topology first.", true);
			return;
		}

		if (selectedTopology.Topology.Kind is not CadTopologyKind.CircularEdge
			and not CadTopologyKind.CylindricalFace
			and not CadTopologyKind.ClosedProfile)
		{
			ui.SetStatus("Mates require a circular edge, cylindrical face, or detected closed profile.", true);
			return;
		}

		TryOperation(() =>
		{
			CadRevisioned<MateFrameResult> result = document.GetMateFrameAsync(
				selectedTopology.Topology,
				viewport.Selection.HitPoint
			).GetAwaiter().GetResult();
			CadMate target = selectedMate?.PartId == selectedPart.Id ? selectedMate : null;
			target ??= project.Mates.FirstOrDefault(mate => mate.PartId == selectedPart.Id && !mate.IsResolved);
			CadMate pending = new()
			{
				Id = target?.Id ?? Guid.NewGuid(),
				PartId = selectedPart.Id,
				Name = target?.Name ?? $"Mate {project.Mates.Count + 1}",
			};
			pending.Rebind(selectedTopology.Topology, result.Value.Frame, result.Value.RadiusMillimetres);
			document.BindMateSelectorAsync(pending).GetAwaiter().GetResult();

			target ??= project.AddMate(pending.PartId, pending.Name, pending.Id);
			target.Rebind(pending.Topology.Value, pending.LocalFrame.Value, pending.RadiusMillimetres);
			selectedMate = target;

			RegenerateRunnersForMate(target.Id);
			RefreshUi();
			ui.SetStatus($"Mate '{selectedMate.Name}' bound to exact topology.");
		});
	}

	private void ChangeNodeParameter(int index, double value)
	{
		RunnerNode node = nodeCanvas.SelectedNode;

		if (node == null)
		{
			return;
		}

		string[] properties = node.DefinitionId == RunnerNodes.CubicBezier
			&& bezierInspectorProperties.Length > 0
				? bezierInspectorProperties
				: nodeCanvas.EditableProperties();

		if (index >= properties.Length)
		{
			return;
		}

		node.Properties[properties[index]] = value.ToString("G17", CultureInfo.InvariantCulture);
		if (ActiveRunner != null)
		{
			viewport.ReloadBezierCommittedProperties(ActiveRunner.Id, node);
			CommitRunnerOrSystemEdit(ActiveRunner);
			RegenerateRunner(ActiveRunner);
		}
	}

	private void FlipMate()
	{
		if (selectedMate?.IsResolved != true)
		{
			ui.SetStatus("Select a resolved mate before flipping its axis.", true);
			return;
		}

		selectedMate.Flip();
		RegenerateRunnersForMate(selectedMate.Id);
		RefreshUi();
	}

	private void RenameMate(string name)
	{
		if (selectedMate == null || string.IsNullOrWhiteSpace(name))
		{
			return;
		}

		selectedMate.Name = name.Trim();
		RefreshUi();
	}

	private void AddRunner()
	{
		if (selectedMate?.IsResolved != true)
		{
			ui.SetStatus("Select a resolved mate before adding a runner.", true);
			return;
		}

		CadRunner existing = project.Runners.FirstOrDefault(runner => runner.StartMateId == selectedMate.Id);
		if (existing != null)
		{
			SelectRunner(existing.Id);
			ui.SetStatus($"Mate '{selectedMate.Name}' already owns {existing.Name}.", true);
			return;
		}

		TryOperation(() =>
		{
			CadRunner runner = project.AddRunner(selectedMate.Id);
			nodeCanvas.ClearSelection();
			viewport.SetActiveRunner(runner.Id);
			RegenerateRunner(runner);
			RefreshUi();
		});
	}

	private void DeleteActiveRunner()
	{
		CadRunner runner = ActiveRunner;
		if (runner == null)
			return;
		if (project.CollectorSystems.Any(system =>
			system.Inlets.Any(inlet => inlet.Binding?.RunnerId == runner.Id)))
		{
			ui.SetStatus("Delete or detach the runner's collector system first.", true);
			return;
		}
		TryOperation(() =>
		{
			document.RemoveRunnerAsync(runner.Id).GetAwaiter().GetResult();
			project.RemoveRunner(runner.Id);
			evaluations.Remove(runner.Id);
			runnerBuildErrors.Remove(runner.Id);
			viewport.RemoveRunner(runner.Id);
			evaluation = ActiveRunner != null
				? project.EvaluateRunnerAsync(document, ActiveRunner).GetAwaiter().GetResult()
				: null;
			nodeCanvas.ClearSelection();
			viewport.SetActiveRunner(ActiveRunner?.Id);
			RefreshUi();
		});
	}

	private void RenameRunner(string name)
	{
		if (ActiveRunner == null || string.IsNullOrWhiteSpace(name))
			return;
		string normalized = name.Trim();
		if (string.Equals(ActiveRunner.Name, normalized, StringComparison.Ordinal))
			return;
		TryOperation(() =>
		{
			CadRunner pending = new() { Id = ActiveRunner.Id, Name = normalized };
			document.RenameRunnerAsync(pending).GetAwaiter().GetResult();
			ActiveRunner.Name = normalized;
			RefreshUi();
		});
	}

	private void SelectPart(Guid partId)
	{
		DiscardCollectorDraft();
		project.SetActiveCollector(null);
		selectedPart = project.Parts.FirstOrDefault(part => part.Id == partId);
		RefreshUi();
	}

	private void SelectMate(Guid mateId)
	{
		DiscardCollectorDraft();
		project.SetActiveCollector(null);
		selectedMate = project.Mates.FirstOrDefault(mate => mate.Id == mateId);
		selectedPart = selectedMate == null ? selectedPart : project.Parts.FirstOrDefault(part => part.Id == selectedMate.PartId);
		RefreshUi();
	}

	private void SelectRunner(Guid runnerId)
	{
		DiscardCollectorDraft();
		if (!project.SetActiveRunner(runnerId))
			return;
		CadCollectorSystem system = project.CollectorSystems.FirstOrDefault(candidate =>
			candidate.Inlets.Any(inlet => inlet.Binding?.RunnerId == runnerId));
		CadCollectorInlet inlet = system?.Inlets.FirstOrDefault(
			item => item.Binding?.RunnerId == runnerId);
		project.SetActiveCollector(system?.Id, inlet?.Id);
		evaluation = evaluations.TryGetValue(runnerId, out RunnerEvaluationResult value)
			? value : project.EvaluateRunnerAsync(document, ActiveRunner).GetAwaiter().GetResult();
		nodeCanvas.ClearSelection();
		viewport.SetActiveRunner(runnerId);
		RefreshUi();
	}

	private void RegenerateRunner(CadRunner runner)
	{
		CadCollectorSystem collector = project.CollectorSystems.FirstOrDefault(system =>
			system.Inlets.Any(inlet => inlet.Binding?.RunnerId == runner.Id));
		if (collector != null)
		{
			RegenerateCollectorSystem(collector);
			return;
		}

		Stopwatch timing = Stopwatch.StartNew();
		RunnerEvaluationResult result = project.EvaluateRunnerAsync(document, runner).GetAwaiter().GetResult();
		long evaluationMilliseconds = timing.ElapsedMilliseconds;
		evaluations[runner.Id] = result;
		if (runner == ActiveRunner)
			evaluation = result;

		if (!result.Success)
		{
			runnerBuildErrors[runner.Id] = string.Join(Environment.NewLine,
				result.Diagnostics.Select(item => item.Message));
			viewport.MarkRunnerStale(runner.Id);
			viewport.SetBezierInvalid(
				runner.Id,
				result.Diagnostics.FirstOrDefault(item => item.NodeId.HasValue)?.NodeId
			);
			ui.SetStatus(runnerBuildErrors[runner.Id], true);
			return;
		}

		try
		{
			timing.Restart();
			long revision = document.BuildRunnerAsync(runner, result).GetAwaiter().GetResult();
			long buildMilliseconds = timing.ElapsedMilliseconds;
			timing.Restart();
			CadRevisioned<CadTessellation> preview = document.TessellateRunnerAsync(
				runner.Id,
				InteractiveLinearDeflection,
				InteractiveAngularDeflection
			).GetAwaiter().GetResult();
			long tessellationMilliseconds = timing.ElapsedMilliseconds;

			if (preview.Revision != revision || revision != document.Revision)
			{
				throw new InvalidOperationException("Runner regeneration was superseded by a newer document revision.");
			}

			timing.Restart();
			viewport.AddOrReplace(null, runner.Id, preview.Value, true);
			if (runner == ActiveRunner)
			{
				UpdateBezierEditor(nodeCanvas.SelectedNode);
			}
			long uploadMilliseconds = timing.ElapsedMilliseconds;
			runnerBuildErrors.Remove(runner.Id);
			Console.WriteLine(
				$"[Manifold CAD] Regenerated {runner.Name}: eval={evaluationMilliseconds} ms, "
				+ $"build={buildMilliseconds} ms, mesh={tessellationMilliseconds} ms, "
				+ $"upload={uploadMilliseconds} ms"
			);
			string lengthDescription = result.LengthIsToleranceControlled
				? "tolerance-controlled kernel length"
				: "exact line/arc length";
			ui.SetStatus($"{runner.Name} {result.LengthMillimetres:F2} mm | {lengthDescription} | exact solid valid | "
				+ $"eval {evaluationMilliseconds} ms, build {buildMilliseconds} ms, "
				+ $"mesh {tessellationMilliseconds} ms, upload {uploadMilliseconds} ms");
		}
		catch (Exception exception)
		{
			runnerBuildErrors[runner.Id] = exception.Message;
			viewport.MarkRunnerStale(runner.Id);
			ui.SetStatus($"{runner.Name}: {exception.Message}", true);
		}
	}

	private void CommitRunnerOrSystemEdit(CadRunner runner)
	{
		CadCollectorSystem system = project.CollectorSystems.FirstOrDefault(candidate =>
			candidate.Inlets.Any(inlet => inlet.Binding?.RunnerId == runner.Id));
		if (system == null)
		{
			runner.CommitEdit();
		}
		else
		{
			system.CommitEdit();
		}
	}

	private void RegenerateRunnersForPart(Guid partId)
	{
		HashSet<Guid> mateIds = project.Mates.Where(mate => mate.PartId == partId).Select(mate => mate.Id).ToHashSet();
		HashSet<Guid> committedSystems = new();
		foreach (CadRunner runner in project.Runners.Where(item => mateIds.Contains(item.StartMateId)))
		{
			CadCollectorSystem system = project.CollectorSystems.FirstOrDefault(candidate =>
				candidate.Inlets.Any(inlet => inlet.Binding?.RunnerId == runner.Id));
			if (system != null && committedSystems.Add(system.Id))
			{
				system.CommitEdit();
				RegenerateCollectorSystem(system);
			}
			else if (system == null)
			{
				RegenerateRunner(runner);
			}
		}
	}

	private void RegenerateRunnersForMate(Guid mateId)
	{
		HashSet<Guid> committedSystems = new();
		foreach (CadRunner runner in project.Runners.Where(item => item.StartMateId == mateId))
		{
			CadCollectorSystem system = project.CollectorSystems.FirstOrDefault(candidate =>
				candidate.Inlets.Any(inlet => inlet.Binding?.RunnerId == runner.Id));
			if (system != null && committedSystems.Add(system.Id))
			{
				system.CommitEdit();
				RegenerateCollectorSystem(system);
			}
			else if (system == null)
			{
				RegenerateRunner(runner);
			}
		}
	}

	private void RegenerateAllRunners()
	{
		HashSet<Guid> regeneratedCollectors = new();
		foreach (CadRunner runner in project.Runners)
		{
			CadCollectorSystem system = project.CollectorSystems.FirstOrDefault(candidate =>
				candidate.Inlets.Any(inlet => inlet.Binding?.RunnerId == runner.Id));
			if (system == null)
			{
				RegenerateRunner(runner);
			}
			else if (regeneratedCollectors.Add(system.Id))
			{
				RegenerateCollectorSystem(system);
			}
		}
	}

	private void UploadPart(CadPart part)
	{
		CadRevisioned<CadTessellation> preview = document.TessellatePartAsync(part.Id).GetAwaiter().GetResult();

		if (preview.Revision == document.Revision)
		{
			viewport.AddOrReplace(part.Id, null, preview.Value, false);
		}

		CadRevisioned<IReadOnlyList<NativeTopologyDescriptor>> topology = document.GetTopologyAsync(part.Id)
			.GetAwaiter()
			.GetResult();

		if (topology.Revision == document.Revision)
		{
			viewport.SetMateCandidates(part, topology.Value);
			viewport.SetMates(project);
		}
	}

	private void SelectViewportItem(CadViewportSelection selection)
	{
		CadGeometrySourceRef? source = selection.Sources?
			.OrderBy(item => item.SourceKind switch
			{
				CadGeometrySourceKind.RunnerNode => 0,
				CadGeometrySourceKind.CollectorInlet => 1,
				CadGeometrySourceKind.CollectorOutlet => 2,
				_ => 3,
			})
			.Cast<CadGeometrySourceRef?>()
			.FirstOrDefault();
		if (source.HasValue && source.Value.SourceKind == CadGeometrySourceKind.RunnerNode)
		{
			SelectRunner(source.Value.OwnerId);
			if (source.Value.ElementGuid.HasValue)
			{
				nodeCanvas.SelectBySource(source.Value.ElementGuid, ActiveGraph);
			}
		}
		else if (source.HasValue && source.Value.SourceKind == CadGeometrySourceKind.CollectorInlet
			&& source.Value.ElementGuid.HasValue)
		{
			SelectCollectorInlet(source.Value.ElementGuid.Value);
		}
		else if (source.HasValue && source.Value.OwnerId != Guid.Empty)
		{
			SelectCollector(source.Value.OwnerId);
		}
		else if (selection.RunnerId.HasValue
			&& project.Runners.Any(runner => runner.Id == selection.RunnerId.Value))
		{
			SelectRunner(selection.RunnerId.Value);
		}

		if (selection.MateId.HasValue)
		{
			selectedMate = project.Mates.FirstOrDefault(mate => mate.Id == selection.MateId.Value);
		}

		if (selection.SourceNodeId.HasValue && ActiveRunner != null)
		{
			nodeCanvas.SelectBySource(selection.SourceNodeId, ActiveGraph);
		}

		if (!selection.PartId.HasValue)
		{
			return;
		}

		selectedPart = project.Parts.FirstOrDefault(part => part.Id == selection.PartId.Value);
		selectedMate = project.Mates.FirstOrDefault(mate => mate.PartId == selection.PartId.Value
			&& mate.Topology?.TopologyId == selection.TopologyId);
		CadRevisioned<IReadOnlyList<NativeTopologyDescriptor>> topology = document.GetTopologyAsync(
			selection.PartId.Value
		).GetAwaiter().GetResult();
		selectedTopology = topology.Value.FirstOrDefault(item => item.Topology.TopologyId == selection.TopologyId);
		hasSelectedTopology = selectedTopology != null;
		RefreshUi();

		if (hasSelectedTopology && selection.IsMateCandidate)
		{
			CreateOrRebindMate();
			return;
		}

		if (hasSelectedTopology)
		{
			bool mateEligible = selectedTopology.Topology.Kind is CadTopologyKind.CircularEdge
				or CadTopologyKind.CylindricalFace
				or CadTopologyKind.ClosedProfile;
			ui.SetStatus(
				mateEligible
					? $"Selected {selectedTopology.Topology.Kind}; click Create / Rebind Mate."
					: $"Selected {selectedTopology.Topology.Kind}; choose a cyan candidate sphere or supported topology."
			);
		}
	}

}
