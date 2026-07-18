namespace FishGfx.Voxels;

public enum VoxelTransparentInvalidationReason
{
	None,
	FirstFrame,
	Geometry,
	Visibility,
	Translation,
	Rotation,
}

public readonly struct VoxelRendererFrameDiagnostics
{
	internal VoxelRendererFrameDiagnostics(
		double cullingMilliseconds,
		double commandBuildMilliseconds,
		double submissionMilliseconds,
		double gpuMilliseconds,
		double transparentBuildMilliseconds,
		double meshSchedulingMilliseconds,
		double meshUploadMilliseconds,
		int scheduledMeshes,
		int uploadedMeshes,
		int fastCompletedMeshes,
		int candidateChunks,
		int activeChunks,
		int visibleChunks,
		int inactiveCachedChunks,
		int opaqueLogicalDraws,
		int cutoutLogicalDraws,
		int transparentDrawCalls,
		int driverDrawCalls,
		int indirectCommandCount,
		int geometryPagesTouched,
		int passSubmissions,
		int transparentUploadBytes,
		int cullingAndCommandAllocatedBytes,
		int submissionAllocatedBytes,
		bool transparentCacheHit,
		VoxelTransparentInvalidationReason transparentInvalidationReason
	)
	{
		CullingMilliseconds = cullingMilliseconds;
		CommandBuildMilliseconds = commandBuildMilliseconds;
		SubmissionMilliseconds = submissionMilliseconds;
		GpuMilliseconds = gpuMilliseconds;
		TransparentBuildMilliseconds = transparentBuildMilliseconds;
		MeshSchedulingMilliseconds = meshSchedulingMilliseconds;
		MeshUploadMilliseconds = meshUploadMilliseconds;
		ScheduledMeshes = scheduledMeshes;
		UploadedMeshes = uploadedMeshes;
		FastCompletedMeshes = fastCompletedMeshes;
		CandidateChunks = candidateChunks;
		ActiveChunks = activeChunks;
		VisibleChunks = visibleChunks;
		InactiveCachedChunks = inactiveCachedChunks;
		OpaqueLogicalDraws = opaqueLogicalDraws;
		CutoutLogicalDraws = cutoutLogicalDraws;
		TransparentDrawCalls = transparentDrawCalls;
		DriverDrawCalls = driverDrawCalls;
		IndirectCommandCount = indirectCommandCount;
		GeometryPagesTouched = geometryPagesTouched;
		PassSubmissions = passSubmissions;
		TransparentUploadBytes = transparentUploadBytes;
		CullingAndCommandAllocatedBytes = cullingAndCommandAllocatedBytes;
		SubmissionAllocatedBytes = submissionAllocatedBytes;
		TransparentCacheHit = transparentCacheHit;
		TransparentInvalidationReason = transparentInvalidationReason;
	}

	public double CullingMilliseconds { get; }
	public double CommandBuildMilliseconds { get; }
	public double SubmissionMilliseconds { get; }
	public double GpuMilliseconds { get; }
	public double TransparentBuildMilliseconds { get; }
	public double MeshSchedulingMilliseconds { get; }
	public double MeshUploadMilliseconds { get; }
	public int ScheduledMeshes { get; }
	public int UploadedMeshes { get; }
	public int FastCompletedMeshes { get; }
	public int CandidateChunks { get; }
	public int ActiveChunks { get; }
	public int VisibleChunks { get; }
	public int InactiveCachedChunks { get; }
	public int OpaqueLogicalDraws { get; }
	public int CutoutLogicalDraws { get; }
	public int OpaqueDrawCalls => OpaqueLogicalDraws;
	public int CutoutDrawCalls => CutoutLogicalDraws;
	public int TransparentDrawCalls { get; }
	public int LogicalDraws => OpaqueLogicalDraws + CutoutLogicalDraws + TransparentDrawCalls;
	public int DrawCalls => LogicalDraws;
	public int DriverDrawCalls { get; }
	public int IndirectCommandCount { get; }
	public int GeometryPagesTouched { get; }
	public int ShaderBinds => PassSubmissions;
	public int TextureBinds => PassSubmissions;
	public int PassSubmissions { get; }
	public int TransparentUploadBytes { get; }
	public int CullingAndCommandAllocatedBytes { get; }
	public int SubmissionAllocatedBytes { get; }
	public int ManagedAllocatedBytes => CullingAndCommandAllocatedBytes + SubmissionAllocatedBytes;
	public bool TransparentCacheHit { get; }
	public VoxelTransparentInvalidationReason TransparentInvalidationReason { get; }
}
