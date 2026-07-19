using System;
using System.Collections.Generic;
using System.Numerics;

namespace FishGfx.Graphics.Shadows;

public enum DirectionalShadowFilter
{
	Pcf3x3,
	Pcf5x5,
}

[Flags]
public enum DirectionalShadowDirtyReason
{
	None = 0,
	FirstUse = 1 << 0,
	Camera = 1 << 1,
	Sun = 1 << 2,
	VoxelGeometry = 1 << 3,
	DynamicActor = 1 << 4,
	Quality = 1 << 5,
	Teleport = 1 << 6,
	Sunrise = 1 << 7,
}

public sealed record DirectionalShadowOptions(
	int CascadeCount,
	int Resolution,
	float MaximumDistance,
	float SplitLambda,
	float CascadeBlendFraction,
	DirectionalShadowFilter Filter,
	float RasterSlopeBias,
	float RasterConstantBias)
{
	public IReadOnlyList<int> UpdateIntervals { get; init; } = Array.Empty<int>();

	internal void Validate()
	{
		if (CascadeCount is < 0 or > DirectionalShadowFrame.MaximumCascades)
		{
			throw new ArgumentOutOfRangeException(nameof(CascadeCount));
		}

		if (CascadeCount > 0 && Resolution <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(Resolution));
		}

		if (!float.IsFinite(MaximumDistance) || MaximumDistance < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(MaximumDistance));
		}

		if (!float.IsFinite(SplitLambda) || SplitLambda is < 0 or > 1)
		{
			throw new ArgumentOutOfRangeException(nameof(SplitLambda));
		}

		if (!float.IsFinite(CascadeBlendFraction) || CascadeBlendFraction is < 0 or > 0.5f)
		{
			throw new ArgumentOutOfRangeException(nameof(CascadeBlendFraction));
		}

		if (!Enum.IsDefined(Filter))
		{
			throw new ArgumentOutOfRangeException(nameof(Filter));
		}

		if (!float.IsFinite(RasterSlopeBias) || !float.IsFinite(RasterConstantBias))
		{
			throw new ArgumentOutOfRangeException(nameof(RasterSlopeBias));
		}

		for (int index = 0; index < UpdateIntervals.Count; index++)
		{
			if (UpdateIntervals[index] <= 0)
			{
				throw new ArgumentOutOfRangeException(nameof(UpdateIntervals));
			}
		}
	}

	internal int GetUpdateInterval(int cascadeIndex)
	{
		if (cascadeIndex < UpdateIntervals.Count)
		{
			return UpdateIntervals[cascadeIndex];
		}

		return cascadeIndex switch
		{
			0 => 1,
			1 => 2,
			_ => 4,
		};
	}
}

public readonly record struct DirectionalShadowCascade(
	int Index,
	Camera Camera,
	Matrix4x4 ViewProjection,
	float NearDistance,
	float FarDistance,
	Vector2 TexelWorldSize)
{
	public ViewFrustum CasterFrustum => ViewFrustum.FromCamera(Camera);
}

public readonly record struct DirectionalShadowCascadeDiagnostics(
	int Index,
	float NearDistance,
	float FarDistance,
	long AgeFrames,
	DirectionalShadowDirtyReason DirtyReasons,
	int CasterChunkCount,
	int LogicalCommandCount,
	int DriverDrawCount,
	double GpuMilliseconds);

public readonly record struct DirectionalShadowDiagnostics(
	bool Enabled,
	float EffectiveDistance,
	int CascadeCount,
	int RefreshedCascadeCount,
	DirectionalShadowDirtyReason DirtyReasons,
	double CullingMilliseconds,
	double CommandBuildMilliseconds,
	double SubmissionMilliseconds,
	long ManagedAllocationBytes,
	IReadOnlyList<DirectionalShadowCascadeDiagnostics> Cascades);
