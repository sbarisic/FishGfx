using System;
using System.Collections.Generic;
using System.Numerics;
using FishGfx.Graphics;

namespace FishGfx.Voxels;

public sealed partial class VoxelRenderer : IDisposable
{
	private int BuildTransparentStream(Camera camera)
	{
		EnsureWritableTransparentMesh();
		transparentFaces.Clear();
		Vector3 cameraForward = camera.WorldForwardNormal;

		for (int chunkIndex = 0; chunkIndex < visibleTransparentChunks.Count; chunkIndex++)
		{
			GpuChunk chunk = visibleTransparentChunks[chunkIndex];
			Vector3 origin = chunk.Coordinate.WorldOrigin;

			for (int faceIndex = 0; faceIndex < chunk.TransparentFaces.Length; faceIndex++)
			{
				VoxelTransparentFace face = chunk.TransparentFaces[faceIndex];
				float depth = Vector3.Dot(face.Center + origin - camera.Position, cameraForward);
				transparentFaces.Add(
					new VoxelTransparentFaceInstance(
						chunk.Coordinate,
						faceIndex,
						origin,
						face,
						depth
					)
				);
			}
		}

		int required = VoxelTransparentStreamBuilder.CountVertices(transparentFaces);

		if (transparentVertexBuffer.Length < required)
		{
			Array.Resize(
				ref transparentVertexBuffer,
				VoxelMesh.CalculateCapacity(transparentVertexBuffer.Length, required)
			);
		}

		int vertexCount = VoxelTransparentStreamBuilder.BuildSorted(
			transparentFaces,
			transparentVertexBuffer
		);
		transparentMesh.Update(transparentVertexBuffer, vertexCount);
		visibleTransparentFaces = transparentFaces.Count;
		visibleTransparentVertices = vertexCount;

		return checked(vertexCount * System.Runtime.InteropServices.Marshal.SizeOf<VoxelVertex>());
	}

	private void EnsureWritableTransparentMesh()
	{
		if (!transparentMesh.IsRetained)
		{
			return;
		}

		VoxelMesh previous = transparentMesh;
		VoxelMesh replacement = new VoxelMesh(Graphics, BufferUsage.Stream);
		transparentMesh = replacement;
		previous.Dispose();
	}

	private void ProcessRemovedChunks()
	{
		while (removedChunks.TryDequeue(out ChunkCoordinate coordinate))
		{
			RemoveGpuChunk(coordinate);
		}
	}

	private void RemoveGpuChunk(ChunkCoordinate coordinate)
	{
		if (!gpuChunks.Remove(coordinate, out GpuChunk chunk))
		{
			return;
		}

		opaqueVertices -= chunk.Opaque?.VertexCount ?? 0;
		cutoutVertices -= chunk.Cutout?.VertexCount ?? 0;
		orderedGpuChunks.Remove(chunk);
		activeCoordinates.Remove(coordinate);
		activeGpuChunks.Remove(chunk);
		activeSetDirty = true;
		transparentGeometryRevision++;
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

	private static ulong AddSignature(ulong signature, ChunkCoordinate coordinate)
	{
		signature = AddSignature(signature, coordinate.X);
		signature = AddSignature(signature, coordinate.Y);
		return AddSignature(signature, coordinate.Z);
	}

	private static ulong AddSignature(ulong signature, long value)
	{
		unchecked
		{
			signature ^= (ulong)value;
			return signature * 1099511628211UL;
		}
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
		public long Revision;
		public long LightRevision;
		public AxisAlignedBoundingBox Bounds;
		public VoxelGeometryAllocation Opaque;
		public VoxelGeometryAllocation Cutout;
		public VoxelTransparentFace[] TransparentFaces = Array.Empty<VoxelTransparentFace>();

		public void Dispose()
		{
			Opaque?.ReleaseOwner();
			Cutout?.ReleaseOwner();
		}
	}

	private sealed class GpuChunkComparer : IComparer<GpuChunk>
	{
		internal static readonly GpuChunkComparer Instance = new GpuChunkComparer();

		public int Compare(GpuChunk left, GpuChunk right) => CompareGpuChunks(left, right);
	}

}
