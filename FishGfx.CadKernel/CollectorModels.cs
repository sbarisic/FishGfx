namespace FishGfx.Cad;

public enum CollectorLayoutPreset
{
	Row,
	Radial,
	Staggered,
}

public enum CadGenerationOwnerKind
{
	Runner,
	CollectorSystem,
}

public readonly record struct CadGenerationStamp(
	CadGenerationOwnerKind OwnerKind,
	Guid OwnerId,
	long Revision
);

public sealed class CadCollectorBinding
{
	public Guid RunnerId { get; set; }

	public Guid TerminalBezierNodeId { get; set; }

	public Guid? ClockingTransitionNodeId { get; set; }

	internal CadCollectorBinding DeepClone()
	{
		return new CadCollectorBinding
		{
			RunnerId = RunnerId,
			TerminalBezierNodeId = TerminalBezierNodeId,
			ClockingTransitionNodeId = ClockingTransitionNodeId,
		};
	}
}

public sealed class CadCollectorInlet
{
	public Guid Id { get; set; } = Guid.NewGuid();

	public string Name { get; set; } = "Inlet";

	public CadFrame LocalFrame { get; set; } = CollectorFrameDefaults.Inlet;

	public double MergeStation { get; set; } = 0.5;

	public double BranchStartHandleLength { get; set; } = 35;

	public double ClockingTransitionLength { get; set; } = 20;

	public CadCollectorBinding Binding { get; set; }

	internal CadCollectorInlet DeepClone()
	{
		return new CadCollectorInlet
		{
			Id = Id,
			Name = Name,
			LocalFrame = LocalFrame,
			MergeStation = MergeStation,
			BranchStartHandleLength = BranchStartHandleLength,
			ClockingTransitionLength = ClockingTransitionLength,
			Binding = Binding?.DeepClone(),
		};
	}
}

public sealed class CadCollectorSystem
{
	private long generationRevision;

	public Guid Id { get; set; } = Guid.NewGuid();

	public string Name { get; set; } = "Collector";

	public CadFrame OutletFrame { get; set; } = CollectorFrameDefaults.Outlet;

	public PipeProfile OutletProfile { get; set; } = new(63.5, 2);

	public double OutletStubLength { get; set; } = 50;

	public double MergeLength { get; set; } = 100;

	public double OverlapLength { get; set; } = 12;

	public double BranchEndHandleLength { get; set; } = 35;

	public List<CadCollectorInlet> Inlets { get; set; } = new();

	public long GenerationRevision => Interlocked.Read(ref generationRevision);

	public bool IsResolved { get; set; } = true;

	public string Diagnostic { get; set; }

	public CadGenerationStamp GenerationStamp =>
		new(CadGenerationOwnerKind.CollectorSystem, Id, GenerationRevision);

	public CadFrame GetWorldInletFrame(CadCollectorInlet inlet)
	{
		ArgumentNullException.ThrowIfNull(inlet);
		return OutletFrame.Compose(inlet.LocalFrame);
	}

	public long CommitEdit()
	{
		return Interlocked.Increment(ref generationRevision);
	}

	internal void SetGenerationRevision(long value)
	{
		Interlocked.Exchange(ref generationRevision, value);
	}

	internal CadCollectorSystem DeepClone()
	{
		CadCollectorSystem clone = new()
		{
			Id = Id,
			Name = Name,
			OutletFrame = OutletFrame,
			OutletProfile = OutletProfile,
			OutletStubLength = OutletStubLength,
			MergeLength = MergeLength,
			OverlapLength = OverlapLength,
			BranchEndHandleLength = BranchEndHandleLength,
			Inlets = Inlets.Select(inlet => inlet.DeepClone()).ToList(),
			IsResolved = IsResolved,
			Diagnostic = Diagnostic,
		};
		clone.SetGenerationRevision(GenerationRevision);
		return clone;
	}
}

public readonly record struct RunnerEndpointConstraint(
	Guid CollectorSystemId,
	long GenerationRevision,
	Guid InletId,
	Guid TerminalBezierNodeId,
	CadFrame BezierEndFrame,
	CadFrame TerminalFrame,
	double EndHandleLength,
	Guid? ClockingTransitionNodeId,
	double ClockingTransitionLength
)
{
	public CadGenerationStamp Stamp =>
		new(CadGenerationOwnerKind.CollectorSystem, CollectorSystemId, GenerationRevision);
}

internal static class CollectorFrameDefaults
{
	internal static CadFrame Outlet =>
		new(new CadPoint3(400, 0, 0), new CadPoint3(1, 0, 0), new CadPoint3(0, 1, 0));

	internal static CadFrame Inlet =>
		new(CadPoint3.Zero, new CadPoint3(1, 0, 0), new CadPoint3(0, 1, 0));
}
