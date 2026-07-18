using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using FishGfx.Graphics;
using FishGfx.Voxels;
using Xunit;

namespace FishGfx.Tests;

public sealed class VoxelTransparentOrderingTests
{
	[Fact]
	public void OrderingProducesExactGlobalBackToFrontIndicesWithStableTies()
	{
		VoxelTransparentAllocation allocation = CreateAllocation(
			new VoxelTransparentFaceRecord(
				new Vector3(0, 0, -5),
				20,
				2,
				new ChunkCoordinate(1, 0, 0),
				0
			),
			new VoxelTransparentFaceRecord(
				new Vector3(0, 0, -10),
				100,
				3,
				new ChunkCoordinate(-1, 0, 0),
				4
			),
			new VoxelTransparentFaceRecord(
				new Vector3(0, 0, -5),
				10,
				1,
				new ChunkCoordinate(0, 0, 0),
				2
			)
		);
		VoxelTransparentOrderingSource source = CreateSource(allocation);

		try
		{
			using VoxelTransparentOrderingResult result = VoxelTransparentOrderingScheduler.Build(
				CreateRequest(source, cullingEnabled: false),
				TestContext.Current.CancellationToken
			);

			Assert.Equal(3, result.FaceCount);
			Assert.Equal(6, result.IndexCount);
			Assert.Equal(
				new uint[] { 100, 101, 102, 10, 20, 21 },
				result.Indices.ToArray()
			);
		}
		finally
		{
			source.ReleaseOwner();
		}
	}

	[Fact]
	public void OrderingCullsChunksByFrustumAndDistance()
	{
		Camera camera = CreateCamera();
		VoxelTransparentAllocation visible = CreateAllocation(
			new VoxelTransparentFaceRecord(
				new Vector3(0, 0, -5),
				0,
				3,
				new ChunkCoordinate(0, 0, -1),
				0
			)
		);
		VoxelTransparentAllocation outside = CreateAllocation(
			new VoxelTransparentFaceRecord(
				new Vector3(100, 0, -5),
				10,
				3,
				new ChunkCoordinate(6, 0, -1),
				0
			)
		);
		VoxelTransparentOrderingSource source = new(
			new[]
			{
				new VoxelTransparentOrderingChunk(
					AxisAlignedBoundingBox.FromPositionAndSize(
						new Vector3(-1, -1, -6),
						new Vector3(2)
					),
					visible
				),
				new VoxelTransparentOrderingChunk(
					AxisAlignedBoundingBox.FromPositionAndSize(
						new Vector3(99, -1, -6),
						new Vector3(2)
					),
					outside
				),
			},
			geometryRevision: 1,
			activeSetGeneration: 1
		);

		try
		{
			VoxelTransparentOrderingRequest request = new(
				source,
				ViewFrustum.FromCamera(camera),
				camera.Position,
				camera.WorldForwardNormal,
				50,
				true,
				VoxelTransparentInvalidationReason.FirstFrame,
				1
			);
			using VoxelTransparentOrderingResult result = VoxelTransparentOrderingScheduler.Build(
				request,
				TestContext.Current.CancellationToken
			);

			Assert.Equal(1, result.VisibleChunkCount);
			Assert.Equal(new uint[] { 0, 1, 2 }, result.Indices.ToArray());
		}
		finally
		{
			source.ReleaseOwner();
		}
	}

	[Fact]
	public void WarmOrderingDoesNotAllocatePerFace()
	{
		const int faceCount = 10_000;
		VoxelTransparentFaceRecord[] records = new VoxelTransparentFaceRecord[faceCount];

		for (int index = 0; index < records.Length; index++)
		{
			records[index] = new VoxelTransparentFaceRecord(
				new Vector3(0, 0, -index - 1),
				checked((uint)(index * 6)),
				6,
				new ChunkCoordinate(index % 8, 0, -index / 8),
				index
			);
		}

		VoxelTransparentOrderingSource source = CreateSource(CreateAllocation(records));

		try
		{
			VoxelTransparentOrderingRequest request = CreateRequest(source, cullingEnabled: false);

			using (VoxelTransparentOrderingResult warm = VoxelTransparentOrderingScheduler.Build(
				request,
				TestContext.Current.CancellationToken
			))
			{
			}

			using VoxelTransparentOrderingResult measured = VoxelTransparentOrderingScheduler.Build(
				request,
				TestContext.Current.CancellationToken
			);
			Assert.Equal(faceCount, measured.FaceCount);
			Assert.True(
				measured.WorkerAllocatedBytes < 4096,
				$"Ordering allocated {measured.WorkerAllocatedBytes} bytes after warm-up."
			);
		}
		finally
		{
			source.ReleaseOwner();
		}
	}

	[Fact]
	public void SchedulerCoalescesCameraRequestsAndKeepsReleasedSourceAlive()
	{
		VoxelTransparentFaceRecord[] records = new VoxelTransparentFaceRecord[20_000];

		for (int index = 0; index < records.Length; index++)
		{
			records[index] = new VoxelTransparentFaceRecord(
				new Vector3(0, 0, -index),
				checked((uint)index),
				1,
				new ChunkCoordinate(index % 4, 0, -index / 4),
				index
			);
		}

		VoxelTransparentOrderingSource source = CreateSource(CreateAllocation(records));
		using VoxelTransparentOrderingScheduler scheduler = new();

		for (int sequence = 1; sequence <= 100; sequence++)
		{
			scheduler.Request(CreateRequest(source, cullingEnabled: false, sequence));
		}

		source.ReleaseOwner();
		VoxelTransparentOrderingResult latest = null;
		bool receivedLatest = SpinWait.SpinUntil(
			() =>
			{
				while (scheduler.TryTakeCompleted(out VoxelTransparentOrderingResult completed))
				{
					latest?.Dispose();
					latest = completed;
				}

				return latest?.RequestSequence == 100;
			},
			TimeSpan.FromSeconds(5)
		);

		try
		{
			Assert.True(receivedLatest);
			Assert.True(scheduler.CoalescedRequests > 0);
			Assert.False(scheduler.TryTakeFailure(out _));
		}
		finally
		{
			latest?.Dispose();
		}
	}

	[Fact]
	public void OrderingHonorsCancellationBeforePublishingAResult()
	{
		VoxelTransparentOrderingSource source = CreateSource(
			CreateAllocation(
				new VoxelTransparentFaceRecord(
					new Vector3(0, 0, -1),
					0,
					6,
					new ChunkCoordinate(0, 0, -1),
					0
				)
			)
		);
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();

		try
		{
			Assert.Throws<OperationCanceledException>(
				() => VoxelTransparentOrderingScheduler.Build(
					CreateRequest(source, cullingEnabled: false),
					cancellation.Token
				)
			);
		}
		finally
		{
			source.ReleaseOwner();
		}
	}

	[Fact]
	public void VertexRangeAllocatorCoalescesAndGrowsWithoutMovingAllocations()
	{
		VoxelVertexRangeAllocator allocator = new(capacity: 60, alignment: 6);

		Assert.True(allocator.TryAllocate(12, out int first));
		Assert.Equal(0, first);
		Assert.True(allocator.TryAllocate(18, out int second));
		Assert.Equal(12, second);
		allocator.Free(first, 12);
		allocator.Free(second, 18);
		Assert.True(allocator.TryAllocate(60, out int coalesced));
		Assert.Equal(0, coalesced);
		allocator.Grow(120);
		Assert.True(allocator.TryAllocate(60, out int grown));
		Assert.Equal(60, grown);
	}

	[Fact]
	public void GraphicsBufferExposesSameCapacityDiscardOperation()
	{
		Assert.NotNull(typeof(GraphicsBuffer).GetMethod(nameof(GraphicsBuffer.DiscardContents)));
	}

	private static VoxelTransparentOrderingSource CreateSource(
		VoxelTransparentAllocation allocation
	)
	{
		return new VoxelTransparentOrderingSource(
			new[]
			{
				new VoxelTransparentOrderingChunk(
					AxisAlignedBoundingBox.FromPositionAndSize(
						new Vector3(-1, -1, -20_002),
						new Vector3(2, 2, 20_004)
					),
					allocation
				),
			},
			geometryRevision: 1,
			activeSetGeneration: 1
		);
	}

	private static VoxelTransparentAllocation CreateAllocation(
		params VoxelTransparentFaceRecord[] records
	)
	{
		VoxelTransparentGeometryStore store = (VoxelTransparentGeometryStore)
			RuntimeHelpers.GetUninitializedObject(typeof(VoxelTransparentGeometryStore));
		int vertexCount = 0;

		for (int index = 0; index < records.Length; index++)
		{
			vertexCount = checked(vertexCount + records[index].VertexCount);
		}

		VoxelTransparentAllocation allocation = new(
			store,
			firstVertex: 0,
			capacity: Math.Max(6, vertexCount)
		);
		allocation.SetGeometry(vertexCount, records);
		return allocation;
	}

	private static VoxelTransparentOrderingRequest CreateRequest(
		VoxelTransparentOrderingSource source,
		bool cullingEnabled,
		long sequence = 1
	)
	{
		Camera camera = CreateCamera();

		return new VoxelTransparentOrderingRequest(
			source,
			ViewFrustum.FromCamera(camera),
			camera.Position,
			camera.WorldForwardNormal,
			50_000,
			cullingEnabled,
			VoxelTransparentInvalidationReason.Translation,
			sequence
		);
	}

	private static Camera CreateCamera()
	{
		Camera camera = new()
		{
			Position = Vector3.Zero,
		};
		camera.SetPerspective(1280, 720, MathF.PI / 2, 0.1f, 50_000);
		return camera;
	}
}
