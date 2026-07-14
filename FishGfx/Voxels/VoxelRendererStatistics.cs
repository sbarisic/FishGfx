namespace FishGfx.Voxels;

public readonly struct VoxelRendererStatistics
{
	internal VoxelRendererStatistics(
		int loadedChunks,
		int gpuChunks,
		int visibleChunks,
		int pendingJobs,
		int acceptedMeshes,
		int discardedMeshes,
		int opaqueVertices,
		int cutoutVertices,
		int transparentFaces,
		int transparentVertices
	)
	{
		LoadedChunks = loadedChunks;
		GpuChunks = gpuChunks;
		VisibleChunks = visibleChunks;
		PendingJobs = pendingJobs;
		AcceptedMeshes = acceptedMeshes;
		DiscardedMeshes = discardedMeshes;
		OpaqueVertices = opaqueVertices;
		CutoutVertices = cutoutVertices;
		TransparentFaces = transparentFaces;
		TransparentVertices = transparentVertices;
	}

	public int LoadedChunks { get; }

	public int GpuChunks { get; }

	public int VisibleChunks { get; }

	public int PendingJobs { get; }

	public int AcceptedMeshes { get; }

	public int DiscardedMeshes { get; }

	public int OpaqueVertices { get; }

	public int CutoutVertices { get; }

	public int TransparentFaces { get; }

	public int TransparentVertices { get; }
}
