namespace FishGfx.Voxels;

internal readonly struct VoxelWorldContentChange
{
	private VoxelWorldContentChange(
		ChunkCoordinate coordinate,
		int localIndex,
		ushort previousMaterialId,
		ushort materialId,
		bool isBulk
	)
	{
		Coordinate = coordinate;
		LocalIndex = localIndex;
		PreviousMaterialId = previousMaterialId;
		MaterialId = materialId;
		IsBulk = isBulk;
	}

	internal ChunkCoordinate Coordinate { get; }
	internal int LocalIndex { get; }
	internal ushort PreviousMaterialId { get; }
	internal ushort MaterialId { get; }
	internal bool IsBulk { get; }

	internal static VoxelWorldContentChange Single(
		ChunkCoordinate coordinate,
		int localIndex,
		ushort previousMaterialId,
		ushort materialId
	)
	{
		return new VoxelWorldContentChange(
			coordinate,
			localIndex,
			previousMaterialId,
			materialId,
			isBulk: false
		);
	}

	internal static VoxelWorldContentChange Bulk(ChunkCoordinate coordinate)
	{
		return new VoxelWorldContentChange(coordinate, -1, 0, 0, isBulk: true);
	}
}
