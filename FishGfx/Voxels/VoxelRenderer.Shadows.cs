using System;
using System.Diagnostics;
using System.Numerics;
using FishGfx.Graphics;
using FishGfx.Graphics.Shadows;

namespace FishGfx.Voxels;

public readonly record struct VoxelShadowSubmissionDiagnostics(
	int CasterChunkCount,
	int OpaqueCommandCount,
	int CutoutCommandCount,
	int AlphaShadowCommandCount,
	int DriverDrawCount,
	long AlphaShadowVertexCount,
	double CullingMilliseconds,
	double CommandBuildMilliseconds,
	double SubmissionMilliseconds,
	long ManagedAllocationBytes);

public sealed partial class VoxelRenderer
{
	public VoxelShadowSubmissionDiagnostics LastShadowSubmission { get; private set; }

	public void RenderShadowCasters(
		RenderPass pass,
		in DirectionalShadowCascade cascade)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(pass);
		long allocationStart = GC.GetAllocatedBytesForCurrentThread();
		long cullingStart = Stopwatch.GetTimestamp();
		ViewFrustum frustum = cascade.CasterFrustum;
		shadowOpaque.Clear();
		shadowCutout.Clear();
		shadowAlpha.Clear();
		int casterChunks = 0;
		long alphaVertices = 0;

		for (int index = 0; index < orderedGpuChunks.Count; index++)
		{
			GpuChunk chunk = orderedGpuChunks[index];

			if (chunk.Bounds.IsEmpty)
			{
				continue;
			}

			AxisAlignedBoundingBox bounds = chunk.Bounds.Translate(chunk.Coordinate.WorldOrigin);

			if (!frustum.Intersects(bounds))
			{
				continue;
			}

			bool hasCaster = false;

			if (chunk.Opaque?.VertexCount > 0)
			{
				shadowOpaque.Add(new VoxelPassEntry(chunk.Opaque, chunk.Coordinate, 0));
				hasCaster = true;
			}

			if (chunk.Cutout?.VertexCount > 0)
			{
				shadowCutout.Add(new VoxelPassEntry(chunk.Cutout, chunk.Coordinate, 0));
				hasCaster = true;
			}

			if (chunk.AlphaShadow?.VertexCount > 0)
			{
				shadowAlpha.Add(new VoxelPassEntry(chunk.AlphaShadow, chunk.Coordinate, 0));
				alphaVertices += chunk.AlphaShadow.VertexCount;
				hasCaster = true;
			}

			if (hasCaster)
			{
				casterChunks++;
			}
		}

		double cullingMilliseconds = Stopwatch.GetElapsedTime(cullingStart).TotalMilliseconds;
		long commandBuildStart = Stopwatch.GetTimestamp();
		using DrawVoxelShadowPagesCommand command = new(
			surfaceTextures,
			shadowOpaqueShader,
			shadowAlphaShader,
			indirectBuffer,
			options.AlphaCutoff,
			pass.State,
			shadowOpaque,
			shadowCutout,
			shadowAlpha
		);
		double commandBuildMilliseconds = Stopwatch.GetElapsedTime(
			commandBuildStart
		).TotalMilliseconds;
		long submissionStart = Stopwatch.GetTimestamp();
		command.Execute(pass);
		double submissionMilliseconds = Stopwatch.GetElapsedTime(
			submissionStart
		).TotalMilliseconds;
		LastShadowSubmission = new VoxelShadowSubmissionDiagnostics(
			casterChunks,
			shadowOpaque.Count,
			shadowCutout.Count,
			shadowAlpha.Count,
			command.DriverDrawCount,
			alphaVertices,
			cullingMilliseconds,
			commandBuildMilliseconds,
			submissionMilliseconds,
			GC.GetAllocatedBytesForCurrentThread() - allocationStart
		);
	}
}
