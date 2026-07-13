using System;
using System.Collections.Generic;
using System.Numerics;
using FishGfx;
using FishGfx.Graphics;
using Xunit;

namespace FishGfx.Tests;

public class CommandListTests
{
	[Fact]
	public void ExecutesInInsertionOrderAndCanReplay()
	{
		List<int> calls = new();
		CommandList list = new();

		list.Add(new TestCommand(() => calls.Add(1)));
		list.Add(new TestCommand(() => calls.Add(2)));

		list.Execute();
		list.Execute();

		Assert.Equal(new[] { 1, 2, 1, 2 }, calls);
		Assert.Equal(2, list.Count);
		Assert.False(list.IsExecuting);
	}

	[Fact]
	public void FailureStopsReplayAndRestoresExecutionFlag()
	{
		List<int> calls = new();
		CommandList list = new();

		list.Add(new TestCommand(() => calls.Add(1)));
		list.Add(new TestCommand(() => throw new TestException()));
		list.Add(new TestCommand(() => calls.Add(3)));

		Assert.Throws<TestException>(() => list.Execute());
		Assert.Equal(new[] { 1 }, calls);
		Assert.Equal(3, list.Count);
		Assert.False(list.IsExecuting);
	}

	[Fact]
	public void RejectsMutationDuringExecution()
	{
		CommandList list = new();
		list.Add(new TestCommand(list.Clear));

		InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => list.Execute());

		Assert.Contains("modified", exception.Message, StringComparison.OrdinalIgnoreCase);
		Assert.Single(list.Commands);
		Assert.False(list.IsExecuting);
	}

	[Fact]
	public void RejectsReentrantExecution()
	{
		CommandList list = new();
		list.Add(new TestCommand(list.Execute));

		InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => list.Execute());

		Assert.Contains("recursively", exception.Message, StringComparison.OrdinalIgnoreCase);
		Assert.False(list.IsExecuting);
	}

	[Fact]
	public void SupportsInspectionRemovalAndClearing()
	{
		CommandList list = new();
		TestCommand first = list.Add(new TestCommand(() => { }));
		TestCommand second = list.Add(new TestCommand(() => { }));

		Assert.Equal(2, list.Count);
		Assert.Same(first, list[0]);
		Assert.Same(second, list.Commands[1]);
		Assert.False(list.Commands is IList<GraphicsCommand> mutable && !mutable.IsReadOnly);

		Assert.True(list.Remove(first));
		Assert.False(list.Remove(first));
		Assert.Same(second, list[0]);

		list.RemoveAt(0);
		Assert.Empty(list.Commands);

		list.Add(first);
		list.Clear();
		Assert.Equal(0, list.Count);
	}

	[Fact]
	public void PointCommandsSnapshotMutableArrays()
	{
		Vertex2 original = new(new Vector2(10, 20), Color.Red);
		Vertex2[] points = { original };
		Point2DCommand command = new(points, 3);

		points[0] = new Vertex2(new Vector2(100, 200), Color.Blue);

		Assert.Equal(original.Position, command.Points[0].Position);
		Assert.Equal(original.Color, command.Points[0].Color);
		Assert.Equal(3, command.Thickness);
	}

	[Fact]
	public void ConvenienceOverloadsCanonicalizeEquivalentShapes()
	{
		CommandList list = new();

		Point2DCommand point = list.RecordPoint(new Vertex2(new Vector2(1, 2), Color.White));
		EllipseCommand circle = list.RecordCircle(new Vector2(5, 6), 7, color: Color.Red);
		FilledEllipseCommand filledCircle = list.RecordFilledCircle(new Vector2(8, 9), 10);
		RectangleCommand rectangle = list.RecordRectangle(new Vector2(10, 20), new Vector2(30, 40));

		Assert.Single(point.Points);
		Assert.Equal(new Vector2(7), circle.Radii);
		Assert.Equal(new Vector2(10), filledCircle.Radii);
		Assert.Equal(10, rectangle.X);
		Assert.Equal(40, rectangle.Height);
		Assert.Collection(
			list.Commands,
			command => Assert.IsType<Point2DCommand>(command),
			command => Assert.IsType<EllipseCommand>(command),
			command => Assert.IsType<FilledEllipseCommand>(command),
			command => Assert.IsType<RectangleCommand>(command)
		);
	}

	[Fact]
	public void ConvenienceMethodsPreserveRecordedValues()
	{
		CommandList list = new();
		RingLinesCommand ring = list.RecordRingLines(
			new Vector2(30, 40),
			5,
			20,
			0.25f,
			1.75f,
			4,
			Color.Yellow,
			12
		);

		Assert.Equal(new Vector2(30, 40), ring.Center);
		Assert.Equal(5, ring.InnerRadius);
		Assert.Equal(20, ring.OuterRadius);
		Assert.Equal(0.25f, ring.StartAngle);
		Assert.Equal(1.75f, ring.EndAngle);
		Assert.Equal(4, ring.Thickness);
		Assert.Equal(Color.Yellow, ring.Color);
		Assert.Equal(12, ring.Segments);
	}

	[Fact]
	public void RejectsNullCommandsArraysAndRequiredResources()
	{
		CommandList list = new();

		Assert.Throws<ArgumentNullException>(() => list.Add<GraphicsCommand>(null));
		Assert.Throws<ArgumentNullException>(() => list.Remove(null));
		Assert.Throws<ArgumentNullException>(() => new Point2DCommand(null));
		Assert.Throws<ArgumentNullException>(() => new Point3DCommand(null));
		Assert.Throws<ArgumentNullException>(() => new LineStrip2DCommand(null));
		Assert.Throws<ArgumentNullException>(
			() => new NinePatchCommand(Vector2.Zero, Vector2.One, null, new NinePatchInsets(1))
		);
		Assert.Throws<ArgumentNullException>(
			() => new TexturedEllipseCommand(Vector2.Zero, Vector2.One, null, Vector2.Zero, Vector2.One)
		);
		Assert.Throws<ArgumentNullException>(
			() => new TexturedRoundedRectangleCommand(
				Vector2.Zero,
				Vector2.One,
				new CornerRadii(1),
				null,
				Vector2.Zero,
				Vector2.One
			)
		);
		Assert.Throws<ArgumentNullException>(
			() => new DrawTextCommand(null, Vector2.Zero, "text", Color.White)
		);
	}

	[Fact]
	public void SnapshotsRejectUnbalancedRenderStateCommands()
	{
		CommandList missingPop = new();
		missingPop.RecordPushRenderState(Gfx.CreateDefaultRenderState());
		Assert.Throws<InvalidOperationException>(() => missingPop.Snapshot());

		CommandList missingPush = new();
		missingPush.RecordPopRenderState();
		Assert.Throws<InvalidOperationException>(() => missingPush.Snapshot());

		CommandList balanced = new();
		balanced.RecordPushRenderState(Gfx.CreateDefaultRenderState());
		balanced.RecordPopRenderState();
		Assert.Equal(2, balanced.Snapshot().Count);
	}

	private sealed class TestCommand : GraphicsCommand
	{
		private readonly Action action;

		public TestCommand(Action action)
		{
			this.action = action;
		}

		public override void Execute()
		{
			action();
		}
	}

	private sealed class TestException : Exception
	{
	}
}
