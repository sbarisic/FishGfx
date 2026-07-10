using FishGfx.NodeEditor;
using System.Numerics;
using Xunit;

namespace FishGfx.Tests;

public class NodeEditorTests {
	[Fact]
	public void ConnectionsAreTypedAndInputsAreReplaced() {
		NodeGraph graph = new NodeGraph();
		Node scalarA = NodeTemplates.Create("Scalar Source", Vector2.Zero);
		Node scalarB = NodeTemplates.Create("Scalar Source", Vector2.Zero);
		Node process = NodeTemplates.Create("Scalar Process", Vector2.Zero);
		Node vector = NodeTemplates.Create("Vector Source", Vector2.Zero);
		Assert.NotNull(graph.Connect(scalarA.Outputs[0], process.Inputs[0]));
		Assert.Null(graph.Connect(vector.Outputs[0], process.Inputs[0]));
		NodeConnection replacement = graph.Connect(scalarB.Outputs[0], process.Inputs[0]);
		Assert.Single(graph.Connections);
		Assert.Same(replacement, graph.ConnectionAtInput(process.Inputs[0]));
	}

	[Fact]
	public void OutputsFanOutAndNodeRemovalCleansConnections() {
		NodeGraph graph = new NodeGraph();
		Node source = NodeTemplates.Create("Scalar Source", Vector2.Zero);
		Node one = NodeTemplates.Create("Scalar Output", Vector2.Zero);
		Node two = NodeTemplates.Create("Scalar Output", Vector2.Zero);
		graph.Nodes.AddRange(new[] { source, one, two });
		graph.Connect(source.Outputs[0], one.Inputs[0]); graph.Connect(source.Outputs[0], two.Inputs[0]);
		Assert.Equal(2, graph.Connections.Count);
		graph.Remove(source);
		Assert.Empty(graph.Connections);
	}

	[Fact]
	public void CanvasTransformsRoundTripAndZoomKeepsCursorWorldPoint() {
		NodeCanvas canvas = new NodeCanvas();
		Vector2 world = new Vector2(123, 456);
		AssertVector(world, canvas.ScreenToWorld(canvas.WorldToScreen(world)));
		Vector2 cursor = new Vector2(800, 450);
		Vector2 before = canvas.ScreenToWorld(cursor);
		canvas.ZoomAt(cursor, 3);
		AssertVector(before, canvas.ScreenToWorld(cursor));
	}

	[Fact]
	public void InlineEditorCommitsOnlyFiniteNumbers() {
		NodeValue value = new NodeValue("value", 4);
		InlineValueEditor editor = new InlineValueEditor();
		editor.Begin(value);
		editor.Append(".5"); Assert.True(editor.Commit()); Assert.Equal(4.5f, value.Value);
		editor.Begin(value); while (editor.Text.Length > 0) editor.Backspace();
		editor.Append("-"); Assert.False(editor.Commit()); Assert.Equal(4.5f, value.Value);
	}

	[Fact]
	public void GeometryHitsNodesPortsAndBezier() {
		Node node = NodeTemplates.Create("Scalar Source", new Vector2(100, 200));
		Assert.True(NodeGeometry.BoundsOf(node).Contains(new Vector2(120, 220)));
		Vector2 port = NodeGeometry.PortPosition(node.Outputs[0]);
		Assert.Equal(node.Position.X + node.Width, port.X);
		Assert.True(NodeGeometry.NearConnection(new Vector2(300, 300), new Vector2(100, 300), new Vector2(500, 300)));
		Assert.False(NodeGeometry.NearConnection(new Vector2(300, 500), new Vector2(100, 300), new Vector2(500, 300)));
	}

	private static void AssertVector(Vector2 expected, Vector2 actual) {
		Assert.InRange(actual.X, expected.X - .001f, expected.X + .001f);
		Assert.InRange(actual.Y, expected.Y - .001f, expected.Y + .001f);
	}
}
