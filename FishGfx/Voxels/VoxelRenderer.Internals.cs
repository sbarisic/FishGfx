using System;
using System.Collections.Generic;
using System.Numerics;
using FishGfx.Graphics;

namespace FishGfx.Voxels;

public sealed partial class VoxelRenderer : IDisposable
{
	private void ProcessRemovedChunks()
	{
		while (removedChunks.TryDequeue(out ChunkCoordinate coordinate))
		{
			RemoveGpuChunk(coordinate);
		}
	}

	private void RemoveGpuChunk(ChunkCoordinate coordinate)
	{
		if (currentUploadJob?.Result.Coordinate == coordinate)
		{
			currentUploadJob.Dispose();
			currentUploadJob = null;
			discardedUploadJobs++;
		}
		completedEmptyChunks.Remove(coordinate);
		if (!gpuChunks.Remove(coordinate, out GpuChunk chunk))
		{
			return;
		}

		opaqueVertices -= chunk.Opaque?.VertexCount ?? 0;
		cutoutVertices -= chunk.Cutout?.VertexCount ?? 0;
		bool castShadow = HasShadowCasterGeometry(chunk);
		bool affectedActiveOrdering = activeCoordinates.Contains(coordinate)
			&& HasTransparentGeometry(chunk.Transparent);
		orderedGpuChunks.Remove(chunk);
		RemoveFromGpuColumn(chunk);
		activeCoordinates.Remove(coordinate);
		activeGpuChunks.Remove(chunk);
		activeSetDirty = true;
		if (castShadow)
		{
			shadowGeometryRevision++;
			EnqueueShadowInvalidation(chunk.Bounds);
		}
		if (affectedActiveOrdering)
		{
			transparentGeometryRevision++;
			transparentSourceDirty = true;
		}
		chunk.Dispose();
	}

	private void OnChunkRemoved(ChunkCoordinate coordinate)
	{
		removedChunks.Enqueue(coordinate);
	}

	private static RenderState CreateState(bool transparent)
	{
		return RenderState.Default with
		{
			Winding = Winding.CounterClockwise,
			DepthTestEnabled = true,
			CullMode = CullMode.Back,
			BlendEnabled = transparent,
			DepthWriteEnabled = !transparent,
			SourceBlend = BlendFactor.SourceAlpha,
			DestinationBlend = BlendFactor.OneMinusSourceAlpha,
		};
	}

	private static void ValidateOptions(VoxelRendererOptions options)
	{
		if (options.WorkerCount <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(options.WorkerCount));
		}

		if (options.MeshUploadBudget < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(options.MeshUploadBudget));
		}

		if (options.MaximumMeshingWorkers <= 0)
			throw new ArgumentOutOfRangeException(nameof(options.MaximumMeshingWorkers));
		if (options.MaximumReadyMeshJobs <= 0)
			throw new ArgumentOutOfRangeException(nameof(options.MaximumReadyMeshJobs));
		if (options.ResumeReadyMeshJobs < 0
			|| options.ResumeReadyMeshJobs >= options.MaximumReadyMeshJobs)
		{
			throw new ArgumentOutOfRangeException(nameof(options.ResumeReadyMeshJobs));
		}
		if (options.MaximumReadyMeshBytes <= 0)
			throw new ArgumentOutOfRangeException(nameof(options.MaximumReadyMeshBytes));
		if (options.ResumeReadyMeshBytes < 0
			|| options.ResumeReadyMeshBytes >= options.MaximumReadyMeshBytes)
		{
			throw new ArgumentOutOfRangeException(nameof(options.ResumeReadyMeshBytes));
		}
		if (options.MeshUploadByteBudget <= 0)
			throw new ArgumentOutOfRangeException(nameof(options.MeshUploadByteBudget));
		if (options.MeshUploadSliceBytes <= 0
			|| options.MeshUploadSliceBytes > options.MeshUploadByteBudget)
		{
			throw new ArgumentOutOfRangeException(nameof(options.MeshUploadSliceBytes));
		}

		if (double.IsNaN(options.MeshUploadTimeBudgetMilliseconds)
			|| options.MeshUploadTimeBudgetMilliseconds <= 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(options.MeshUploadTimeBudgetMilliseconds)
			);
		}

		if (!float.IsFinite(options.MaxRenderDistance) || options.MaxRenderDistance <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(options.MaxRenderDistance));
		}

		if (!float.IsFinite(options.AlphaCutoff) || options.AlphaCutoff < 0 || options.AlphaCutoff > 1)
		{
			throw new ArgumentOutOfRangeException(nameof(options.AlphaCutoff));
		}

		if (options.GeometryPageSizeBytes <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(options.GeometryPageSizeBytes));
		}

		ValidateNonNegativeFinite(options.TransparentResortDistance, nameof(options.TransparentResortDistance));
		ValidateNonNegativeFinite(options.TransparentResortAngleDegrees, nameof(options.TransparentResortAngleDegrees));
		ValidatePositiveFinite(options.ActiveSetRefreshDistance, nameof(options.ActiveSetRefreshDistance));
		ValidateNonNegativeFinite(options.ActivationMargin, nameof(options.ActivationMargin));
		ValidateNonNegativeFinite(options.DeactivationMargin, nameof(options.DeactivationMargin));

		if (options.DeactivationMargin < options.ActivationMargin)
		{
			throw new ArgumentOutOfRangeException(
				nameof(options.DeactivationMargin),
				"The deactivation margin must be at least the activation margin."
			);
		}

		options.Sun.Validate(nameof(options.Sun));
		if (options.Meshing == null)
		{
			throw new ArgumentNullException(nameof(options.Meshing));
		}
	}

	private static void ValidateNonNegativeFinite(float value, string name)
	{
		if (!float.IsFinite(value) || value < 0)
		{
			throw new ArgumentOutOfRangeException(name);
		}
	}

	private static void ValidatePositiveFinite(float value, string name)
	{
		if (!float.IsFinite(value) || value <= 0)
		{
			throw new ArgumentOutOfRangeException(name);
		}
	}

	private static int CompareGpuChunks(GpuChunk left, GpuChunk right)
	{
		return CompareCoordinates(left.Coordinate, right.Coordinate);
	}

	private void InsertOrderedGpuChunk(GpuChunk chunk)
	{
		int index = orderedGpuChunks.BinarySearch(chunk, GpuChunkComparer.Instance);

		if (index < 0)
		{
			index = ~index;
		}

		orderedGpuChunks.Insert(index, chunk);
	}

	private static bool HasShadowCasterGeometry(GpuChunk chunk) =>
		chunk.Opaque?.VertexCount > 0
		|| chunk.Cutout?.VertexCount > 0
		|| chunk.AlphaShadow?.VertexCount > 0;

	private void AddToGpuColumn(GpuChunk chunk)
	{
		GpuColumnCoordinate coordinate = new(chunk.Coordinate.X, chunk.Coordinate.Z);
		if (!gpuChunkColumns.TryGetValue(coordinate, out List<GpuChunk> chunks))
		{
			chunks = new List<GpuChunk>();
			gpuChunkColumns.Add(coordinate, chunks);
		}
		chunks.Add(chunk);
	}

	private void RemoveFromGpuColumn(GpuChunk chunk)
	{
		GpuColumnCoordinate coordinate = new(chunk.Coordinate.X, chunk.Coordinate.Z);
		if (!gpuChunkColumns.TryGetValue(coordinate, out List<GpuChunk> chunks))
		{
			return;
		}

		chunks.Remove(chunk);
		if (chunks.Count == 0)
		{
			gpuChunkColumns.Remove(coordinate);
		}
	}

	internal static int ComparePassEntries(VoxelPassEntry left, VoxelPassEntry right)
	{
		int result = left.Depth.CompareTo(right.Depth);

		return result != 0 ? result : CompareCoordinates(left.Coordinate, right.Coordinate);
	}

	private static int CompareCoordinates(ChunkCoordinate left, ChunkCoordinate right)
	{
		int result = left.X.CompareTo(right.X);

		if (result == 0)
		{
			result = left.Y.CompareTo(right.Y);
		}

		if (result == 0)
		{
			result = left.Z.CompareTo(right.Z);
		}

		return result;
	}

	private void ThrowIfDisposed()
	{
		if (disposed)
		{
			throw new ObjectDisposedException(nameof(VoxelRenderer));
		}
	}

	private sealed class GpuChunk : IDisposable
	{
		internal GpuChunk(ChunkCoordinate coordinate)
		{
			Coordinate = coordinate;
		}

		public ChunkCoordinate Coordinate { get; }
		public long WorldGeneration;
		public long Revision;
		public long LightGeneration;
		public long LightRevision;
		public AxisAlignedBoundingBox Bounds;
		public VoxelGeometryAllocation Opaque;
		public VoxelGeometryAllocation Cutout;
		public VoxelGeometryAllocation AlphaShadow;
		public VoxelTransparentAllocation Transparent;

		public void Dispose()
		{
			Opaque?.ReleaseOwner();
			Cutout?.ReleaseOwner();
			AlphaShadow?.ReleaseOwner();
			Transparent?.ReleaseOwner();
		}
	}

	private readonly record struct CompletedEmptyChunk(
		long WorldGeneration,
		long WorldRevision,
		long LightGeneration,
		long LightRevision);

	private readonly record struct GpuColumnCoordinate(int X, int Z);

	private sealed class GpuChunkComparer : IComparer<GpuChunk>
	{
		internal static readonly GpuChunkComparer Instance = new GpuChunkComparer();

		public int Compare(GpuChunk left, GpuChunk right) => CompareGpuChunks(left, right);
	}

}
