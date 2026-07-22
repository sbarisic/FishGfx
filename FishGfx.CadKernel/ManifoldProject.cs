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
}

public sealed class ManifoldProject
{
	private readonly List<CadPart> parts = new();
	private readonly List<CadMate> mates = new();

	public ManifoldProject()
	{
		Parts = parts.AsReadOnly();
		Mates = mates.AsReadOnly();
		Graph = new RunnerGraph();
	}

	public Guid Id { get; set; } = Guid.NewGuid();

	public string Name { get; set; } = "Untitled Manifold";

	public IReadOnlyList<CadPart> Parts { get; }

	public IReadOnlyList<CadMate> Mates { get; }

	public RunnerGraph Graph { get; set; }

	public ManifoldViewState View { get; set; } = new();

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
			mates.RemoveAll(mate => mate.PartId == partId);
		}

		return removed;
	}

	public RunnerEvaluationResult EvaluateRunner()
	{
		return RunnerGraphEvaluator.Evaluate(
			Graph,
			mates.ToDictionary(mate => mate.Id),
			parts.ToDictionary(part => part.Id)
		);
	}

	internal void AddLoadedPart(CadPart part)
	{
		parts.Add(part);
	}

	internal void AddLoadedMate(CadMate mate)
	{
		mates.Add(mate);
	}
}
