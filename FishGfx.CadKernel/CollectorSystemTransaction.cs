namespace FishGfx.Cad;

public sealed partial class CollectorSystemTransaction
{
	private readonly ManifoldProject project;
	private readonly Dictionary<Guid, RunnerGraph> stagedGraphs;
	private readonly List<CadCollectorSystem> stagedCollectors;
	private bool completed;

	private CollectorSystemTransaction(ManifoldProject project)
	{
		this.project = project ?? throw new ArgumentNullException(nameof(project));
		stagedGraphs = project.Runners.ToDictionary(runner => runner.Id, runner => runner.Graph.DeepClone());
		stagedCollectors = project.CollectorSystems.Select(system => system.DeepClone()).ToList();
	}

	public static CollectorSystemTransaction Begin(ManifoldProject project)
	{
		return new CollectorSystemTransaction(project);
	}

	public IReadOnlyList<CadCollectorSystem> CollectorSystems =>
		stagedCollectors.Select(system => system.DeepClone()).ToArray();

	public bool TryCreate(
		IEnumerable<Guid> runnerIds,
		CollectorLayoutPreset preset,
		string name,
		IReadOnlyDictionary<Guid, RunnerEvaluationResult> authoritativeEvaluations,
		out CadCollectorSystem system,
		out string error
	)
	{
		Dictionary<Guid, RunnerGraph> graphBackup = CloneGraphs();
		List<CadCollectorSystem> collectorBackup = CloneCollectors();
		if (TryCreateCore(
			runnerIds,
			preset,
			name,
			authoritativeEvaluations,
			out system,
			out error
		))
		{
			return true;
		}
		Restore(graphBackup, collectorBackup);
		return false;
	}

	private bool TryCreateCore(
		IEnumerable<Guid> runnerIds,
		CollectorLayoutPreset preset,
		string name,
		IReadOnlyDictionary<Guid, RunnerEvaluationResult> authoritativeEvaluations,
		out CadCollectorSystem system,
		out string error
	)
	{
		EnsureOpen();
		system = null;
		error = null;
		Guid[] ids = runnerIds?.Distinct().ToArray() ?? Array.Empty<Guid>();
		if (ids.Length < 2)
		{
			error = "A collector system requires at least two distinct runners.";
			return false;
		}
		if (ids.Any(id => project.Runners.All(runner => runner.Id != id)))
		{
			error = "Every collector member must be an existing project runner.";
			return false;
		}
		if (ids.Any(id =>
		{
			CadRunner runner = project.Runners.Single(item => item.Id == id);
			CadMate mate = project.Mates.FirstOrDefault(item => item.Id == runner.StartMateId);
			return mate?.IsResolved != true
				|| project.Parts.All(part => part.Id != mate.PartId);
		}))
		{
			error = "Every collector member requires a resolved runner start mate.";
			return false;
		}
		if (ids.Any(IsRunnerBound))
		{
			error = "A runner may belong to only one collector system.";
			return false;
		}

		CadCollectorSystem candidate = new()
		{
			Name = string.IsNullOrWhiteSpace(name) ? $"Collector {stagedCollectors.Count + 1}" : name.Trim(),
		};
		candidate.OutletFrame = SeedOutletFrame(ids, authoritativeEvaluations);
		for (int index = 0; index < ids.Length; index++)
		{
			Guid runnerId = ids[index];
			RunnerGraph graph = stagedGraphs[runnerId];
			if (!TryEnsureTerminalBezier(graph, out Guid nodeId, out error))
			{
				return false;
			}
			CadRunner originalRunner = project.Runners.Single(runner => runner.Id == runnerId);
			CadRunner stagedRunner = new()
			{
				Id = originalRunner.Id,
				Name = originalRunner.Name,
				StartMateId = originalRunner.StartMateId,
				Graph = graph,
			};
			RunnerGraphPlan unconstrained = RunnerGraphPlanner.Plan(
				stagedRunner,
				project.Mates.ToDictionary(item => item.Id),
				project.Parts.ToDictionary(item => item.Id)
			);
			if (!unconstrained.Success)
			{
				error = string.Join("; ", unconstrained.Diagnostics.Select(item => item.Message));
				return false;
			}
			Guid? clockingNodeId = null;
			if (unconstrained.Features.Last().OutputProfile.Kind == RunnerProfileKind.MateProfile
				&& !TryInsertClockingTransition(graph, nodeId, out clockingNodeId, out error))
			{
				return false;
			}
			candidate.Inlets.Add(new CadCollectorInlet
			{
				Name = $"Inlet {index + 1}",
				MergeStation = (index + 1d) / (ids.Length + 1d),
				Binding = new CadCollectorBinding
				{
					RunnerId = runnerId,
					TerminalBezierNodeId = nodeId,
					ClockingTransitionNodeId = clockingNodeId,
				},
			});
		}

		ApplyPreset(candidate, preset);
		SeedTerminalHandles(candidate, authoritativeEvaluations);
		if (!ValidateSystem(candidate, stagedGraphs, out error))
		{
			return false;
		}
		candidate.CommitEdit();
		stagedCollectors.Add(candidate);
		system = candidate;
		return true;
	}

	public bool TryApplyPreset(Guid systemId, CollectorLayoutPreset preset, out string error)
	{
		EnsureOpen();
		CadCollectorSystem system = stagedCollectors.SingleOrDefault(item => item.Id == systemId);
		if (system == null)
		{
			error = "The collector system does not exist.";
			return false;
		}
		CadCollectorSystem backup = system.DeepClone();
		ApplyPreset(system, preset);
		if (!ValidateSystem(system, stagedGraphs, out error))
		{
			ReplaceCollector(systemId, backup);
			return false;
		}
		system.CommitEdit();
		return true;
	}

	public bool TryRename(Guid systemId, string name, out string error)
	{
		EnsureOpen();
		CadCollectorSystem system = stagedCollectors.SingleOrDefault(item => item.Id == systemId);
		if (system == null)
		{
			error = "The collector system does not exist.";
			return false;
		}
		if (string.IsNullOrWhiteSpace(name))
		{
			error = "A collector name cannot be empty.";
			return false;
		}
		string previousName = system.Name;
		system.Name = name.Trim();
		if (!ValidateSystem(system, stagedGraphs, out error))
		{
			system.Name = previousName;
			return false;
		}
		return true;
	}

	public bool TryDelete(
		Guid systemId,
		IReadOnlyDictionary<Guid, RunnerEvaluationResult> authoritativeEvaluations,
		out string error
	)
	{
		EnsureOpen();
		ArgumentNullException.ThrowIfNull(authoritativeEvaluations);
		Dictionary<Guid, RunnerGraph> graphBackup = CloneGraphs();
		List<CadCollectorSystem> collectorBackup = CloneCollectors();
		error = null;
		CadCollectorSystem system = stagedCollectors.SingleOrDefault(item => item.Id == systemId);
		if (system == null)
		{
			error = "The collector system does not exist.";
			return false;
		}
		foreach (CadCollectorInlet inlet in system.Inlets)
		{
			if (!BakeTerminalConstraint(
				system,
				inlet,
				authoritativeEvaluations,
				out error
			))
			{
				Restore(graphBackup, collectorBackup);
				return false;
			}
		}
		stagedCollectors.Remove(system);
		return true;
	}

	public bool TryUpdate(
		Guid systemId,
		Action<CadCollectorSystem> update,
		out string error
	)
	{
		EnsureOpen();
		ArgumentNullException.ThrowIfNull(update);
		CadCollectorSystem system = stagedCollectors.SingleOrDefault(item => item.Id == systemId);
		if (system == null)
		{
			error = "The collector system does not exist.";
			return false;
		}
		CadCollectorSystem backup = system.DeepClone();
		try
		{
			update(system);
		}
		catch (Exception exception)
		{
			ReplaceCollector(systemId, backup);
			error = exception.Message;
			return false;
		}
		if (system.Id != systemId)
		{
			ReplaceCollector(systemId, backup);
			error = "A collector transaction cannot change a system's stable ID.";
			return false;
		}
		if (system.GenerationRevision != backup.GenerationRevision)
		{
			ReplaceCollector(systemId, backup);
			error = "Collector parameter updates cannot change the generation revision directly.";
			return false;
		}
		if (!HasSameBindingStructure(backup, system))
		{
			ReplaceCollector(systemId, backup);
			error = "Collector parameter updates cannot change stable inlet IDs or runner bindings.";
			return false;
		}
		if (!ValidateSystem(system, stagedGraphs, out error))
		{
			ReplaceCollector(systemId, backup);
			return false;
		}
		system.CommitEdit();
		return true;
	}

	public bool TryValidate(out string error)
	{
		EnsureOpen();
		if (stagedCollectors.Any(system => system == null)
			|| stagedCollectors.Select(system => system.Id).Distinct().Count()
				!= stagedCollectors.Count)
		{
			error = "Collector system IDs must be unique.";
			return false;
		}
		Guid[] boundRunnerIds = stagedCollectors
			.Where(system => system?.Inlets != null)
			.SelectMany(system => system.Inlets)
			.Where(inlet => inlet?.Binding != null)
			.Select(inlet => inlet.Binding.RunnerId)
			.ToArray();
		if (boundRunnerIds.Distinct().Count() != boundRunnerIds.Length)
		{
			error = "A runner may belong to only one collector system.";
			return false;
		}
		foreach (RunnerGraph graph in stagedGraphs.Values)
		{
			if (!graph.TryValidate(out error))
			{
				return false;
			}
		}
		foreach (CadCollectorSystem system in stagedCollectors)
		{
			if (!ValidateSystem(system, stagedGraphs, out error))
			{
				return false;
			}
		}
		error = null;
		return true;
	}

	private static bool HasSameBindingStructure(
		CadCollectorSystem before,
		CadCollectorSystem after
	)
	{
		if (before.Inlets == null
			|| after.Inlets == null
			|| before.Inlets.Count != after.Inlets.Count)
		{
			return false;
		}
		Dictionary<Guid, CadCollectorBinding> previous = before.Inlets
			.Where(inlet => inlet?.Binding != null)
			.ToDictionary(inlet => inlet.Id, inlet => inlet.Binding);
		if (previous.Count != before.Inlets.Count)
		{
			return false;
		}
		foreach (CadCollectorInlet inlet in after.Inlets)
		{
			if (inlet?.Binding == null
				|| !previous.TryGetValue(inlet.Id, out CadCollectorBinding binding)
				|| binding.RunnerId != inlet.Binding.RunnerId
				|| binding.TerminalBezierNodeId != inlet.Binding.TerminalBezierNodeId
				|| binding.ClockingTransitionNodeId != inlet.Binding.ClockingTransitionNodeId)
			{
				return false;
			}
		}
		return true;
	}

	public bool Commit(out string error)
	{
		EnsureOpen();
		if (!TryValidate(out error))
		{
			return false;
		}
		project.CommitCollectorTransaction(stagedGraphs, stagedCollectors);
		completed = true;
		return true;
	}

	private bool BakeTerminalConstraint(
		CadCollectorSystem system,
		CadCollectorInlet inlet,
		IReadOnlyDictionary<Guid, RunnerEvaluationResult> authoritativeEvaluations,
		out string error
	)
	{
		error = null;
		CadCollectorBinding binding = inlet.Binding;
		if (binding == null || !stagedGraphs.TryGetValue(binding.RunnerId, out RunnerGraph graph))
		{
			error = $"Collector inlet '{inlet.Name}' has no valid runner binding.";
			return false;
		}
		RunnerNode bezier = graph.Nodes.SingleOrDefault(node => node.Id == binding.TerminalBezierNodeId);
		if (bezier == null || bezier.DefinitionId != RunnerNodes.CubicBezier)
		{
			error = $"Collector inlet '{inlet.Name}' references a missing terminal Bézier.";
			return false;
		}

		if (!authoritativeEvaluations.TryGetValue(
				binding.RunnerId,
				out RunnerEvaluationResult evaluation
			)
			|| !evaluation.Success
			|| evaluation.GenerationStamp.OwnerKind != CadGenerationOwnerKind.CollectorSystem
			|| evaluation.GenerationStamp.OwnerId != system.Id
			|| evaluation.GenerationStamp.Revision != system.GenerationRevision)
		{
			error = $"Collector inlet '{inlet.Name}' has no current authoritative runner evaluation to bake.";
			return false;
		}
		RunnerFeature feature = evaluation.Chain.Features.SingleOrDefault(
			item => item.NodeId == bezier.Id);
		if (feature == null)
		{
			error = $"Collector inlet '{inlet.Name}' has no evaluated terminal Bézier.";
			return false;
		}
		CadPoint3 control2Local = feature.EntryFrame.InverseTransformPoint(feature.Control2);
		CadPoint3 endLocal = feature.EntryFrame.InverseTransformPoint(feature.ExitFrame.Origin);
		double endHandleLength = (feature.ExitFrame.Origin - feature.Control2).Length;
		if (!double.IsFinite(endHandleLength) || endHandleLength <= 0)
		{
			error = $"Collector inlet '{inlet.Name}' produced an invalid terminal handle.";
			return false;
		}
		bezier.Properties["control2T"] = FormattableString.Invariant($"{control2Local.X:R}");
		bezier.Properties["control2U"] = FormattableString.Invariant($"{control2Local.Y:R}");
		bezier.Properties["control2V"] = FormattableString.Invariant($"{control2Local.Z:R}");
		bezier.Properties["endT"] = FormattableString.Invariant($"{endLocal.X:R}");
		bezier.Properties["endU"] = FormattableString.Invariant($"{endLocal.Y:R}");
		bezier.Properties["endV"] = FormattableString.Invariant($"{endLocal.Z:R}");
		bezier.Properties["endHandleLength"] = FormattableString.Invariant($"{endHandleLength:R}");

		if (binding.ClockingTransitionNodeId.HasValue)
		{
			if (!RemoveClockingTransitionWhenSafe(
				graph,
				binding.ClockingTransitionNodeId.Value,
				out error
			))
			{
				return false;
			}
		}
		return true;
	}

	private static bool TryEnsureTerminalBezier(RunnerGraph graph, out Guid nodeId, out string error)
	{
		nodeId = Guid.Empty;
		error = null;
		RunnerNode[] outputs = graph.Nodes.Where(node => node.DefinitionId == RunnerNodes.RunnerOutput).ToArray();
		if (outputs.Length != 1)
		{
			error = "A bound runner requires exactly one Runner Output.";
			return false;
		}
		RunnerConnection terminal = graph.Connections.SingleOrDefault(connection =>
			connection.InputNodeId == outputs[0].Id && connection.InputPort == "runner");
		if (terminal == null)
		{
			error = "Runner Output must have a connected feature chain.";
			return false;
		}
		RunnerNode source = graph.Nodes.Single(node => node.Id == terminal.OutputNodeId);
		if (source.DefinitionId == RunnerNodes.CubicBezier)
		{
			nodeId = source.Id;
			return true;
		}
		if (!graph.TrySpliceConnection(
			terminal.Id,
			RunnerNodes.CubicBezier,
			(source.X + outputs[0].X) * 0.5,
			(source.Y + outputs[0].Y) * 0.5,
			out RunnerNode inserted,
			out error
		))
		{
			return false;
		}
		nodeId = inserted.Id;
		return true;
	}

	private static bool TryInsertClockingTransition(
		RunnerGraph graph,
		Guid bezierNodeId,
		out Guid? nodeId,
		out string error
	)
	{
		nodeId = null;
		RunnerConnection connection = graph.Connections.SingleOrDefault(item =>
			item.OutputNodeId == bezierNodeId
				&& graph.Nodes.Single(node => node.Id == item.InputNodeId).DefinitionId
					== RunnerNodes.RunnerOutput);
		if (connection == null)
		{
			error = "The terminal Bézier is not directly connected to Runner Output.";
			return false;
		}
		RunnerNode bezier = graph.Nodes.Single(item => item.Id == bezierNodeId);
		RunnerNode output = graph.Nodes.Single(item => item.Id == connection.InputNodeId);
		if (!graph.TrySpliceConnection(
			connection.Id,
			RunnerNodes.ClockingTransition,
			(bezier.X + output.X) * 0.5,
			(bezier.Y + output.Y) * 0.5,
			out RunnerNode inserted,
			out error
		))
		{
			return false;
		}
		nodeId = inserted.Id;
		return true;
	}

	private static bool RemoveClockingTransitionWhenSafe(
		RunnerGraph graph,
		Guid nodeId,
		out string error
	)
	{
		RunnerNode node = graph.Nodes.SingleOrDefault(item => item.Id == nodeId);
		if (node == null || node.DefinitionId != RunnerNodes.ClockingTransition)
		{
			error = "The bound clocking transition is missing.";
			return false;
		}
		RunnerConnection[] inputs = graph.Connections
			.Where(item => item.InputNodeId == nodeId)
			.ToArray();
		RunnerConnection[] outputs = graph.Connections
			.Where(item => item.OutputNodeId == nodeId)
			.ToArray();
		if (inputs.Length != 1 || outputs.Length != 1)
		{
			error = "The bound clocking transition cannot be safely detached because it has branches.";
			return false;
		}
		if (!graph.TryConnect(
			inputs[0].OutputNodeId,
			inputs[0].OutputPort,
			outputs[0].InputNodeId,
			outputs[0].InputPort,
			out _,
			out error
		))
		{
			return false;
		}
		graph.RemoveNode(nodeId);
		error = null;
		return true;
	}

	private void ReplaceCollector(Guid systemId, CadCollectorSystem replacement)
	{
		int index = stagedCollectors.FindIndex(item => item.Id == systemId);
		stagedCollectors[index] = replacement;
	}

	private Dictionary<Guid, RunnerGraph> CloneGraphs()
	{
		return stagedGraphs.ToDictionary(item => item.Key, item => item.Value.DeepClone());
	}

	private List<CadCollectorSystem> CloneCollectors()
	{
		return stagedCollectors.Select(system => system.DeepClone()).ToList();
	}

	private void Restore(
		IReadOnlyDictionary<Guid, RunnerGraph> graphs,
		IEnumerable<CadCollectorSystem> collectors
	)
	{
		stagedGraphs.Clear();
		foreach (KeyValuePair<Guid, RunnerGraph> item in graphs)
		{
			stagedGraphs[item.Key] = item.Value;
		}
		stagedCollectors.Clear();
		stagedCollectors.AddRange(collectors);
	}

	private void EnsureOpen()
	{
		if (completed)
		{
			throw new InvalidOperationException("The collector-system transaction has already committed.");
		}
	}
}
