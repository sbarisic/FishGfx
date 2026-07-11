using FishGfx.NodeEditor;
using FishGfx.NodeGraph;
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace FishGfx.Tests;

public class NodeGraphJsonTests {
	[Fact]
	public void RoundTripPreservesGraphIdsBodyLayoutConnectionsAndView() {
		NodeFunctionRegistry registry = Registry(); FunctionNodeGraph graph = new FunctionNodeGraph();
		FunctionNode source = graph.CreateNode(Find(registry, nameof(JsonFunctions.Constant)), new Vector2(12, 34)); source.Width = 275; source.BodyValues[0].Text = "42";
		FunctionNode sink = graph.CreateNode(Find(registry, nameof(JsonFunctions.Identity)), new Vector2(500, 80)); graph.Connect(source.Outputs[0], sink.Inputs[0]);
		NodeGraphViewState view = new NodeGraphViewState(new Vector2(91, -17), 1.4f);
		string json = NodeGraphJson.Serialize(graph, view); NodeGraphLoadResult loaded = NodeGraphJson.Deserialize(json, registry);
		Assert.True(loaded.Success); Assert.Equal(graph.Nodes.Select(n => n.Id), loaded.Graph.Nodes.Select(n => n.Id));
		Assert.Equal("42", loaded.Graph.Nodes[0].BodyValues[0].Text); Assert.Equal(275, loaded.Graph.Nodes[0].Width);
		Assert.Single(loaded.Graph.Connections); Assert.Equal(view.Pan, loaded.View.Pan); Assert.Equal(view.Zoom, loaded.View.Zoom);
		Assert.Equal(json, NodeGraphJson.Serialize(loaded.Graph, loaded.View));
	}

	[Fact]
	public void InvalidDocumentReturnsAllErrorsWithoutChangingExistingGraph() {
		NodeFunctionRegistry registry = Registry(); FunctionNodeGraph original = new FunctionNodeGraph(); original.CreateNode(Find(registry, nameof(JsonFunctions.Constant)), Vector2.Zero);
		JsonNode root = JsonNode.Parse(NodeGraphJson.Serialize(original, new NodeGraphViewState(Vector2.Zero, 1)));
		root["version"] = 99; root["canvas"]["zoom"] = 99; root["nodes"][0]["declaringType"] = "Missing.Type"; root["nodes"][0]["x"] = "NaN";
		NodeGraphLoadResult load = NodeGraphJson.Deserialize(root.ToJsonString(), registry);
		Assert.False(load.Success); Assert.True(load.Errors.Count >= 3); Assert.Single(original.Nodes);
	}

	[Fact]
	public void DuplicateConnectionsAndInvalidIndexesAreRejected() {
		NodeFunctionRegistry registry = Registry(); FunctionNodeGraph graph = new FunctionNodeGraph();
		FunctionNode source = graph.CreateNode(Find(registry, nameof(JsonFunctions.Constant)), Vector2.Zero), sink = graph.CreateNode(Find(registry, nameof(JsonFunctions.Identity)), Vector2.One);
		graph.Connect(source.Outputs[0], sink.Inputs[0]); JsonNode root = JsonNode.Parse(NodeGraphJson.Serialize(graph, new NodeGraphViewState(Vector2.Zero, 1)));
		root["connections"].AsArray().Add(root["connections"][0].DeepClone()); root["connections"].AsArray().Add(root["connections"][0].DeepClone()); root["connections"][0]["outputIndex"] = 50;
		NodeGraphLoadResult load = NodeGraphJson.Deserialize(root.ToJsonString(), registry);
		Assert.False(load.Success); Assert.Contains(load.Errors, e => e.Contains("port index")); Assert.Contains(load.Errors, e => e.Contains("multiple"));
	}

	[Fact]
	public void FileSaveReplacesAndLoadAndEvaluateProducesStructuredJson() {
		string directory = Path.Combine(Path.GetTempPath(), "FishGfxTests", Guid.NewGuid().ToString("N")), path = Path.Combine(directory, "layout.json");
		try {
			NodeFunctionRegistry registry = Registry(); FunctionNodeGraph graph = new FunctionNodeGraph(); FunctionNode node = graph.CreateNode(Find(registry, nameof(JsonFunctions.Special)), Vector2.Zero);
			NodeGraphJson.SaveFile(path, graph, new NodeGraphViewState(Vector2.Zero, 1)); NodeGraphGraphAssert(path);
			NodeGraphExecutionResult result = NodeGraphJson.LoadAndEvaluateFile(path, registry); string json = NodeGraphJson.SerializeExecutionResult(result);
			Assert.True(result.Success); Assert.Contains("NaN", json); Assert.Contains("Vector2", json); Assert.DoesNotContain(".tmp", Directory.GetFiles(directory));
		} finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
	}

	[Fact]
	public void HeadlessRunnerReturnsDocumentAndMeaningfulExitCodes() {
		string directory = Path.Combine(Path.GetTempPath(), "FishGfxTests", Guid.NewGuid().ToString("N")), path = Path.Combine(directory, "layout.json"); Directory.CreateDirectory(directory);
		TextWriter old = Console.Out; StringWriter output = new StringWriter();
		try {
			NodeFunctionRegistry registry = new NodeFunctionRegistry(); registry.Register(typeof(SampleNodeFunctions)); FunctionNodeGraph graph = new FunctionNodeGraph(); graph.CreateNode(registry.Functions.First(), Vector2.Zero);
			NodeGraphJson.SaveFile(path, graph, new NodeGraphViewState(Vector2.Zero, 1)); Console.SetOut(output);
			Assert.Equal(0, HeadlessRunner.Execute(path)); Assert.True(JsonDocument.Parse(output.ToString()).RootElement.TryGetProperty("success", out _));
			output.GetStringBuilder().Clear(); Assert.Equal(2, HeadlessRunner.Execute(path + ".missing"));
		} finally { Console.SetOut(old); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
	}

	private static void NodeGraphGraphAssert(string path) { Assert.True(File.Exists(path)); Assert.False(File.Exists(path + ".tmp")); }
	private static NodeFunctionRegistry Registry() { NodeFunctionRegistry registry = new NodeFunctionRegistry(); registry.Register(typeof(JsonFunctions)); return registry; }
	private static NodeFunctionDescriptor Find(NodeFunctionRegistry registry, string name) => registry.Functions.Single(f => f.Method.Name == name);

	public static class JsonFunctions {
		[NodeFunction] public static int Constant([NodeBody] int value = 1) => value;
		[NodeFunction] public static int Identity(int value) => value;
		[NodeFunction] public static (double number, Vector2 vector, string text) Special() => (double.NaN, new Vector2(2, 3), null);
	}
}
