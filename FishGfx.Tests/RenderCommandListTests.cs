using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using FishGfx;
using FishGfx.Graphics;
using Xunit;

namespace FishGfx.Tests;

public class RenderCommandListTests
{
	[Fact]
	public void ReplayAuthorityBelongsToRenderPass()
	{
		MethodInfo commandExecute = Assert.Single(
			typeof(RenderCommand).GetMethods(
				BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly
			),
			method => method.Name == nameof(RenderCommand.Execute)
		);

		Assert.Equal(typeof(void), commandExecute.ReturnType);
		Assert.Equal(
			new[] { typeof(RenderPass) },
			commandExecute.GetParameters().Select(parameter => parameter.ParameterType)
		);
		AssertNoPublicExecute(typeof(RenderCommandList));
		AssertNoPublicExecute(typeof(RenderCommandBatch));
		AssertNoPublicExecute(typeof(RenderQueue));
		AssertNoPublicExecute(typeof(RenderItem));

		Type[] passExecutionTargets = typeof(RenderPass)
			.GetMethods(BindingFlags.Public | BindingFlags.Instance)
			.Where(method => method.Name == nameof(RenderPass.Execute))
			.Select(method => method.GetParameters()[0].ParameterType)
			.ToArray();

		Assert.Contains(typeof(RenderCommand), passExecutionTargets);
		Assert.Contains(typeof(RenderCommandList), passExecutionTargets);
		Assert.Contains(typeof(RenderCommandBatch), passExecutionTargets);
		Assert.Contains(typeof(RenderItem), passExecutionTargets);
		Assert.Contains(typeof(RenderQueue), passExecutionTargets);
	}

	[Fact]
	public void SnapshotIsImmutableAndIndependentFromRecorder()
	{
		RenderCommandList list = new();
		TestCommand first = list.Add(new TestCommand());
		RenderCommandBatch snapshot = list.Snapshot();

		list.Clear();
		TestCommand second = list.Add(new TestCommand());

		Assert.Single(snapshot.Commands);
		Assert.Same(first, snapshot[0]);
		Assert.Same(second, list[0]);
		Assert.False(snapshot.Commands is IList<RenderCommand> mutable && !mutable.IsReadOnly);
	}

	[Fact]
	public void RecorderRejectsMutationAndRecursiveReplayWhileLocked()
	{
		RenderCommandList list = new();
		list.Add(new TestCommand());
		list.BeginExecution();

		try
		{
			InvalidOperationException mutation = Assert.Throws<InvalidOperationException>(list.Clear);
			InvalidOperationException recursion = Assert.Throws<InvalidOperationException>(list.BeginExecution);

			Assert.Contains("modified", mutation.Message, StringComparison.OrdinalIgnoreCase);
			Assert.Contains("recursively", recursion.Message, StringComparison.OrdinalIgnoreCase);
			Assert.True(list.IsExecuting);
			Assert.Single(list.Commands);
		}
		finally
		{
			list.EndExecution();
		}

		Assert.False(list.IsExecuting);
	}

	[Fact]
	public void SupportsInspectionRemovalAndClearing()
	{
		RenderCommandList list = new();
		TestCommand first = list.Add(new TestCommand());
		TestCommand second = list.Add(new TestCommand());

		Assert.Equal(2, list.Count);
		Assert.Same(first, list[0]);
		Assert.Same(second, list.Commands[1]);
		Assert.False(list.Commands is IList<RenderCommand> mutable && !mutable.IsReadOnly);

		Assert.True(list.Remove(first));
		Assert.False(list.Remove(first));
		Assert.Same(second, list[0]);

		list.RemoveAt(0);
		Assert.Empty(list.Commands);

		list.Add(first);
		list.Clear();
		Assert.Empty(list.Commands);
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
		RenderCommandList list = new();

		Point2DCommand point = list.RecordDrawPoint(new Vertex2(new Vector2(1, 2), Color.White));
		EllipseCommand circle = list.RecordDrawCircle(new Vector2(5, 6), 7, color: Color.Red);
		FillEllipseCommand filledCircle = list.RecordFillCircle(new Vector2(8, 9), 10);
		RectangleCommand rectangle = list.RecordDrawRectangle(
			new Vector2(10, 20),
			new Vector2(30, 40)
		);

		Assert.Single(point.Points);
		Assert.Equal(new Vector2(7), circle.Radii);
		Assert.Equal(new Vector2(10), filledCircle.Radii);
		Assert.Equal(10, rectangle.X);
		Assert.Equal(40, rectangle.Height);
		Assert.Collection(
			list.Commands,
			command => Assert.IsType<Point2DCommand>(command),
			command => Assert.IsType<EllipseCommand>(command),
			command => Assert.IsType<FillEllipseCommand>(command),
			command => Assert.IsType<RectangleCommand>(command)
		);
	}

	[Fact]
	public void ConvenienceMethodsPreserveRecordedValues()
	{
		RenderCommandList list = new();
		RingCommand ring = list.RecordDrawRing(
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
	public void StateChangesAreRecordedAsBalancedScopes()
	{
		RenderCommandList list = new();
		RenderStateScopeCommand stateScope = list.RecordStateScope(
			RenderState.Default,
			nested => nested.RecordClearDepth(0.5f)
		);

		Assert.Equal(RenderState.Default, stateScope.State);
		Assert.IsType<ClearDepthCommand>(Assert.Single(stateScope.Commands.Commands));
		Assert.Same(stateScope, Assert.Single(list.Commands));
		Assert.DoesNotContain(
			typeof(RenderCommand).Assembly.GetTypes(),
			type => type.Name.Contains("PopRenderState", StringComparison.Ordinal)
		);
	}

	[Fact]
	public void RejectsNullCommandsArraysAndRequiredResources()
	{
		RenderCommandList list = new();

		Assert.Throws<ArgumentNullException>(() => list.Add<RenderCommand>(null));
		Assert.Throws<ArgumentNullException>(() => list.Remove(null));
		Assert.Throws<ArgumentNullException>(() => new Point2DCommand(null));
		Assert.Throws<ArgumentNullException>(() => new Point3DCommand(null));
		Assert.Throws<ArgumentNullException>(() => new LineStrip2DCommand(null));
		Assert.Throws<ArgumentNullException>(
			() => new NinePatchCommand(Vector2.Zero, Vector2.One, null, new NinePatchInsets(1))
		);
		Assert.Throws<ArgumentNullException>(
			() => new TexturedEllipseCommand(
				Vector2.Zero,
				Vector2.One,
				null,
				Vector2.Zero,
				Vector2.One
			)
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
			() => new DrawTextCommand(null, Vector2.Zero, "text", Color.White, 16)
		);
	}

	private static void AssertNoPublicExecute(Type type)
	{
		Assert.DoesNotContain(
			type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
			method => method.Name == "Execute"
		);
	}

	private sealed class TestCommand : RenderCommand
	{
		public override void Execute(RenderPass pass)
		{
		}
	}
}
