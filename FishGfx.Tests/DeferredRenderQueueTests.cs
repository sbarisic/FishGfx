using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using FishGfx.Graphics;
using FishGfx.Graphics.Drawables;
using Xunit;

namespace FishGfx.Tests;

public class DeferredRenderQueueTests
{
	[Fact]
	public void BatchSnapshotsCommandsAndReplaysRepeatedly()
	{
		List<int> calls = new();
		CommandList list = new();
		list.Add(new TestCommand(() => calls.Add(1)));
		GraphicsCommandBatch batch = list.Snapshot();

		list.Clear();
		list.Add(new TestCommand(() => calls.Add(2)));
		batch.Execute();
		batch.Execute();

		Assert.Equal(new[] { 1, 1 }, calls);
		Assert.Single(batch.Commands);
		Assert.False(batch.IsExecuting);
	}

	[Fact]
	public void BatchStopsOnFailureAndRejectsReentrancy()
	{
		int laterCalls = 0;
		GraphicsCommandBatch failing = new GraphicsCommandBatch(
			new GraphicsCommand[]
			{
				new TestCommand(() => throw new TestException()),
				new TestCommand(() => laterCalls++),
			}
		);

		Assert.Throws<TestException>(failing.Execute);
		Assert.Equal(0, laterCalls);
		Assert.False(failing.IsExecuting);

		GraphicsCommandBatch recursive = null;
		recursive = new GraphicsCommandBatch(new[] { new TestCommand(() => recursive.Execute()) });
		Assert.Throws<InvalidOperationException>(recursive.Execute);
		Assert.False(recursive.IsExecuting);
	}

	[Fact]
	public void BucketsUseCaseSensitiveNamesAndSupportCustomPasses()
	{
		RenderBucket shadow = new RenderBucket("Shadow");

		Assert.Equal(new RenderBucket("Shadow"), shadow);
		Assert.NotEqual(new RenderBucket("shadow"), shadow);
		Assert.NotEqual(RenderBucket.Opaque, RenderBucket.Transparent);
		Assert.Equal("Shadow", shadow.ToString());
		Assert.Throws<ArgumentException>(() => new RenderBucket(" "));
	}

	[Fact]
	public void QueueQueriesBucketsAndRetainsSubmissionsUntilCleared()
	{
		DeferredRenderQueue queue = new();
		CommandList commands = CreateCommandList();
		RenderBucket selection = new("Selection");

		RenderSubmission opaque = queue.SubmitOpaque(commands, Matrix4x4.Identity, tag: "opaque");
		RenderSubmission custom = queue.Submit(selection, commands, Matrix4x4.Identity, tag: "custom");

		Assert.Equal(2, queue.Count);
		Assert.Equal(new[] { RenderBucket.Opaque, selection }, queue.Buckets);
		Assert.Same(opaque, Assert.Single(queue.Query(RenderBucket.Opaque)));
		Assert.Same(custom, Assert.Single(queue.Query(selection)));
		Assert.Empty(queue.Query(RenderBucket.Transparent));

		queue.Execute(RenderBucket.Opaque);
		Assert.Equal(2, queue.Count);

		queue.BeginFrame();
		Assert.Equal(0, queue.Count);
		Assert.Empty(queue.Buckets);
		Assert.Equal(0, queue.SubmitOpaque(commands, Matrix4x4.Identity).Sequence);
	}

	[Fact]
	public void SubmissionSnapshotsModelMetadataAndCommandReferences()
	{
		DeferredRenderQueue queue = new();
		CommandList commands = CreateCommandList();
		Matrix4x4 model = Matrix4x4.CreateTranslation(10, 20, 30);
		object tag = new();

		RenderSubmission submission = queue.SubmitTransparent(
			commands,
			model,
			layer: 4,
			sortKey: 99,
			tag: tag
		);
		commands.Clear();

		Assert.Equal(model, submission.Model);
		Assert.Equal(new Vector3(10, 20, 30), submission.SortPosition);
		Assert.Equal(4, submission.Layer);
		Assert.Equal((ulong)99, submission.SortKey);
		Assert.Same(tag, submission.Tag);
		Assert.Single(submission.Batch.Commands);
	}

	[Fact]
	public void SubmissionAppliesAndRestoresModelOnSuccessAndFailure()
	{
		ShaderUniforms uniforms = ShaderUniforms.CreateDefault();
		Matrix4x4 original = Matrix4x4.CreateTranslation(1, 2, 3);
		Matrix4x4 submitted = Matrix4x4.CreateTranslation(8, 9, 10);
		uniforms.Model = original;
		ShaderUniforms.Push(uniforms);

		try
		{
			Matrix4x4 observed = default;
			DeferredRenderQueue queue = new();
			CommandList success = new();
			success.Add(new TestCommand(() => observed = ShaderUniforms.Current.Model));

			queue.SubmitOpaque(success, submitted).Execute();

			Assert.Equal(submitted, observed);
			Assert.Equal(original, uniforms.Model);

			CommandList failure = new();
			failure.Add(new TestCommand(() => throw new TestException()));
			RenderSubmission failingSubmission = queue.SubmitOpaque(failure, submitted);

			Assert.Throws<TestException>(failingSubmission.Execute);
			Assert.Equal(original, uniforms.Model);
		}
		finally
		{
			ShaderUniforms.Pop();
		}
	}

	[Fact]
	public void OpaqueFrontToBackUsesLayerDepthKeyAndStableSequence()
	{
		DeferredRenderQueue queue = new();
		CommandList commands = CreateCommandList();
		Camera camera = CreateCamera();

		RenderSubmission far = queue.SubmitOpaque(commands, Matrix4x4.Identity, new Vector3(0, 0, -20), sortKey: 5);
		RenderSubmission nearKey2 = queue.SubmitOpaque(
			commands,
			Matrix4x4.Identity,
			new Vector3(0, 0, -2),
			sortKey: 2
		);
		RenderSubmission nearKey1 = queue.SubmitOpaque(
			commands,
			Matrix4x4.Identity,
			new Vector3(0, 0, -2),
			sortKey: 1
		);
		RenderSubmission forcedLayer = queue.SubmitOpaque(
			commands,
			Matrix4x4.Identity,
			new Vector3(0, 0, -100),
			layer: -1
		);

		IReadOnlyList<RenderSubmission> sorted = queue.GetSorted(
			RenderBucket.Opaque,
			RenderSubmissionComparers.OpaqueFrontToBack(camera)
		);

		Assert.Equal(new[] { forcedLayer, nearKey1, nearKey2, far }, sorted);
		Assert.Equal(new[] { far, nearKey2, nearKey1, forcedLayer }, queue.Query(RenderBucket.Opaque));
	}

	[Fact]
	public void OpaqueStateFirstGroupsSortKeysBeforeDepth()
	{
		DeferredRenderQueue queue = new();
		CommandList commands = CreateCommandList();
		Camera camera = CreateCamera();

		RenderSubmission nearKey9 = queue.SubmitOpaque(
			commands,
			Matrix4x4.Identity,
			new Vector3(0, 0, -1),
			sortKey: 9
		);
		RenderSubmission farKey1 = queue.SubmitOpaque(
			commands,
			Matrix4x4.Identity,
			new Vector3(0, 0, -10),
			sortKey: 1
		);

		IReadOnlyList<RenderSubmission> sorted = queue.GetSorted(
			RenderBucket.Opaque,
			RenderSubmissionComparers.OpaqueStateThenFrontToBack(camera)
		);

		Assert.Equal(new[] { farKey1, nearKey9 }, sorted);
	}

	[Fact]
	public void TransparentSortingIsBackToFrontAndStableForTies()
	{
		DeferredRenderQueue queue = new();
		CommandList commands = CreateCommandList();
		Camera camera = CreateCamera();

		RenderSubmission near = queue.SubmitTransparent(commands, Matrix4x4.Identity, new Vector3(0, 0, -2));
		RenderSubmission farFirst = queue.SubmitTransparent(commands, Matrix4x4.Identity, new Vector3(0, 0, -12));
		RenderSubmission farSecond = queue.SubmitTransparent(commands, Matrix4x4.Identity, new Vector3(0, 0, -12));

		IReadOnlyList<RenderSubmission> sorted = queue.GetSorted(
			RenderBucket.Transparent,
			RenderSubmissionComparers.TransparentBackToFront(camera)
		);

		Assert.Equal(new[] { farFirst, farSecond, near }, sorted);
	}

	[Fact]
	public void SupportsCustomComparers()
	{
		DeferredRenderQueue queue = new();
		CommandList commands = CreateCommandList();
		RenderSubmission first = queue.SubmitOpaque(commands, Matrix4x4.Identity, sortKey: 1);
		RenderSubmission second = queue.SubmitOpaque(commands, Matrix4x4.Identity, sortKey: 2);
		IComparer<RenderSubmission> descendingKey = Comparer<RenderSubmission>.Create(
			(left, right) => right.SortKey.CompareTo(left.SortKey)
		);

		Assert.Equal(new[] { second, first }, queue.GetSorted(RenderBucket.Opaque, descendingKey));
	}

	[Fact]
	public void QueueRejectsMutationAndReentrantExecutionDuringReplay()
	{
		DeferredRenderQueue mutatingQueue = new();
		CommandList mutatingCommands = new();
		mutatingCommands.Add(new TestCommand(mutatingQueue.Clear));
		mutatingQueue.SubmitOpaque(mutatingCommands, Matrix4x4.Identity);

		Assert.Throws<InvalidOperationException>(() => mutatingQueue.Execute(RenderBucket.Opaque));
		Assert.False(mutatingQueue.IsExecuting);
		Assert.Single(mutatingQueue.Query(RenderBucket.Opaque));

		DeferredRenderQueue recursiveQueue = new();
		CommandList recursiveCommands = new();
		recursiveCommands.Add(new TestCommand(() => recursiveQueue.Execute(RenderBucket.Opaque)));
		recursiveQueue.SubmitOpaque(recursiveCommands, Matrix4x4.Identity);

		Assert.Throws<InvalidOperationException>(() => recursiveQueue.Execute(RenderBucket.Opaque));
		Assert.False(recursiveQueue.IsExecuting);

		DeferredRenderQueue sortingQueue = new();
		sortingQueue.SubmitOpaque(CreateCommandList(), Matrix4x4.Identity);
		sortingQueue.SubmitOpaque(CreateCommandList(), Matrix4x4.Identity);
		IComparer<RenderSubmission> mutatingComparer = Comparer<RenderSubmission>.Create((_, _) =>
		{
			sortingQueue.Clear();
			return 0;
		});

		Assert.Throws<InvalidOperationException>(
			() => sortingQueue.Execute(RenderBucket.Opaque, mutatingComparer)
		);
		Assert.False(sortingQueue.IsExecuting);
		Assert.Equal(2, sortingQueue.Query(RenderBucket.Opaque).Count);
	}

	[Fact]
	public void RejectsInvalidSubmissionsAndComparers()
	{
		DeferredRenderQueue queue = new();
		CommandList empty = new();
		CommandList commands = CreateCommandList();
		Matrix4x4 invalidMatrix = Matrix4x4.Identity;
		invalidMatrix.M22 = float.NaN;

		Assert.Throws<ArgumentNullException>(() => queue.SubmitOpaque((CommandList)null, Matrix4x4.Identity));
		Assert.Throws<ArgumentNullException>(() => queue.SubmitOpaque((GraphicsCommandBatch)null, Matrix4x4.Identity));
		Assert.Throws<ArgumentException>(() => queue.SubmitOpaque(empty, Matrix4x4.Identity));
		Assert.Throws<ArgumentException>(() => queue.Submit(default, commands, Matrix4x4.Identity));
		Assert.Throws<ArgumentOutOfRangeException>(() => queue.SubmitOpaque(commands, invalidMatrix));
		Assert.Throws<ArgumentOutOfRangeException>(
			() => queue.SubmitOpaque(commands, Matrix4x4.Identity, new Vector3(float.PositiveInfinity, 0, 0))
		);
		Assert.Throws<ArgumentNullException>(() => queue.GetSorted(RenderBucket.Opaque, null));
		Assert.Throws<ArgumentNullException>(() => new GraphicsCommandBatch(null));
		Assert.Throws<ArgumentException>(
			() => new GraphicsCommandBatch(new GraphicsCommand[] { new TestCommand(() => { }), null })
		);
	}

	[Fact]
	public void TypedDrawableCommandsRetainResourcesAndRejectNullDrawables()
	{
		Mesh3D mesh = (Mesh3D)RuntimeHelpers.GetUninitializedObject(typeof(Mesh3D));
		RenderModel model = (RenderModel)RuntimeHelpers.GetUninitializedObject(typeof(RenderModel));
		CommandList commands = new();

		DrawMesh3DCommand meshCommand = commands.RecordDrawMesh(mesh);
		DrawRenderModelCommand modelCommand = commands.RecordDrawModel(model);

		Assert.Same(mesh, meshCommand.Mesh);
		Assert.Same(model, modelCommand.Model);
		Assert.IsType<DrawMesh3DCommand>(commands[0]);
		Assert.IsType<DrawRenderModelCommand>(commands[1]);
		Assert.Throws<ArgumentNullException>(() => new DrawMesh3DCommand(null));
		Assert.Throws<ArgumentNullException>(() => new DrawRenderModelCommand(null));
	}

	private static CommandList CreateCommandList()
	{
		CommandList commands = new();
		commands.Add(new TestCommand(() => { }));

		return commands;
	}

	private static Camera CreateCamera()
	{
		Camera camera = new();
		camera.Position = Vector3.Zero;
		camera.Rotation = Quaternion.Identity;

		return camera;
	}

	private sealed class TestCommand : GraphicsCommand
	{
		private readonly Action action;

		public TestCommand(Action action)
		{
			this.action = action;
		}

		public override void Execute() => action();
	}

	private sealed class TestException : Exception
	{
	}
}
