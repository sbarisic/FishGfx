namespace FishGfx.Cad;

public sealed class ManifoldViewState
{
	public CadPoint3 CameraTarget { get; set; }

	public double CameraDistance { get; set; } = 500;

	public double YawDegrees { get; set; } = 35;

	public double PitchDegrees { get; set; } = -25;

	public bool Orthographic { get; set; }

	public double GraphPanX { get; set; }

	public double GraphPanY { get; set; }

	public double GraphZoom { get; set; } = 1;

	public Guid? ActiveRunnerId { get; set; }

	public Guid? ActiveCollectorSystemId { get; set; }

	public Guid? ActiveCollectorInletId { get; set; }
}

public sealed class CadRunner
{
	private long editRevision;

	public Guid Id { get; set; } = Guid.NewGuid();

	public string Name { get; set; } = "Runner";

	public Guid StartMateId { get; set; }

	public RunnerGraph Graph { get; set; } = new();

	public long EditRevision => Interlocked.Read(ref editRevision);

	public long CommitEdit() => Interlocked.Increment(ref editRevision);
}

public sealed class ManifoldProject
{
	private readonly List<CadPart> parts = new();
	private readonly List<CadMate> mates = new();
	private readonly List<CadRunner> runners = new();
	private readonly List<CadCollectorSystem> collectorSystems = new();

	public ManifoldProject()
	{
		Parts = parts.AsReadOnly();
		Mates = mates.AsReadOnly();
		Runners = runners.AsReadOnly();
		CollectorSystems = collectorSystems.AsReadOnly();
	}

	public Guid Id { get; set; } = Guid.NewGuid();

	public string Name { get; set; } = "Untitled Manifold";

	public IReadOnlyList<CadPart> Parts { get; }

	public IReadOnlyList<CadMate> Mates { get; }

	public IReadOnlyList<CadRunner> Runners { get; }

	public IReadOnlyList<CadCollectorSystem> CollectorSystems { get; }

	public CadRunner ActiveRunner => View.ActiveRunnerId.HasValue
		? runners.FirstOrDefault(runner => runner.Id == View.ActiveRunnerId.Value)
		: runners.FirstOrDefault();

	public CadCollectorSystem ActiveCollectorSystem => View.ActiveCollectorSystemId.HasValue
		? collectorSystems.FirstOrDefault(system => system.Id == View.ActiveCollectorSystemId.Value)
		: null;

	public ManifoldViewState View { get; set; } = new();

	public CadRunner AddRunner(Guid startMateId, string name = null, Guid? id = null, RunnerGraph graph = null)
	{
		CadMate mate = mates.SingleOrDefault(candidate => candidate.Id == startMateId)
			?? throw new ArgumentException("The runner's start mate does not exist.", nameof(startMateId));

		if (runners.Any(candidate => candidate.StartMateId == startMateId))
		{
			throw new InvalidOperationException($"Mate '{mate.Name}' already owns a runner.");
		}

		CadRunner runner = new()
		{
			Id = id ?? Guid.NewGuid(),
			Name = string.IsNullOrWhiteSpace(name) ? $"Runner - {mate.Name}" : name.Trim(),
			StartMateId = startMateId,
			Graph = graph ?? RunnerGraph.CreateDefault(mate.Topology?.Kind ?? CadTopologyKind.Unknown),
		};

		if (runners.Any(existing => existing.Id == runner.Id))
		{
			throw new ArgumentException($"Runner ID '{runner.Id}' already exists.", nameof(id));
		}

		runners.Add(runner);
		View.ActiveRunnerId = runner.Id;
		return runner;
	}

	public bool RemoveRunner(Guid runnerId)
	{
		if (collectorSystems.SelectMany(system => system.Inlets)
			.Any(inlet => inlet.Binding?.RunnerId == runnerId))
		{
			return false;
		}
		bool removed = runners.RemoveAll(runner => runner.Id == runnerId) > 0;
		if (removed && View.ActiveRunnerId == runnerId)
		{
			View.ActiveRunnerId = runners.FirstOrDefault()?.Id;
		}
		return removed;
	}

	public bool SetActiveRunner(Guid runnerId)
	{
		if (runners.All(runner => runner.Id != runnerId))
		{
			return false;
		}
		View.ActiveRunnerId = runnerId;
		return true;
	}

	public bool SetActiveCollector(Guid? systemId, Guid? inletId = null)
	{
		if (systemId.HasValue)
		{
			CadCollectorSystem system = collectorSystems.FirstOrDefault(item => item.Id == systemId.Value);
			if (system == null || inletId.HasValue && system.Inlets.All(inlet => inlet.Id != inletId.Value))
			{
				return false;
			}
		}
		View.ActiveCollectorSystemId = systemId;
		View.ActiveCollectorInletId = inletId;
		return true;
	}

	public bool TryCreateCollectorSystem(
		IEnumerable<Guid> runnerIds,
		CollectorLayoutPreset preset,
		string name,
		IReadOnlyDictionary<Guid, RunnerEvaluationResult> authoritativeEvaluations,
		out CadCollectorSystem system,
		out string error
	)
	{
		CollectorSystemTransaction transaction = CollectorSystemTransaction.Begin(this);
		if (!transaction.TryCreate(
				runnerIds,
				preset,
				name,
				authoritativeEvaluations,
				out system,
				out error
			)
			|| !transaction.Commit(out error))
		{
			system = null;
			return false;
		}
		Guid committedSystemId = system.Id;
		system = collectorSystems.Single(item => item.Id == committedSystemId);
		View.ActiveCollectorSystemId = system.Id;
		View.ActiveCollectorInletId = system.Inlets.FirstOrDefault()?.Id;
		return true;
	}

	public bool TryCreateCollectorSystem(
		IEnumerable<Guid> runnerIds,
		CollectorLayoutPreset preset,
		string name,
		out CadCollectorSystem system,
		out string error
	)
	{
		return TryCreateCollectorSystem(
			runnerIds,
			preset,
			name,
			null,
			out system,
			out error
		);
	}

	public bool TryDeleteCollectorSystem(
		Guid systemId,
		IReadOnlyDictionary<Guid, RunnerEvaluationResult> authoritativeEvaluations,
		out string error
	)
	{
		CollectorSystemTransaction transaction = CollectorSystemTransaction.Begin(this);
		if (!transaction.TryDelete(
				systemId,
				authoritativeEvaluations,
				out error
			)
			|| !transaction.Commit(out error))
		{
			return false;
		}
		if (View.ActiveCollectorSystemId == systemId)
		{
			View.ActiveCollectorSystemId = null;
			View.ActiveCollectorInletId = null;
		}
		return true;
	}

	public CadPart AddPart(string name, string sourcePath = null, Guid? id = null)
	{
		CadPart part = new()
		{
			Id = id ?? Guid.NewGuid(),
			Name = string.IsNullOrWhiteSpace(name) ? "Part" : name,
			SourcePath = sourcePath,
		};

		if (parts.Any(existing => existing.Id == part.Id))
		{
			throw new ArgumentException($"Part ID '{part.Id}' already exists.", nameof(id));
		}

		parts.Add(part);
		return part;
	}

	public CadMate AddMate(Guid partId, string name, Guid? id = null)
	{
		if (parts.All(part => part.Id != partId))
		{
			throw new ArgumentException("The mate's owning part does not exist.", nameof(partId));
		}

		CadMate mate = new()
		{
			Id = id ?? Guid.NewGuid(),
			PartId = partId,
			Name = string.IsNullOrWhiteSpace(name) ? "Mate" : name,
		};

		if (mates.Any(existing => existing.Id == mate.Id))
		{
			throw new ArgumentException($"Mate ID '{mate.Id}' already exists.", nameof(id));
		}

		mates.Add(mate);
		return mate;
	}

	public void ReplacePart(Guid partId, string replacementSourcePath)
	{
		CadPart part = parts.Single(candidate => candidate.Id == partId);
		part.SourcePath = replacementSourcePath;

		HashSet<Guid> affectedMateIds = mates
			.Where(candidate => candidate.PartId == partId)
			.Select(candidate => candidate.Id)
			.ToHashSet();
		foreach (CadMate mate in mates.Where(candidate => candidate.PartId == partId))
		{
			mate.Invalidate();
		}
		foreach (CadCollectorSystem system in collectorSystems.Where(system =>
			system.Inlets.Any(inlet =>
				runners.Any(runner =>
					runner.Id == inlet.Binding?.RunnerId
						&& affectedMateIds.Contains(runner.StartMateId)))))
		{
			system.IsResolved = false;
			system.Diagnostic = "A collector member start mate requires rebinding after part replacement.";
		}
	}

	public bool RemovePart(Guid partId)
	{
		bool removed = parts.RemoveAll(part => part.Id == partId) > 0;

		if (removed)
		{
			HashSet<Guid> removedMates = mates.Where(mate => mate.PartId == partId).Select(mate => mate.Id).ToHashSet();
			HashSet<Guid> removedRunnerIds = runners
				.Where(runner => removedMates.Contains(runner.StartMateId))
				.Select(runner => runner.Id)
				.ToHashSet();
			CadCollectorSystem[] removedCollectors = collectorSystems
				.Where(system => system.Inlets.Any(inlet =>
					inlet.Binding != null && removedRunnerIds.Contains(inlet.Binding.RunnerId)))
				.ToArray();
			HashSet<Guid> detachedRunnerIds = removedCollectors
				.SelectMany(system => system.Inlets)
				.Where(inlet => inlet.Binding != null
					&& !removedRunnerIds.Contains(inlet.Binding.RunnerId))
				.Select(inlet => inlet.Binding.RunnerId)
				.ToHashSet();
			bool activeRunnerRemoved = View.ActiveRunnerId.HasValue && runners.Any(runner =>
				runner.Id == View.ActiveRunnerId.Value && removedMates.Contains(runner.StartMateId));
			runners.RemoveAll(runner => removedMates.Contains(runner.StartMateId));
			collectorSystems.RemoveAll(system => removedCollectors.Any(
				removedCollector => removedCollector.Id == system.Id));
			mates.RemoveAll(mate => mate.PartId == partId);
			foreach (CadRunner detached in runners.Where(runner =>
				detachedRunnerIds.Contains(runner.Id)))
			{
				detached.CommitEdit();
			}
			if (activeRunnerRemoved)
			{
				View.ActiveRunnerId = runners.FirstOrDefault()?.Id;
			}
			if (View.ActiveCollectorSystemId.HasValue
				&& removedCollectors.Any(system =>
					system.Id == View.ActiveCollectorSystemId.Value))
			{
				View.ActiveCollectorSystemId = null;
				View.ActiveCollectorInletId = null;
			}
		}

		return removed;
	}

	public RunnerEvaluationResult EvaluateRunner(CadRunner runner)
	{
		ArgumentNullException.ThrowIfNull(runner);
		return RunnerGraphEvaluator.Evaluate(
			runner,
			mates.ToDictionary(mate => mate.Id),
			parts.ToDictionary(part => part.Id)
		);
	}

	public IReadOnlyList<RunnerEvaluationResult> EvaluateRunners()
	{
		return runners.Select(EvaluateRunner).ToArray();
	}

	public Task<RunnerEvaluationResult> EvaluateRunnerAsync(
		CadDocument document,
		CadRunner runner,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(document);
		ArgumentNullException.ThrowIfNull(runner);
		if (runners.All(candidate => candidate.Id != runner.Id))
		{
			throw new ArgumentException("The runner does not belong to this project.", nameof(runner));
		}
		return document.EvaluateRunnerAsync(
			runner,
			mates.ToDictionary(mate => mate.Id),
			parts.ToDictionary(part => part.Id),
			GetEndpointConstraint(runner),
			cancellationToken
		);
	}

	public RunnerEndpointConstraint? GetEndpointConstraint(CadRunner runner)
	{
		ArgumentNullException.ThrowIfNull(runner);
		foreach (CadCollectorSystem system in collectorSystems)
		{
			CadCollectorInlet inlet = system.Inlets.FirstOrDefault(item => item.Binding?.RunnerId == runner.Id);
			if (inlet != null)
			{
				return GetEndpointConstraint(system, inlet);
			}
		}
		return null;
	}

	internal RunnerEndpointConstraint GetEndpointConstraint(
		CadCollectorSystem system,
		CadCollectorInlet inlet
	)
	{
		CadCollectorBinding binding = inlet.Binding
			?? throw new InvalidOperationException("The collector inlet is not bound.");
		CadFrame terminal = system.GetWorldInletFrame(inlet);
		CadFrame bezierEnd = binding.ClockingTransitionNodeId.HasValue
			? new CadFrame(
				terminal.Origin - terminal.Tangent * inlet.ClockingTransitionLength,
				terminal.Tangent,
				terminal.Normal
			)
			: terminal;
		CadRunner runner = runners.Single(item => item.Id == binding.RunnerId);
		RunnerNode terminalBezier = runner.Graph.Nodes.Single(
			node => node.Id == binding.TerminalBezierNodeId);
		double endHandleLength = terminalBezier.Properties.TryGetValue(
				"endHandleLength",
				out string storedHandle
			)
			&& double.TryParse(
				storedHandle,
				System.Globalization.NumberStyles.Float,
				System.Globalization.CultureInfo.InvariantCulture,
				out double parsedHandle
			)
			&& double.IsFinite(parsedHandle)
			&& parsedHandle > 0
			? parsedHandle
			: system.BranchEndHandleLength;
		return new RunnerEndpointConstraint(
			system.Id,
			system.GenerationRevision,
			inlet.Id,
			binding.TerminalBezierNodeId,
			bezierEnd,
			terminal,
			endHandleLength,
			binding.ClockingTransitionNodeId,
			inlet.ClockingTransitionLength
		);
	}

	internal void AddLoadedPart(CadPart part)
	{
		ArgumentNullException.ThrowIfNull(part);
		if (parts.Any(existing => existing.Id == part.Id))
		{
			throw new InvalidDataException($"Duplicate part ID '{part.Id}'.");
		}
		parts.Add(part);
	}

	internal void AddLoadedMate(CadMate mate)
	{
		ArgumentNullException.ThrowIfNull(mate);
		if (parts.All(part => part.Id != mate.PartId))
		{
			throw new InvalidDataException($"Mate '{mate.Name}' references a missing part.");
		}
		if (mates.Any(existing => existing.Id == mate.Id))
		{
			throw new InvalidDataException($"Duplicate mate ID '{mate.Id}'.");
		}
		mates.Add(mate);
	}

	internal void AddLoadedRunner(CadRunner runner)
	{
		if (runners.Any(existing => existing.Id == runner.Id))
		{
			throw new InvalidDataException($"Duplicate runner ID '{runner.Id}'.");
		}
		if (mates.All(mate => mate.Id != runner.StartMateId))
		{
			throw new InvalidDataException($"Runner '{runner.Name}' references a missing start mate.");
		}
		if (runners.Any(existing => existing.StartMateId == runner.StartMateId))
		{
			throw new InvalidDataException($"Mate '{runner.StartMateId}' owns more than one runner.");
		}
		runners.Add(runner);
		View.ActiveRunnerId ??= runner.Id;
	}

	internal void AddLoadedCollectorSystem(CadCollectorSystem system)
	{
		ArgumentNullException.ThrowIfNull(system);
		if (collectorSystems.Any(existing => existing.Id == system.Id))
		{
			throw new InvalidDataException($"Duplicate collector system ID '{system.Id}'.");
		}
		if (system.Inlets == null)
		{
			throw new InvalidDataException("A collector system requires an inlet collection.");
		}
		HashSet<Guid> bound = collectorSystems.SelectMany(item => item.Inlets)
			.Where(inlet => inlet.Binding != null)
			.Select(inlet => inlet.Binding.RunnerId)
			.ToHashSet();
		foreach (CadCollectorInlet inlet in system.Inlets)
		{
			CadRunner runner = runners.FirstOrDefault(item => item.Id == inlet.Binding?.RunnerId);
			if (inlet.Binding == null
				|| runner == null
				|| !bound.Add(inlet.Binding.RunnerId))
			{
				throw new InvalidDataException(
					$"Collector inlet '{inlet.Name}' has an invalid or duplicate runner binding."
				);
			}
			if (runner.Graph.Nodes.All(node =>
				node.Id != inlet.Binding.TerminalBezierNodeId
					|| node.DefinitionId != RunnerNodes.CubicBezier)
				|| inlet.Binding.ClockingTransitionNodeId.HasValue
				&& runner.Graph.Nodes.All(node =>
					node.Id != inlet.Binding.ClockingTransitionNodeId.Value
						|| node.DefinitionId != RunnerNodes.ClockingTransition))
			{
				throw new InvalidDataException(
					$"Collector inlet '{inlet.Name}' references missing terminal graph nodes.");
			}
		}
		Dictionary<Guid, RunnerGraph> graphs = runners.ToDictionary(
			runner => runner.Id,
			runner => runner.Graph);
		if (!CollectorSystemTransaction.ValidateSystem(system, graphs, out string validationError))
		{
			throw new InvalidDataException(validationError);
		}
		collectorSystems.Add(system);
	}

	internal void CommitCollectorTransaction(
		IReadOnlyDictionary<Guid, RunnerGraph> graphs,
		IReadOnlyList<CadCollectorSystem> systems
	)
	{
		HashSet<Guid> previouslyBound = collectorSystems
			.SelectMany(system => system.Inlets)
			.Where(inlet => inlet.Binding != null)
			.Select(inlet => inlet.Binding.RunnerId)
			.ToHashSet();
		HashSet<Guid> nextBound = systems
			.SelectMany(system => system.Inlets)
			.Where(inlet => inlet.Binding != null)
			.Select(inlet => inlet.Binding.RunnerId)
			.ToHashSet();
		foreach (CadRunner runner in runners)
		{
			runner.Graph = graphs[runner.Id];
			if (previouslyBound.Contains(runner.Id) && !nextBound.Contains(runner.Id))
			{
				runner.CommitEdit();
			}
		}
		collectorSystems.Clear();
		collectorSystems.AddRange(systems.Select(system => system.DeepClone()));
	}
}
