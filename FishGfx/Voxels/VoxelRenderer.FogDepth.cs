using System;
using System.Collections.Generic;
using System.Numerics;
using FishGfx.Graphics;

namespace FishGfx.Voxels;

public sealed partial class VoxelRenderer
{
	public int RenderFogDepthOccluders(
		RenderPass pass,
		Camera camera,
		float? maxRenderDistance = null
	)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(pass);
		ArgumentNullException.ThrowIfNull(camera);
		float distance = maxRenderDistance ?? options.MaxRenderDistance;

		if (!float.IsFinite(distance) || distance <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(maxRenderDistance));
		}

		RefreshActiveSetIfNeeded(camera.Position, distance);
		ViewFrustum frustum = ViewFrustum.FromCamera(camera);
		float distanceSquared = distance * distance;
		shadowAlpha.Clear();

		for (int index = 0; index < activeGpuChunks.Count; index++)
		{
			GpuChunk chunk = activeGpuChunks[index];
			VoxelGeometryAllocation alphaShadow = chunk.AlphaShadow;

			if (alphaShadow is null
				|| alphaShadow.VertexCount <= 0
				|| chunk.Bounds.IsEmpty)
			{
				continue;
			}

			AxisAlignedBoundingBox worldBounds = chunk.Bounds.Translate(
				chunk.Coordinate.WorldOrigin
			);

			if (cullingEnabled
				&& (Vector3.DistanceSquared(camera.Position, worldBounds.Center)
					> distanceSquared
					|| !frustum.Intersects(worldBounds)))
			{
				continue;
			}

			shadowAlpha.Add(new VoxelPassEntry(
				alphaShadow,
				chunk.Coordinate,
				0
			));
		}

		if (shadowAlpha.Count == 0)
		{
			return 0;
		}

		RenderState depthState = pass.State with
		{
			BlendEnabled = false,
			ColorWriteMask = ColorWriteMask.None,
			DepthTestEnabled = true,
			DepthWriteEnabled = true,
			DepthCompare = CompareFunction.LessOrEqual,
			DepthBiasSlope = 0,
			DepthBiasConstant = 0,
		};
		using DrawVoxelShadowPagesCommand command = new(
			surfaceTextures,
			shadowOpaqueShader,
			shadowAlphaShader,
			indirectBuffer,
			options.AlphaCutoff,
			depthState,
			Array.Empty<VoxelPassEntry>(),
			Array.Empty<VoxelPassEntry>(),
			shadowAlpha
		);
		command.Execute(pass);
		return command.DriverDrawCount;
	}
}
