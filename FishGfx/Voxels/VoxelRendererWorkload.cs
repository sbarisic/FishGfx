namespace FishGfx.Voxels;

public readonly record struct VoxelRendererWorkload(
	int DirtyMeshes,
	int InFlightMeshes,
	int CompletedMeshes,
	int PendingUploadJobs,
	long PendingUploadBytes,
	bool IsBackpressured);
