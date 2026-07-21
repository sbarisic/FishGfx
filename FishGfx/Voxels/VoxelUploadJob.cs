using System;
using System.Buffers;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;

namespace FishGfx.Voxels;

internal sealed class VoxelUploadJob : IDisposable
{
	private readonly VoxelGeometryPagePool opaquePool;
	private readonly VoxelGeometryPagePool cutoutPool;
	private readonly VoxelGeometryPagePool alphaShadowPool;
	private readonly VoxelTransparentGeometryStore transparentStore;
	private readonly VoxelTransparentFaceRecord[] transparentRecords;
	private VoxelVertex[] transparentSlice;
	private int stream;
	private int streamOffset;
	private int transparentFaceIndex;
	private int transparentFaceVertexIndex;
	private bool detached;
	private bool disposed;

	internal VoxelUploadJob(
		VoxelMeshData result,
		VoxelGeometryPagePool opaquePool,
		VoxelGeometryPagePool cutoutPool,
		VoxelGeometryPagePool alphaShadowPool,
		VoxelTransparentGeometryStore transparentStore,
		int maximumSliceBytes)
	{
		Result = result ?? throw new ArgumentNullException(nameof(result));
		this.opaquePool = opaquePool;
		this.cutoutPool = cutoutPool;
		this.alphaShadowPool = alphaShadowPool;
		this.transparentStore = transparentStore;
		int stride = Marshal.SizeOf<VoxelVertex>();
		MaximumSliceVertices = Math.Max(1, maximumSliceBytes / stride);
		transparentRecords = Array.Empty<VoxelTransparentFaceRecord>();
		try
		{
			Opaque = opaquePool.Reserve(result.OpaqueVertexCount, result.Coordinate.WorldOrigin);
			Cutout = cutoutPool.Reserve(result.CutoutVertexCount, result.Coordinate.WorldOrigin);
			AlphaShadow = alphaShadowPool.Reserve(result.AlphaShadowVertexCount, result.Coordinate.WorldOrigin);
			int transparentVertices = CountTransparentVertices(result.TransparentFaces);
			Transparent = transparentStore.Reserve(transparentVertices);
			transparentRecords = CreateTransparentRecords(
				result.TransparentFaces,
				result.Coordinate,
				result.Coordinate.WorldOrigin,
				Transparent
			);
			if (transparentVertices > 0)
				transparentSlice = ArrayPool<VoxelVertex>.Shared.Rent(MaximumSliceVertices);
			TotalBytes = checked((long)(
				result.OpaqueVertexCount
				+ result.CutoutVertexCount
				+ result.AlphaShadowVertexCount
				+ transparentVertices) * stride);
		}
		catch
		{
			Opaque?.ReleaseOwner();
			Cutout?.ReleaseOwner();
			AlphaShadow?.ReleaseOwner();
			Transparent?.ReleaseOwner();
			if (transparentSlice != null)
			{
				ArrayPool<VoxelVertex>.Shared.Return(transparentSlice);
				transparentSlice = null;
			}
			result.ReleasePooledVertexBuffers();
			throw;
		}
		StartedTimestamp = Stopwatch.GetTimestamp();
	}

	internal VoxelMeshData Result { get; }
	internal VoxelGeometryAllocation Opaque { get; private set; }
	internal VoxelGeometryAllocation Cutout { get; private set; }
	internal VoxelGeometryAllocation AlphaShadow { get; private set; }
	internal VoxelTransparentAllocation Transparent { get; private set; }
	internal int MaximumSliceVertices { get; }
	internal long TotalBytes { get; }
	internal long UploadedBytes { get; private set; }
	internal long RemainingBytes => TotalBytes - UploadedBytes;
	internal long StartedTimestamp { get; }
	internal int SliceCount { get; private set; }
	internal bool IsComplete => stream >= 4;

	internal int UploadNextSlice(int maximumBytes)
	{
		ObjectDisposedException.ThrowIf(disposed, this);
		if (IsComplete || maximumBytes <= 0)
			return 0;
		int stride = Marshal.SizeOf<VoxelVertex>();
		int maximumVertices = Math.Max(1, Math.Min(MaximumSliceVertices, maximumBytes / stride));
		int uploadedVertices = stream switch
		{
			0 => UploadGeometrySlice(opaquePool, Opaque, Result.OpaqueVertexSpan, maximumVertices),
			1 => UploadGeometrySlice(cutoutPool, Cutout, Result.CutoutVertexSpan, maximumVertices),
			2 => UploadGeometrySlice(alphaShadowPool, AlphaShadow, Result.AlphaShadowVertexSpan, maximumVertices),
			_ => UploadTransparentSlice(maximumVertices),
		};
		if (uploadedVertices == 0)
		{
			CompleteCurrentStream();
			return UploadNextSlice(maximumBytes);
		}

		int bytes = checked(uploadedVertices * stride);
		UploadedBytes += bytes;
		SliceCount++;
		if (CurrentStreamLength() == streamOffset)
			CompleteCurrentStream();
		return bytes;
	}

	internal void DetachAllocations()
	{
		if (!IsComplete)
			throw new InvalidOperationException("An incomplete upload cannot be published.");
		detached = true;
	}

	public void Dispose()
	{
		if (disposed)
			return;
		disposed = true;
		if (!detached)
		{
			Opaque?.ReleaseOwner();
			Cutout?.ReleaseOwner();
			AlphaShadow?.ReleaseOwner();
			Transparent?.ReleaseOwner();
		}
		if (transparentSlice != null)
		{
			ArrayPool<VoxelVertex>.Shared.Return(transparentSlice);
			transparentSlice = null;
		}
		Result.ReleasePooledVertexBuffers();
	}

	private int UploadGeometrySlice(
		VoxelGeometryPagePool pool,
		VoxelGeometryAllocation allocation,
		ReadOnlySpan<VoxelVertex> source,
		int maximumVertices)
	{
		if (source.Length == 0)
			return 0;
		int count = Math.Min(maximumVertices, source.Length - streamOffset);
		VoxelGeometryPagePool.WriteSlice(
			allocation,
			source.Slice(streamOffset, count),
			streamOffset
		);
		streamOffset += count;
		return count;
	}

	private int UploadTransparentSlice(int maximumVertices)
	{
		if (Transparent == null)
			return 0;
		int count = 0;
		Vector3 origin = Result.Coordinate.WorldOrigin;
		while (count < maximumVertices
			&& transparentFaceIndex < Result.TransparentFaces.Length)
		{
			VoxelVertex[] face = Result.TransparentFaces[transparentFaceIndex].VertexArray;
			while (count < maximumVertices && transparentFaceVertexIndex < face.Length)
			{
				VoxelVertex vertex = face[transparentFaceVertexIndex++];
				vertex.Position += origin;
				transparentSlice[count++] = vertex;
			}
			if (transparentFaceVertexIndex == face.Length)
			{
				transparentFaceIndex++;
				transparentFaceVertexIndex = 0;
			}
		}
		if (count > 0)
		{
			transparentStore.WriteSlice(
				Transparent,
				transparentSlice.AsSpan(0, count),
				streamOffset
			);
			streamOffset += count;
		}
		return count;
	}

	private void CompleteCurrentStream()
	{
		switch (stream)
		{
			case 0:
				VoxelGeometryPagePool.Complete(Opaque, Result.OpaqueVertexCount);
				break;
			case 1:
				VoxelGeometryPagePool.Complete(Cutout, Result.CutoutVertexCount);
				break;
			case 2:
				VoxelGeometryPagePool.Complete(AlphaShadow, Result.AlphaShadowVertexCount);
				break;
			case 3:
				VoxelTransparentGeometryStore.Complete(
					Transparent,
					streamOffset,
					transparentRecords
				);
				break;
		}
		stream++;
		streamOffset = 0;
	}

	private int CurrentStreamLength() => stream switch
	{
		0 => Result.OpaqueVertexCount,
		1 => Result.CutoutVertexCount,
		2 => Result.AlphaShadowVertexCount,
		3 => Transparent?.Capacity >= 0 ? CountTransparentVertices(Result.TransparentFaces) : 0,
		_ => 0,
	};

	private static int CountTransparentVertices(VoxelTransparentFace[] faces)
	{
		int count = 0;
		for (int index = 0; index < faces.Length; index++)
			count = checked(count + faces[index].VertexArray.Length);
		return count;
	}

	private static VoxelTransparentFaceRecord[] CreateTransparentRecords(
		VoxelTransparentFace[] faces,
		ChunkCoordinate coordinate,
		Vector3 origin,
		VoxelTransparentAllocation allocation)
	{
		if (allocation == null)
			return Array.Empty<VoxelTransparentFaceRecord>();
		VoxelTransparentFaceRecord[] records = new VoxelTransparentFaceRecord[faces.Length];
		int vertexOffset = 0;
		for (int index = 0; index < faces.Length; index++)
		{
			VoxelTransparentFace face = faces[index];
			records[index] = new VoxelTransparentFaceRecord(
				face.Center + origin,
				checked((uint)(allocation.FirstVertex + vertexOffset)),
				face.VertexArray.Length,
				coordinate,
				index
			);
			vertexOffset += face.VertexArray.Length;
		}
		return records;
	}
}
