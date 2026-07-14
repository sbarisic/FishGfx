using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using FishGfx.NodeEditor;
using FishGfx.NodeGraph;
using Xunit;

namespace FishGfx.Tests;

public class NodeGraphTests
{
	[Fact]
	public void RegistryExposesOnlyAttributedMethodsAndExpandsNamedTuples()
	{
		NodeFunctionRegistry registry = Registry();
		Assert.DoesNotContain(registry.Functions, f => f.Method.Name == nameof(Functions.Hidden));
		NodeFunctionDescriptor pair = Find(registry, nameof(Functions.Pair));
		Assert.Equal(new[] { "sum", "product" }, pair.Outputs.Select(o => o.Name));
		Assert.All(pair.Outputs, o => Assert.Equal(typeof(int), o.Type));
		Assert.Equal("Pretty", Find(registry, nameof(Functions.Named)).Title);
		Assert.All(registry.Functions.Where(f => f.Title == "Convert"), f => Assert.Contains("(", f.MenuLabel));
		Assert.Equal("Math", Find(registry, nameof(Functions.Add)).Category);
		Assert.Equal(nameof(Functions), Find(registry, nameof(Functions.Identity)).Category);
		Assert.Same(Find(registry, nameof(Functions.Add)), registry.Get("test.add"));
	}

	[Fact]
	public void RegistrationRejectsDuplicatesAndUnsupportedShapes()
	{
		NodeFunctionRegistry registry = Registry();
		Assert.Throws<InvalidOperationException>(() => registry.Register(typeof(Functions)));
		Assert.Throws<NotSupportedException>(() => new NodeFunctionRegistry().Register(typeof(InvalidFunctions)));
		Assert.Throws<NotSupportedException>(() => new NodeFunctionRegistry().Register(typeof(InvalidBodyFunctions)));
		Assert.Throws<NotSupportedException>(() => new NodeFunctionRegistry().Register(typeof(RefFunctions)));
		Assert.Throws<InvalidOperationException>(() => registry.Register(typeof(DuplicateIdFunctions)));
		Assert.Throws<ArgumentException>(() => new NodeFunctionRegistry().Register(typeof(MissingIdFunctions)));
		Assert.Throws<ArgumentException>(() => new NodeFunctionRegistry().Register(typeof(NotStatic)));
	}

	[Fact]
	public void NodesUseExactTypesReplaceInputsAndFanOut()
	{
		NodeFunctionRegistry registry = Registry();
		FunctionGraph graph = new FunctionGraph();
		FunctionNode a = graph.AddNode(Find(registry, nameof(Functions.Constant)), Vector2.Zero);
		FunctionNode b = graph.AddNode(Find(registry, nameof(Functions.Constant)), Vector2.Zero);
		FunctionNode add = graph.AddNode(Find(registry, nameof(Functions.Add)), Vector2.Zero);
		FunctionNode floatSink = graph.AddNode(Find(registry, nameof(Functions.FloatIdentity)), Vector2.Zero);
		Guid[] portIds = graph.Nodes.SelectMany(node => node.Inputs.Concat(node.Outputs)).Select(port => port.Id).ToArray();
		Assert.DoesNotContain(Guid.Empty, portIds);
		Assert.Equal(portIds.Length, portIds.Distinct().Count());
		Assert.False(graph.TryConnect(a.Outputs[0], floatSink.Inputs[0], out _));
		Assert.True(graph.TryConnect(a.Outputs[0], add.Inputs[0], out _));
		Assert.True(graph.TryConnect(b.Outputs[0], add.Inputs[0], out NodeConnection replacement));
		Assert.True(graph.TryConnect(b.Outputs[0], add.Inputs[1], out _));
		Assert.Equal(2, graph.Connections.Count);
		Assert.True(graph.TryGetInputConnection(add.Inputs[0], out NodeConnection connected));
		Assert.Same(replacement, connected);
	}

	[Fact]
	public void ConnectionsRejectForeignPortsWithoutReplacingExistingInput()
	{
		NodeFunctionRegistry registry = Registry();
		FunctionGraph graph = new FunctionGraph();
		FunctionNode source = graph.AddNode(Find(registry, nameof(Functions.Constant)), Vector2.Zero);
		FunctionNode sink = graph.AddNode(Find(registry, nameof(Functions.Identity)), Vector2.Zero);
		Assert.True(graph.TryConnect(source.Outputs[0], sink.Inputs[0], out NodeConnection existing));

		FunctionGraph foreignGraph = new FunctionGraph();
		FunctionNode foreign = foreignGraph.AddNode(Find(registry, nameof(Functions.Constant)), Vector2.Zero);

		Assert.False(graph.TryConnect(foreign.Outputs[0], sink.Inputs[0], out _));
		Assert.Single(graph.Connections);
		Assert.True(graph.TryGetInputConnection(sink.Inputs[0], out NodeConnection connected));
		Assert.Same(existing, connected);
	}

	[Fact]
	public void EvaluatorUsesDefaultsPropagatesAndExpandsTuples()
	{
		NodeFunctionRegistry registry = Registry();
		FunctionGraph graph = new FunctionGraph();
		FunctionNode constant = graph.AddNode(Find(registry, nameof(Functions.Constant)), Vector2.Zero);
		constant.InlineValues[0].Text = "6";
		FunctionNode add = graph.AddNode(Find(registry, nameof(Functions.Add)), Vector2.Zero);
		FunctionNode pair = graph.AddNode(Find(registry, nameof(Functions.Pair)), Vector2.Zero);
		graph.TryConnect(constant.Outputs[0], add.Inputs[0], out _);
		graph.TryConnect(add.Outputs[0], pair.Inputs[0], out _);
		NodeEvaluationResult result = new FunctionGraphEvaluator().Evaluate(graph);
		Assert.True(result.Success);
		Assert.Equal(6, add.Outputs[0].Value);
		Assert.Equal(12, pair.Outputs[0].Value);
		Assert.Equal(36, pair.Outputs[1].Value);
	}

	[Fact]
	public void EvaluatorReportsCyclesExceptionsAndContinuesIndependentNodes()
	{
		NodeFunctionRegistry registry = Registry();
		FunctionGraph graph = new FunctionGraph();
		FunctionNode one = graph.AddNode(Find(registry, nameof(Functions.Identity)), Vector2.Zero);
		FunctionNode two = graph.AddNode(Find(registry, nameof(Functions.Identity)), Vector2.Zero);
		FunctionNode fail = graph.AddNode(Find(registry, nameof(Functions.Throw)), Vector2.Zero);
		FunctionNode good = graph.AddNode(Find(registry, nameof(Functions.Constant)), Vector2.Zero);
		graph.TryConnect(one.Outputs[0], two.Inputs[0], out _);
		graph.TryConnect(two.Outputs[0], one.Inputs[0], out _);
		NodeEvaluationResult result = new FunctionGraphEvaluator().Evaluate(graph);
		Assert.False(result.Success);
		Assert.Equal(NodeEvaluationState.Error, one.EvaluationState);
		Assert.Equal(NodeEvaluationState.Error, two.EvaluationState);
		Assert.Equal(NodeEvaluationState.Error, fail.EvaluationState);
		Assert.Equal(NodeEvaluationState.Success, good.EvaluationState);
	}

	[Theory]
	[InlineData(typeof(int), "-42")]
	[InlineData(typeof(double), "3.25")]
	[InlineData(typeof(bool), "true")]
	[InlineData(typeof(TestEnum), "Second")]
	[InlineData(typeof(Vector2), "1.5, -2")]
	public void BodyValueConverterParsesSupportedValues(Type type, string text) =>
		Assert.True(NodeValueConverter.TryParse(text, type, out _));

	[Fact]
	public void BodyValueConverterRejectsInvalidAndNonFiniteValues()
	{
		Assert.False(NodeValueConverter.TryParse("NaN", typeof(float), out _));
		Assert.False(NodeValueConverter.TryParse("1, 2", typeof(Vector3), out _));
	}

	[Fact]
	public void CanvasAndGeometryRemainStableWithFunctionNodes()
	{
		NodeCanvas canvas = new NodeCanvas();
		Vector2 world = new Vector2(123, 456);
		AssertVector(world, canvas.ScreenToWorld(canvas.WorldToScreen(world)));
		Vector2 cursor = new Vector2(800, 450);
		Vector2 before = canvas.ScreenToWorld(cursor);
		canvas.ZoomAt(cursor, 3);
		AssertVector(before, canvas.ScreenToWorld(cursor));
		FunctionNode node = new FunctionGraph().AddNode(
			Find(Registry(), nameof(Functions.Constant)),
			new Vector2(100, 200)
		);
		Assert.True(NodeGeometry.BoundsOf(node).Contains(new Vector2(120, 220)));
		Bounds valueBounds = NodeGeometry.ValueBounds(node, 0);
		Assert.True(
			valueBounds.Y + valueBounds.Height < NodeGeometry.PortPosition(node.Outputs[0]).Y - NodeGeometry.PortRadius
		);
		Assert.True(NodeGeometry.NearConnection(new Vector2(300, 300), new Vector2(100, 300), new Vector2(500, 300)));
	}

	[Fact]
	public void ContextMenuFiltersNavigatesColorsAndClampsToScreen()
	{
		NodeFunctionRegistry registry = Registry();
		ContextMenu menu = new ContextMenu(registry.Functions);
		Vector2 world = new Vector2(123, 456);
		menu.Open(new Vector2(1910, 1070), world, 1920, 1080);
		Assert.Equal(new Vector2(1390, 650), menu.Position);
		Assert.Equal(world, menu.InsertionWorld);
		Assert.Equal(
			menu.Categories.Select(c => c.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase),
			menu.Categories.Select(c => c.Name)
		);
		Assert.Equal(ContextMenu.ColorFor("Math"), ContextMenu.ColorFor("Math"));
		Assert.NotEqual(ContextMenu.ColorFor("Math"), ContextMenu.ColorFor("Values"));
		menu.Append("Single");
		Assert.All(menu.CurrentFunctions, f => Assert.Contains(f.Parameters, p => p.Type == typeof(float)));
		Assert.False(menu.Escape());
		Assert.Equal("", menu.SearchText);
		NodeFunctionDescriptor before = menu.Activate();
		menu.MoveFunction(1);
		Assert.NotNull(menu.Activate());
		menu.MoveCategory(1);
		Assert.NotNull(menu.Activate());
		menu.Scroll(new Vector2(menu.Position.X + 10, menu.Position.Y + 100), -100);
		Assert.InRange(menu.CategoryScroll, 0, Math.Max(0, menu.Categories.Count - menu.VisibleRows));
		Assert.NotNull(before);
	}

	private static NodeFunctionRegistry Registry()
	{
		NodeFunctionRegistry registry = new NodeFunctionRegistry();
		registry.Register(typeof(Functions));
		return registry;
	}

	private static NodeFunctionDescriptor Find(NodeFunctionRegistry registry, string method) =>
		registry.Functions.Single(f => f.Method.Name == method);

	private static void AssertVector(Vector2 expected, Vector2 actual)
	{
		Assert.InRange(actual.X, expected.X - .001f, expected.X + .001f);
		Assert.InRange(actual.Y, expected.Y - .001f, expected.Y + .001f);
	}

	public enum TestEnum
	{
		First,
		Second,
	}

	public static class Functions
	{
		[NodeFunction("test.constant", Category = "Values")]
		public static int Constant([NodeInline] int value = 1) => value;

		[NodeFunction("test.add", Category = "Math")]
		public static int Add(int a, int b) => a + b;

		[NodeFunction("test.identity")]
		public static int Identity(int value) => value;

		[NodeFunction("test.float-identity")]
		public static float FloatIdentity(float value) => value;

		[NodeFunction("test.pair", Category = "Math")]
		public static (int sum, int product) Pair(int value) => (value * 2, value * value);

		[NodeFunction("test.named", Title = "Pretty")]
		public static bool Named([NodeInline] bool value = true) => value;

		[NodeFunction("test.throw")]
		public static int Throw() => throw new InvalidOperationException("boom");

		[NodeFunction("test.convert-int", Title = "Convert")]
		public static int ConvertInt(int value) => value;

		[NodeFunction("test.convert-float", Title = "Convert")]
		public static float ConvertFloat(float value) => value;

		public static void Hidden()
		{
		}
	}

	public static class InvalidFunctions
	{
		[NodeFunction("invalid.async")]
		public static Task Async() => Task.CompletedTask;
	}

	public static class InvalidBodyFunctions
	{
		[NodeFunction("invalid.inline")]
		public static DateTime Unsupported([NodeInline] DateTime value) => value;
	}

	public static class RefFunctions
	{
		[NodeFunction("invalid.ref")]
		public static int Ref(ref int value) => value;
	}

	public static class DuplicateIdFunctions
	{
		[NodeFunction("test.add")]
		public static int Duplicate(int value) => value;
	}

	public static class MissingIdFunctions
	{
		[NodeFunction("")]
		public static int Missing(int value) => value;
	}

	public class NotStatic
	{
	}
}
