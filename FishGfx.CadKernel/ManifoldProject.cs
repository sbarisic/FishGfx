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
}

public sealed class CadRunner
{
	public Guid Id { get; set; } = Guid.NewGuid();

	public string Name { get; set; } = "Runner";

	public Guid StartMateId { get; set; }

	public RunnerGraph Graph { get; set; } = new();
}

public sealed class ManifoldProject
{
	private readonly List<CadPart> parts = new();
	private readonly List<CadMate> mates = new();
	private readonly List<CadRunner> runners = new();

	public ManifoldProject()
	{
		Parts = parts.AsReadOnly();
		Mates = mates.AsReadOnly();
		Runners = runners.AsReadOnly();
	}

	public Guid Id { get; set; } = Guid.NewGuid();

	public string Name { get; set; } = "Untitled Manifold";

	public IReadOnlyList<CadPart> Parts { get; }

	public IReadOnlyList<CadMate> Mates { get; }

	public IReadOnlyList<CadRunner> Runners { get; }

	public CadRunner ActiveRunner => View.ActiveRunnerId.HasValue
		? runners.FirstOrDefault(runner => runner.Id == View.ActiveRunnerId.Value)
		: runners.FirstOrDefault();

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

		foreach (CadMate mate in mates.Where(candidate => candidate.PartId == partId))
		{
			mate.Invalidate();
		}
	}

	public bool RemovePart(Guid partId)
	{
		bool removed = parts.RemoveAll(part => part.Id == partId) > 0;

		if (removed)
		{
			HashSet<Guid> removedMates = mates.Where(mate => mate.PartId == partId).Select(mate => mate.Id).ToHashSet();
			bool activeRunnerRemoved = View.ActiveRunnerId.HasValue && runners.Any(runner =>
				runner.Id == View.ActiveRunnerId.Value && removedMates.Contains(runner.StartMateId));
			runners.RemoveAll(runner => removedMates.Contains(runner.StartMateId));
			mates.RemoveAll(mate => mate.PartId == partId);
			if (activeRunnerRemoved)
			{
				View.ActiveRunnerId = runners.FirstOrDefault()?.Id;
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
}
