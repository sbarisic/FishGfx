using FishGfx.Voxels;
using Xunit;

namespace FishGfx.Tests;

public sealed class VoxelPresentationStateTests
{
	[Theory]
	[InlineData(false, false, false, false, VoxelPresentationState.Missing)]
	[InlineData(true, false, false, false, VoxelPresentationState.WaitingForLighting)]
	[InlineData(true, true, false, false, VoxelPresentationState.Meshing)]
	[InlineData(true, true, true, false, VoxelPresentationState.Resident)]
	[InlineData(true, true, false, true, VoxelPresentationState.EmptyComplete)]
	public void ReadinessRequiresCurrentLightingAndMeshCompletion(
		bool chunkExists,
		bool lightingPublished,
		bool residentMatches,
		bool emptyMatches,
		VoxelPresentationState expected)
	{
		Assert.Equal(
			expected,
			VoxelRenderer.ResolvePresentationState(
				chunkExists,
				lightingPublished,
				residentMatches,
				emptyMatches));
	}

	[Theory]
	[InlineData(false, 1, 1, 1, 1, false)]
	[InlineData(true, 1, 2, 1, 1, false)]
	[InlineData(true, 1, 1, 1, 2, false)]
	[InlineData(true, 2, 2, 3, 3, true)]
	public void TransparentReadinessRejectsStaleGeometryAndActiveSets(
		bool hasSnapshot,
		long snapshotGeometryRevision,
		long currentGeometryRevision,
		long snapshotActiveSetGeneration,
		long currentActiveSetGeneration,
		bool expected)
	{
		Assert.Equal(
			expected,
			VoxelRenderer.IsTransparentOrderingCurrent(
				hasSnapshot,
				snapshotGeometryRevision,
				currentGeometryRevision,
				snapshotActiveSetGeneration,
				currentActiveSetGeneration));
	}
}
