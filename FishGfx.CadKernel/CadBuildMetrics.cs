namespace FishGfx.Cad;

[Flags]
public enum CadBuildCacheLayers
{
	None = 0,
	RunnerSection = 1 << 0,
	RunnerSolid = 1 << 1,
	CollectorBody = 1 << 2,
	SystemAssembly = 1 << 3,
	Tessellation = 1 << 4,
}

public readonly record struct CadTopologyMetrics(
	uint SolidCount,
	uint ShellCount,
	uint FaceCount,
	uint EdgeCount,
	uint VertexCount
);

public readonly record struct CadBuildOperationMetrics(
	uint SweepCount,
	uint LoftCount,
	uint SewCount,
	uint MergeBooleanCount,
	uint InterfaceBooleanCount,
	uint FinalBooleanCount,
	uint CutCount,
	uint ValidationCount,
	uint ClassificationCount
);

public readonly record struct CadBuildStageMetrics(
	TimeSpan Total,
	TimeSpan Evaluation,
	TimeSpan Sweeps,
	TimeSpan Lofts,
	TimeSpan Sewing,
	TimeSpan Merge,
	TimeSpan Validation,
	TimeSpan Tessellation
);

public readonly record struct CadBuildMetrics(
	ulong Revision,
	CadBuildStageMetrics Stages,
	CadBuildOperationMetrics Operations,
	CadBuildCacheLayers CacheLayers,
	double MeasuredGapMillimetres,
	double SelectedToleranceMillimetres,
	CadTopologyMetrics Topology
);
