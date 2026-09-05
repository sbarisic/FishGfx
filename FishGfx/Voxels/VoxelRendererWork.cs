namespace FishGfx.Voxels;

public readonly record struct VoxelMeshingWork(int Dirty, int Eligible, int Deferred,
    int LightingBlocked, int InFlight, int Completed, int Failures);

public readonly record struct VoxelRendererWork(VoxelMeshingWork Meshing, int Uploads, bool TransparentPending)
{
    public bool IsSettled => Meshing.Eligible == 0 && Meshing.InFlight == 0
        && Meshing.Completed == 0 && Meshing.Failures == 0 && Uploads == 0 && !TransparentPending;
}
