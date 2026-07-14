using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using FishGfx.Graphics;
using FishGfx.Graphics.Drawables;
using Xunit;

namespace FishGfx.Tests;

public class RenderQueueTests
{
	[Fact]
	public void BatchSnapshotsCommandsAndGuardsRecursiveReplay()
	{
		RenderCommandList list = CreateCommandList();
		RenderCommand command = list[0];
		RenderCommandBatch batch = list.Snapshot();

		list.Clear();
		list.Add(new TestCommand());

		Assert.Single(batch.Commands);
		Assert.Same(command, batch[0]);

		batch.BeginExecution();

		try
		{
			InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
				batch.BeginExecution
			);

			Assert.Contains("recursively", exception.Message, StringComparison.OrdinalIgnoreCase);
			Assert.True(batch.IsExecuting);
		}
		finally
		{
			batch.EndExecution();
		}

		Assert.False(batch.IsExecuting);
	}

	[Fact]
	public void BucketsUseCaseSensitiveNamesAndSupportCustomPasses()
	{
		RenderQueueBucket shadow = new("Shadow");

		Assert.Equal(new RenderQueueBucket("Shadow"), shadow);
		Assert.NotEqual(new RenderQueueBucket("shadow"), shadow);
		Assert.NotEqual(RenderQueueBucket.Opaque, RenderQueueBucket.Transparent);
		Assert.Equal("Shadow", shadow.ToString());
		Assert.Throws<ArgumentException>(() => new RenderQueueBucket(" "));
	}

	[Fact]
	public void QueueQueriesBucketsAndRetainsItemsUntilCleared()
	{
		RenderQueue queue = new();
		RenderCommandList commands = CreateCommandList();
		RenderQueueBucket selection = new("Selection");

		RenderItem opaque = queue.SubmitOpaque(commands, Matrix4x4.Identity, tag: "opaque");
		RenderItem custom = queue.Submit(selection, commands, Matrix4x4.Identity, tag: "custom");

		Assert.Equal(2, queue.Count);
		Assert.Equal(new[] { RenderQueueBucket.Opaque, selection }, queue.Buckets);
		Assert.Same(opaque, Assert.Single(queue.Query(RenderQueueBucket.Opaque)));
		Assert.Same(custom, Assert.Single(queue.Query(selection)));
		Assert.Empty(queue.Query(RenderQueueBucket.Transparent));

		queue.BeginFrame();

		Assert.Equal(0, queue.Count);
		Assert.Empty(queue.Buckets);
		Assert.Equal(0, queue.SubmitOpaque(commands, Matrix4x4.Identity).Sequence);
	}

	[Fact]
	public void ItemSnapshotsModelMetadataAndCommandReferences()
	{
		RenderQueue queue = new();
		RenderCommandList commands = CreateCommandList();
		Matrix4x4 model = Matrix4x4.CreateTranslation(10, 20, 30);
		object tag = new();

		RenderItem item = queue.SubmitTransparent(
			commands,
			model,
			layer: 4,
			sortKey: 99,
			tag: tag
		);
		commands.Clear();

		Assert.Equal(model, item.Model);
		Assert.Equal(new Vector3(10, 20, 30), item.SortPosition);
		Assert.Equal(4, item.Layer);
		Assert.Equal((ulong)99, item.SortKey);
		Assert.Same(tag, item.Tag);
		Assert.Single(item.Batch.Commands);
	}

	[Fact]
	public void OpaqueFrontToBackUsesLayerDepthKeyAndStableSequence()
	{
		RenderQueue queue = new();
		RenderCommandList commands = CreateCommandList();
		Camera camera = CreateCamera();

		RenderItem far = queue.SubmitOpaque(
			commands,
			Matrix4x4.Identity,
			new Vector3(0, 0, -20),
			sortKey: 5
		);
		RenderItem nearKey2 = queue.SubmitOpaque(
			commands,
			Matrix4x4.Identity,
			new Vector3(0, 0, -2),
			sortKey: 2
		);
		RenderItem nearKey1 = queue.SubmitOpaque(
			commands,
			Matrix4x4.Identity,
			new Vector3(0, 0, -2),
			sortKey: 1
		);
		RenderItem forcedLayer = queue.SubmitOpaque(
			commands,
			Matrix4x4.Identity,
			new Vector3(0, 0, -100),
			layer: -1
		);

		IReadOnlyList<RenderItem> sorted = queue.GetSorted(
			RenderQueueBucket.Opaque,
			RenderItemComparers.OpaqueFrontToBack(camera)
		);

		Assert.Equal(new[] { forcedLayer, nearKey1, nearKey2, far }, sorted);
		Assert.Equal(
			new[] { far, nearKey2, nearKey1, forcedLayer },
			queue.Query(RenderQueueBucket.Opaque)
		);
	}

	[Fact]
	public void OpaqueStateFirstGroupsSortKeysBeforeDepth()
	{
		RenderQueue queue = new();
		RenderCommandList commands = CreateCommandList();
		Camera camera = CreateCamera();

		RenderItem nearKey9 = queue.SubmitOpaque(
			commands,
			Matrix4x4.Identity,
			new Vector3(0, 0, -1),
			sortKey: 9
		);
		RenderItem farKey1 = queue.SubmitOpaque(
			commands,
			Matrix4x4.Identity,
			new Vector3(0, 0, -10),
			sortKey: 1
		);

		IReadOnlyList<RenderItem> sorted = queue.GetSorted(
			RenderQueueBucket.Opaque,
			RenderItemComparers.OpaqueStateThenFrontToBack(camera)
		);

		Assert.Equal(new[] { farKey1, nearKey9 }, sorted);
	}

	[Fact]
	public void TransparentSortingIsBackToFrontAndStableForTies()
	{
		RenderQueue queue = new();
		RenderCommandList commands = CreateCommandList();
		Camera camera = CreateCamera();

		RenderItem near = queue.SubmitTransparent(
			commands,
			Matrix4x4.Identity,
			new Vector3(0, 0, -2)
		);
		RenderItem farFirst = queue.SubmitTransparent(
			commands,
			Matrix4x4.Identity,
			new Vector3(0, 0, -12)
		);
		RenderItem farSecond = queue.SubmitTransparent(
			commands,
			Matrix4x4.Identity,
			new Vector3(0, 0, -12)
		);

		IReadOnlyList<RenderItem> sorted = queue.GetSorted(
			RenderQueueBucket.Transparent,
			RenderItemComparers.TransparentBackToFront(camera)
		);

		Assert.Equal(new[] { farFirst, farSecond, near }, sorted);
	}

	[Fact]
	public void SupportsCustomComparersWithoutMutatingStoredOrder()
	{
		RenderQueue queue = new();
		RenderCommandList commands = CreateCommandList();
		RenderItem first = queue.SubmitOpaque(commands, Matrix4x4.Identity, sortKey: 1);
		RenderItem second = queue.SubmitOpaque(commands, Matrix4x4.Identity, sortKey: 2);
		IComparer<RenderItem> descendingKey = Comparer<RenderItem>.Create(
			(left, right) => right.SortKey.CompareTo(left.SortKey)
		);

		Assert.Equal(
			new[] { second, first },
			queue.GetSorted(RenderQueueBucket.Opaque, descendingKey)
		);
		Assert.Equal(new[] { first, second }, queue.Query(RenderQueueBucket.Opaque));
	}

	[Fact]
	public void QueueRejectsMutationReentrancyAndComparerMutationDuringReplay()
	{
		RenderQueue queue = new();
		queue.SubmitOpaque(CreateCommandList(), Matrix4x4.Identity);
		queue.SubmitOpaque(CreateCommandList(), Matrix4x4.Identity);
		IComparer<RenderItem> mutatingComparer = Comparer<RenderItem>.Create((_, _) =>
		{
			queue.Clear();

			return 0;
		});

		queue.BeginExecution();

		try
		{
			Assert.Throws<InvalidOperationException>(queue.Clear);
			Assert.Throws<InvalidOperationException>(queue.BeginExecution);
			Assert.Throws<InvalidOperationException>(
				() => queue.GetSorted(RenderQueueBucket.Opaque, mutatingComparer)
			);
			Assert.True(queue.IsExecuting);
			Assert.Equal(2, queue.Count);
		}
		finally
		{
			queue.EndExecution();
		}

		Assert.False(queue.IsExecuting);
	}

	[Fact]
	public void RejectsInvalidItemsAndComparers()
	{
		RenderQueue queue = new();
		RenderCommandList empty = new();
		RenderCommandList commands = CreateCommandList();
		Matrix4x4 invalidMatrix = Matrix4x4.Identity;
		invalidMatrix.M22 = float.NaN;

		Assert.Throws<ArgumentNullException>(
			() => queue.SubmitOpaque((RenderCommandList)null, Matrix4x4.Identity)
		);
		Assert.Throws<ArgumentNullException>(
			() => queue.SubmitOpaque((RenderCommandBatch)null, Matrix4x4.Identity)
		);
		Assert.Throws<ArgumentException>(() => queue.SubmitOpaque(empty, Matrix4x4.Identity));
		Assert.Throws<ArgumentException>(() => queue.Submit(default, commands, Matrix4x4.Identity));
		Assert.Throws<ArgumentOutOfRangeException>(
			() => queue.SubmitOpaque(commands, invalidMatrix)
		);
		Assert.Throws<ArgumentOutOfRangeException>(
			() => queue.SubmitOpaque(
				commands,
				Matrix4x4.Identity,
				new Vector3(float.PositiveInfinity, 0, 0)
			)
		);
		Assert.Throws<ArgumentNullException>(
			() => queue.GetSorted(RenderQueueBucket.Opaque, null)
		);
		Assert.Throws<ArgumentNullException>(() => new RenderCommandBatch(null));
		Assert.Throws<ArgumentException>(
			() => new RenderCommandBatch(new RenderCommand[] { new TestCommand(), null })
		);
	}

	[Fact]
	public void TypedDrawableCommandsRetainResourcesAndRejectNullDrawables()
	{
		Mesh3D mesh = (Mesh3D)RuntimeHelpers.GetUninitializedObject(typeof(Mesh3D));
		RenderModel model = (RenderModel)RuntimeHelpers.GetUninitializedObject(typeof(RenderModel));
		RenderCommandList commands = new();

		DrawMeshCommand meshCommand = commands.RecordDrawMesh(mesh);
		DrawModelCommand modelCommand = commands.RecordDrawModel(model);

		Assert.Same(mesh, meshCommand.Mesh);
		Assert.Same(model, modelCommand.Model);
		Assert.IsType<DrawMeshCommand>(commands[0]);
		Assert.IsType<DrawModelCommand>(commands[1]);
		Assert.Throws<ArgumentNullException>(() => new DrawMeshCommand(null));
		Assert.Throws<ArgumentNullException>(() => new DrawModelCommand(null));
	}

	private static RenderCommandList CreateCommandList()
	{
		RenderCommandList commands = new();
		commands.Add(new TestCommand());

		return commands;
	}

	private static Camera CreateCamera()
	{
		Camera camera = new();
		camera.Position = Vector3.Zero;
		camera.Rotation = Quaternion.Identity;

		return camera;
	}

	private sealed class TestCommand : RenderCommand
	{
		public override void Execute(RenderPass pass)
		{
		}
	}
}
