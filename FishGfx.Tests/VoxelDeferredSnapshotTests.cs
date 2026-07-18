using System;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using FishGfx.Graphics;
using FishGfx.Voxels;
using Xunit;

namespace FishGfx.Tests;

public sealed class VoxelDeferredSnapshotTests
{
	[Fact]
	public void IndirectCommandHasTheOpenGlSpecifiedLayout()
	{
		Assert.Equal(16, Marshal.SizeOf<DrawArraysIndirectCommand>());
	}

	[Fact]
	public void IndirectAllocationProducesStableCommandAndCanBeRetained()
	{
		VoxelGeometryPage page = CreateUninitialized<VoxelGeometryPage>();
		VoxelGeometryAllocation allocation = new(page, 36, 72, 48, 9);

		allocation.Retain();
		DrawArraysIndirectCommand command = allocation.CreateDrawCommand();

		Assert.True(allocation.IsRetained);
		Assert.Equal(48u, command.Count);
		Assert.Equal(1u, command.InstanceCount);
		Assert.Equal(36u, command.First);
		Assert.Equal(9u, command.BaseInstance);

		allocation.ReleaseRetained();
		Assert.False(allocation.IsRetained);
	}

	[Fact]
	public void RetainedAllocationSurvivesOwnerReleaseUntilQueueSnapshotEnds()
	{
		VoxelGeometryPage page = CreateDisposedUninitializedPage();
		VoxelGeometryAllocation allocation = new(page, 36, 72, 48, 9);

		allocation.Retain();
		allocation.ReleaseOwner();

		Assert.True(allocation.IsRetained);

		allocation.ReleaseRetained();

		Assert.False(allocation.IsRetained);
		Assert.Throws<ObjectDisposedException>(allocation.Retain);
	}

	[Fact]
	public void QueueClearReleasesAllSnapshotsWhenOneReleaseFails()
	{
		RenderQueue queue = new();
		RenderCommandBatch batch = new(new RenderCommand[] { new NoOpCommand() });
		TrackingDisposable first = new(throwOnDispose: true);
		TrackingDisposable second = new(throwOnDispose: false);

		queue.SubmitRetained(
			RenderQueueBucket.Opaque,
			batch,
			Matrix4x4.Identity,
			first
		);
		queue.SubmitRetained(
			RenderQueueBucket.Transparent,
			batch,
			Matrix4x4.Identity,
			second
		);

		Assert.Throws<AggregateException>(queue.Clear);
		Assert.True(first.IsDisposed);
		Assert.True(second.IsDisposed);
		Assert.Equal(0, queue.Count);
		Assert.Empty(queue.Buckets);
	}

	private static T CreateUninitialized<T>()
		where T : class
	{
		return (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
	}

	private static VoxelGeometryPage CreateDisposedUninitializedPage()
	{
		VoxelGeometryPage page = CreateUninitialized<VoxelGeometryPage>();
		FieldInfo sync = typeof(VoxelGeometryPage).GetField(
			"sync",
			BindingFlags.Instance | BindingFlags.NonPublic
		);
		FieldInfo disposed = typeof(VoxelGeometryPage).GetField(
			"disposed",
			BindingFlags.Instance | BindingFlags.NonPublic
		);
		sync?.SetValue(page, new object());
		disposed?.SetValue(page, true);

		return page;
	}

	private sealed class NoOpCommand : RenderCommand
	{
		public override void Execute(RenderPass pass)
		{
		}
	}

	private sealed class TrackingDisposable : IDisposable
	{
		private readonly bool throwOnDispose;

		internal TrackingDisposable(bool throwOnDispose)
		{
			this.throwOnDispose = throwOnDispose;
		}

		internal bool IsDisposed { get; private set; }

		public void Dispose()
		{
			IsDisposed = true;

			if (throwOnDispose)
			{
				throw new InvalidOperationException("Expected test failure.");
			}
		}
	}
}
