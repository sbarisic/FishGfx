namespace FishGfx.Voxels;

public sealed class VoxelMeshingOptions
{
	public bool AmbientOcclusion { get; set; } = true;

	public byte AoLevel1 { get; set; } = 210;

	public byte AoLevel2 { get; set; } = 170;

	public byte AoLevel3 { get; set; } = 125;
}
