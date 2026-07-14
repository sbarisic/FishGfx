using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using FishGfx.Graphics;
using FishGfx.Voxels;
using Xunit;

namespace FishGfx.Tests;

public sealed class VoxelDeferredSnapshotTests
{
	[Fact]
	public void PassCommandsSnapshotEachEnqueueAndSurviveMeshRemoval()
	{
		VoxelMesh firstMesh = CreateUninitialized<VoxelMesh>();
		VoxelMesh secondMesh = CreateUninitialized<VoxelMesh>();
		Texture atlas = CreateUninitialized<Texture>();
		ShaderProgram shader = CreateUninitialized<ShaderProgram>();
		Matrix4x4 firstModel = Matrix4x4.CreateTranslation(1, 2, 3);
		Matrix4x4 secondModel = Matrix4x4.CreateTranslation(7, 8, 9);
		ChunkCoordinate firstCoordinate = new(1, 2, 3);
		ChunkCoordinate secondCoordinate = new(7, 8, 9);
		List<VoxelPassEntry> visible = new()
		{
			new VoxelPassEntry(firstMesh, firstModel, firstCoordinate, depth: 1),
		};
		DrawVoxelPassCommand first = CreateCommand(atlas, shader, visible);

		visible[0] = new VoxelPassEntry(
			secondMesh,
			secondModel,
			secondCoordinate,
			depth: 2
		);
		DrawVoxelPassCommand second = CreateCommand(atlas, shader, visible);
		firstMesh.Dispose();

		AssertSnapshot(first, firstModel, firstCoordinate);
		AssertSnapshot(second, secondModel, secondCoordinate);
		Assert.True(firstMesh.IsRetained);
		Assert.True(secondMesh.IsRetained);

		second.Dispose();
		secondMesh.Dispose();
		first.Dispose();

		Assert.False(firstMesh.IsRetained);
		Assert.False(secondMesh.IsRetained);
	}

	[Fact]
	public void RetainedReferenceKeepsStorageAliveAfterOwnerRelease()
	{
		VoxelMesh mesh = CreateUninitialized<VoxelMesh>();
		mesh.RetainReference();

		mesh.Dispose();

		Assert.True(mesh.IsRetained);
		Assert.Equal(0, mesh.VertexCount);

		mesh.ReleaseReference();

		Assert.False(mesh.IsRetained);
		Assert.Throws<ObjectDisposedException>(mesh.RetainReference);
	}

	[Fact]
	public void AbandonedQueueFinalizerReleasesSnapshotReferences()
	{
		VoxelMesh mesh = CreateUninitialized<VoxelMesh>();
		Texture atlas = CreateUninitialized<Texture>();
		ShaderProgram shader = CreateUninitialized<ShaderProgram>();
		WeakReference queueReference = EnqueueAbandonedSnapshot(mesh, atlas, shader);

		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();

		Assert.False(queueReference.IsAlive);
		Assert.False(mesh.IsRetained);
		mesh.Dispose();
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

	private static DrawVoxelPassCommand CreateCommand(
		Texture atlas,
		ShaderProgram shader,
		IReadOnlyList<VoxelPassEntry> entries
	)
	{
		VoxelSunSettings sun = new(-Vector3.UnitY, Color.White, 1, 0.2f);

		return new DrawVoxelPassCommand(
			atlas,
			shader,
			RenderState.Default,
			sun,
			alphaCutoff: -1,
			VoxelFogSettings.Disabled,
			entries
		);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static WeakReference EnqueueAbandonedSnapshot(
		VoxelMesh mesh,
		Texture atlas,
		ShaderProgram shader
	)
	{
		List<VoxelPassEntry> entries = new()
		{
			new VoxelPassEntry(
				mesh,
				Matrix4x4.Identity,
				new ChunkCoordinate(0, 0, 0),
				depth: 0
			),
		};
		DrawVoxelPassCommand command = CreateCommand(atlas, shader, entries);
		RenderCommandBatch batch = new(new RenderCommand[] { command });
		RenderQueue queue = new();
		queue.SubmitRetained(
			RenderQueueBucket.Opaque,
			batch,
			Matrix4x4.Identity,
			command
		);

		return new WeakReference(queue);
	}

	private static void AssertSnapshot(
		DrawVoxelPassCommand command,
		Matrix4x4 expectedModel,
		ChunkCoordinate expectedCoordinate
	)
	{
		FieldInfo entriesField = typeof(DrawVoxelPassCommand).GetField(
			"entries",
			BindingFlags.Instance | BindingFlags.NonPublic
		);
		Array entries = Assert.IsAssignableFrom<Array>(entriesField?.GetValue(command));
		object entry = Assert.Single(entries.Cast<object>());
		Type entryType = entry.GetType();
		PropertyInfo modelProperty = entryType.GetProperty(
			"Model",
			BindingFlags.Instance | BindingFlags.NonPublic
		);
		PropertyInfo coordinateProperty = entryType.GetProperty(
			"Coordinate",
			BindingFlags.Instance | BindingFlags.NonPublic
		);

		Assert.Equal(expectedModel, modelProperty?.GetValue(entry));
		Assert.Equal(expectedCoordinate, coordinateProperty?.GetValue(entry));
	}

	private static T CreateUninitialized<T>()
		where T : class
	{
		return (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
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
