namespace FishGfx.Voxels;

/// <summary>
/// Describes how far a voxel chunk has progressed through lighting, meshing, and
/// GPU publication. Streaming clients can use it without inspecting renderer internals.
/// </summary>
public enum VoxelPresentationState
{
	Missing,
	WaitingForLighting,
	Meshing,
	Resident,
	EmptyComplete,
}
