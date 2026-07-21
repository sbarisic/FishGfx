namespace FishGfx.Voxels;

public enum VoxelTransparentInvalidationReason
{
	None,
	FirstFrame,
	Geometry,
	ActiveSet,
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
		VoxelTransparentInvalidationReason transparentInvalidationReason,
		int transparentFaceCount,
		int transparentIndexCount,
		double transparentSourceBuildMilliseconds,
		double transparentWorkerSortMilliseconds,
		double transparentResultApplyMilliseconds,
		double transparentIndexUploadMilliseconds,
		double transparentGpuMilliseconds,
		int transparentMainThreadAllocatedBytes,
		int transparentWorkerAllocatedBytes,
		bool transparentOrderingPending,
		bool transparentOrderingRunning,
		int transparentCoalescedRequests,
		int transparentStaleResults,
		int transparentDroppedResults,
		long transparentOrderingGeometryRevision,
		double transparentOrderingAgeSeconds,
		float transparentOrderingCameraDistanceDelta,
		float transparentOrderingCameraAngleDeltaDegrees,
		VoxelTransparentInvalidationReason transparentOrderingReason,
		double activeSetRefreshMilliseconds,
		int activeSetAllocatedBytes,
		int activeSetVisitedColumns,
		int activeSetTestedChunks,
		int activeSetAdditions,
		int activeSetRemovals,
		VoxelActiveSetRefreshReason activeSetRefreshReason,
		long meshUploadBytes,
		int meshUploadSlices,
		double meshUploadPreparationMilliseconds,
		int meshUploadStorageGrowths,
		double oldestMeshUploadJobAgeSeconds,
		int completedUploadJobs,
		int discardedUploadJobs,
		long queuedMeshUploadBytes,
		VoxelRendererWorkload workload
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
		TransparentFaceCount = transparentFaceCount;
		TransparentIndexCount = transparentIndexCount;
		TransparentSourceBuildMilliseconds = transparentSourceBuildMilliseconds;
		TransparentWorkerSortMilliseconds = transparentWorkerSortMilliseconds;
		TransparentResultApplyMilliseconds = transparentResultApplyMilliseconds;
		TransparentIndexUploadMilliseconds = transparentIndexUploadMilliseconds;
		TransparentGpuMilliseconds = transparentGpuMilliseconds;
		TransparentMainThreadAllocatedBytes = transparentMainThreadAllocatedBytes;
		TransparentWorkerAllocatedBytes = transparentWorkerAllocatedBytes;
		TransparentOrderingPending = transparentOrderingPending;
		TransparentOrderingRunning = transparentOrderingRunning;
		TransparentCoalescedRequests = transparentCoalescedRequests;
		TransparentStaleResults = transparentStaleResults;
		TransparentDroppedResults = transparentDroppedResults;
		TransparentOrderingGeometryRevision = transparentOrderingGeometryRevision;
		TransparentOrderingAgeSeconds = transparentOrderingAgeSeconds;
		TransparentOrderingCameraDistanceDelta = transparentOrderingCameraDistanceDelta;
		TransparentOrderingCameraAngleDeltaDegrees = transparentOrderingCameraAngleDeltaDegrees;
		TransparentOrderingReason = transparentOrderingReason;
		ActiveSetRefreshMilliseconds = activeSetRefreshMilliseconds;
		ActiveSetAllocatedBytes = activeSetAllocatedBytes;
		ActiveSetVisitedColumns = activeSetVisitedColumns;
		ActiveSetTestedChunks = activeSetTestedChunks;
		ActiveSetAdditions = activeSetAdditions;
		ActiveSetRemovals = activeSetRemovals;
		ActiveSetRefreshReason = activeSetRefreshReason;
		MeshUploadBytes = meshUploadBytes;
		MeshUploadSlices = meshUploadSlices;
		MeshUploadPreparationMilliseconds = meshUploadPreparationMilliseconds;
		MeshUploadStorageGrowths = meshUploadStorageGrowths;
		OldestMeshUploadJobAgeSeconds = oldestMeshUploadJobAgeSeconds;
		CompletedUploadJobs = completedUploadJobs;
		DiscardedUploadJobs = discardedUploadJobs;
		QueuedMeshUploadBytes = queuedMeshUploadBytes;
		Workload = workload;
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
	public int ManagedAllocatedBytes => CullingAndCommandAllocatedBytes
		+ SubmissionAllocatedBytes
		+ TransparentMainThreadAllocatedBytes;
	public bool TransparentCacheHit { get; }
	public VoxelTransparentInvalidationReason TransparentInvalidationReason { get; }
	public int TransparentFaceCount { get; }
	public int TransparentIndexCount { get; }
	public double TransparentSourceBuildMilliseconds { get; }
	public double TransparentWorkerSortMilliseconds { get; }
	public double TransparentResultApplyMilliseconds { get; }
	public double TransparentIndexUploadMilliseconds { get; }
	public double TransparentGpuMilliseconds { get; }
	public int TransparentMainThreadAllocatedBytes { get; }
	public int TransparentWorkerAllocatedBytes { get; }
	public bool TransparentOrderingPending { get; }
	public bool TransparentOrderingRunning { get; }
	public int TransparentCoalescedRequests { get; }
	public int TransparentStaleResults { get; }
	public int TransparentDroppedResults { get; }
	public long TransparentOrderingGeometryRevision { get; }
	public double TransparentOrderingAgeSeconds { get; }
	public float TransparentOrderingCameraDistanceDelta { get; }
	public float TransparentOrderingCameraAngleDeltaDegrees { get; }
	public VoxelTransparentInvalidationReason TransparentOrderingReason { get; }
	public double ActiveSetRefreshMilliseconds { get; }
	public int ActiveSetAllocatedBytes { get; }
	public int ActiveSetVisitedColumns { get; }
	public int ActiveSetTestedChunks { get; }
	public int ActiveSetAdditions { get; }
	public int ActiveSetRemovals { get; }
	public VoxelActiveSetRefreshReason ActiveSetRefreshReason { get; }
	public long MeshUploadBytes { get; }
	public int MeshUploadSlices { get; }
	public double MeshUploadPreparationMilliseconds { get; }
	public int MeshUploadStorageGrowths { get; }
	public double OldestMeshUploadJobAgeSeconds { get; }
	public int CompletedUploadJobs { get; }
	public int DiscardedUploadJobs { get; }
	public long QueuedMeshUploadBytes { get; }
	public VoxelRendererWorkload Workload { get; }
}
