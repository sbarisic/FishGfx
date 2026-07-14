using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using FishGfx.NodeEditor;
using FishGfx.NodeGraph;
using Xunit;

namespace FishGfx.Tests;

public class NodeGraphJsonTests
{
	[Fact]
	public void NullJsonReturnsStructuredFailure()
	{
		NodeGraphLoadResult result = NodeGraphJson.Deserialize(null, Registry());

		Assert.False(result.Success);
		Assert.Contains(result.Errors, error => error.Contains("JSON content", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public void RoundTripPreservesGraphIdsInlineValuesLayoutConnectionsAndView()
	{
		NodeFunctionRegistry registry = Registry();
		FunctionGraph graph = new FunctionGraph();
		FunctionNode source = graph.AddNode(Find(registry, nameof(JsonFunctions.Constant)), new Vector2(12, 34));
		source.Width = 275;
		source.InlineValues[0].Text = "42";
		FunctionNode sink = graph.AddNode(Find(registry, nameof(JsonFunctions.Identity)), new Vector2(500, 80));
		graph.TryConnect(source.Outputs[0], sink.Inputs[0], out _);
		NodeGraphViewState view = new NodeGraphViewState(new Vector2(91, -17), 1.4f);
		string json = NodeGraphJson.Serialize(graph, view);
		NodeGraphLoadResult loaded = NodeGraphJson.Deserialize(json, registry);
		Assert.True(loaded.Success);
		Assert.Equal(graph.Nodes.Select(n => n.Id).OrderBy(id => id), loaded.Graph.Nodes.Select(n => n.Id));
		FunctionNode loadedSource = loaded.Graph.Nodes.Single(n => n.Id == source.Id);
		Assert.Equal("42", loadedSource.InlineValues[0].Text);
		Assert.Equal(275, loadedSource.Width);
		Assert.Single(loaded.Graph.Connections);
		Assert.Equal(view.Pan, loaded.View.Pan);
		Assert.Equal(view.Zoom, loaded.View.Zoom);
		Assert.Equal(json, NodeGraphJson.Serialize(loaded.Graph, loaded.View));
	}

	[Fact]
	public void InvalidDocumentReturnsAllErrorsWithoutChangingExistingGraph()
	{
		NodeFunctionRegistry registry = Registry();
		FunctionGraph original = new FunctionGraph();
		original.AddNode(Find(registry, nameof(JsonFunctions.Constant)), Vector2.Zero);
		JsonNode root = JsonNode.Parse(NodeGraphJson.Serialize(original, new NodeGraphViewState(Vector2.Zero, 1)));
		root["version"] = 99;
		root["viewport"]["zoom"] = 99;
		root["nodes"][0]["function"] = "missing.function";
		root["nodes"][0]["position"]["x"] = "NaN";
		NodeGraphLoadResult load = NodeGraphJson.Deserialize(root.ToJsonString(), registry);
		Assert.False(load.Success);
		Assert.True(load.Errors.Count >= 3);
		Assert.Single(original.Nodes);
	}

	[Fact]
	public void DuplicateConnectionsAndUnknownPortsAreRejected()
	{
		NodeFunctionRegistry registry = Registry();
		FunctionGraph graph = new FunctionGraph();
		FunctionNode source = graph.AddNode(Find(registry, nameof(JsonFunctions.Constant)), Vector2.Zero);
		FunctionNode sink = graph.AddNode(Find(registry, nameof(JsonFunctions.Identity)), Vector2.One);

		graph.TryConnect(source.Outputs[0], sink.Inputs[0], out _);
		JsonNode root = JsonNode.Parse(NodeGraphJson.Serialize(graph, new NodeGraphViewState(Vector2.Zero, 1)));
		root["connections"].AsArray().Add(root["connections"][0].DeepClone());
		root["connections"].AsArray().Add(root["connections"][0].DeepClone());
		root["connections"][0]["from"]["port"] = "missing";
		NodeGraphLoadResult load = NodeGraphJson.Deserialize(root.ToJsonString(), registry);
		Assert.False(load.Success);
		Assert.Contains(load.Errors, e => e.Contains("named port"));
		Assert.Contains(load.Errors, e => e.Contains("multiple"));
	}

	[Fact]
	public void FileSaveReplacesAndLoadAndEvaluateProducesStructuredJson()
	{
		string directory = Path.Combine(Path.GetTempPath(), "FishGfxTests", Guid.NewGuid().ToString("N"));
		string path = Path.Combine(directory, "layout.json");
		try
		{
			NodeFunctionRegistry registry = Registry();
			FunctionGraph graph = new FunctionGraph();

			graph.AddNode(Find(registry, nameof(JsonFunctions.Special)), Vector2.Zero);
			NodeGraphJson.SaveFile(path, graph, new NodeGraphViewState(Vector2.Zero, 1));
			NodeGraphGraphAssert(path);
			NodeGraphExecutionResult result = NodeGraphJson.LoadAndEvaluateFile(path, registry);
			string json = NodeGraphJson.SerializeExecutionResult(result);
			Assert.True(result.Success);
			Assert.Contains("NaN", json);
			Assert.Contains("Vector2", json);
			Assert.Contains("json.special", json);
			Assert.Contains("successfulNodeCount", json);

			JsonElement document = JsonDocument.Parse(json).RootElement;
			JsonElement executionNode = document.GetProperty("nodes")[0];

			Assert.Equal("json.special", executionNode.GetProperty("function").GetString());
			Assert.Equal("success", executionNode.GetProperty("state").GetString());
			string numberType = executionNode
				.GetProperty("outputs")
				.GetProperty("number")
				.GetProperty("type")
				.GetString();

			Assert.Equal("System.Double", numberType);
			Assert.True(executionNode.GetProperty("outputs").TryGetProperty("vector", out _));
			Assert.DoesNotContain(".tmp", Directory.GetFiles(directory));
		}
		finally
		{
			if (Directory.Exists(directory))
			{
				Directory.Delete(directory, true);
			}
		}
	}

	[Fact]
	public void HeadlessRunnerReturnsDocumentAndMeaningfulExitCodes()
	{
		string directory = Path.Combine(Path.GetTempPath(), "FishGfxTests", Guid.NewGuid().ToString("N"));
		string path = Path.Combine(directory, "layout.json");
		Directory.CreateDirectory(directory);
		TextWriter old = Console.Out;
		StringWriter output = new StringWriter();

		try
		{
			NodeFunctionRegistry registry = new NodeFunctionRegistry();
			registry.Register(typeof(SampleNodeFunctions));
			FunctionGraph graph = new FunctionGraph();

			graph.AddNode(registry.Functions.First(), Vector2.Zero);
			NodeGraphJson.SaveFile(path, graph, new NodeGraphViewState(Vector2.Zero, 1));
			Console.SetOut(output);
			Assert.Equal(0, HeadlessRunner.Execute(path));
			JsonElement result = JsonDocument.Parse(output.ToString()).RootElement;

			Assert.Equal(2, result.GetProperty("version").GetInt32());
			Assert.True(result.TryGetProperty("success", out _));
			output.GetStringBuilder().Clear();
			Assert.Equal(2, HeadlessRunner.Execute(path + ".missing"));
		}
		finally
		{
			Console.SetOut(old);

			if (Directory.Exists(directory))
			{
				Directory.Delete(directory, true);
			}
		}
	}

	[Fact]
	public void VersionTwoUsesStableNamesAndRejectsVersionOneDocuments()
	{
		NodeFunctionRegistry registry = Registry();
		FunctionGraph graph = new FunctionGraph();
		FunctionNode source = graph.AddNode(registry.Get("json.constant"), new Vector2(4, 8));

		source.GetInlineValue("value").Text = "7";

		JsonNode root = JsonNode.Parse(NodeGraphJson.Serialize(graph, new NodeGraphViewState(Vector2.One, 1)));

		Assert.Equal(2, root["version"].GetValue<int>());
		Assert.NotNull(root["viewport"]["pan"]["x"]);
		Assert.Equal("json.constant", root["nodes"][0]["function"].GetValue<string>());
		Assert.Equal("7", root["nodes"][0]["inlineValues"]["value"].GetValue<string>());
		Assert.Null(root["nodes"][0]["method"]);

		root["nodes"][0]["inlineValues"].AsObject().Remove("value");

		NodeGraphLoadResult defaulted = NodeGraphJson.Deserialize(root.ToJsonString(), registry);

		Assert.True(defaulted.Success);
		Assert.Equal("1", defaulted.Graph.Nodes[0].GetInlineValue("value").Text);

		root["version"] = 1;

		NodeGraphLoadResult result = NodeGraphJson.Deserialize(root.ToJsonString(), registry);

		Assert.False(result.Success);
		Assert.Contains(result.Errors, error => error.Contains("Unsupported schema version 1"));
	}

	[Fact]
	public void LoaderRejectsUnknownInlineKeysAndCycles()
	{
		NodeFunctionRegistry registry = Registry();
		FunctionGraph graph = new FunctionGraph();
		FunctionNode first = graph.AddNode(registry.Get("json.identity"), Vector2.Zero);
		FunctionNode second = graph.AddNode(registry.Get("json.identity"), Vector2.One);

		graph.TryConnect(first.Outputs[0], second.Inputs[0], out _);

		JsonNode root = JsonNode.Parse(NodeGraphJson.Serialize(graph, new NodeGraphViewState(Vector2.Zero, 1)));
		JsonObject reverse = new JsonObject
		{
			["from"] = new JsonObject
			{
				["node"] = second.Id.ToString(),
				["port"] = "result",
			},
			["to"] = new JsonObject
			{
				["node"] = first.Id.ToString(),
				["port"] = "value",
			},
		};

		root["connections"].AsArray().Add(reverse);

		NodeGraphLoadResult cycle = NodeGraphJson.Deserialize(root.ToJsonString(), registry);

		Assert.False(cycle.Success);
		Assert.Contains(cycle.Errors, error => error.Contains("cycle", StringComparison.OrdinalIgnoreCase));

		JsonNode constant = JsonNode.Parse(NodeGraphJson.Serialize(
			GraphWithConstant(registry),
			new NodeGraphViewState(Vector2.Zero, 1)
		));
		constant["nodes"][0]["inlineValues"]["unknown"] = "1";

		NodeGraphLoadResult inline = NodeGraphJson.Deserialize(constant.ToJsonString(), registry);

		Assert.False(inline.Success);
		Assert.Contains(inline.Errors, error => error.Contains("unknown inline value"));
	}

	private static void NodeGraphGraphAssert(string path)
	{
		Assert.True(File.Exists(path));
		Assert.False(File.Exists(path + ".tmp"));
	}

	private static NodeFunctionRegistry Registry()
	{
		NodeFunctionRegistry registry = new NodeFunctionRegistry();
		registry.Register(typeof(JsonFunctions));
		return registry;
	}

	private static NodeFunctionDescriptor Find(NodeFunctionRegistry registry, string name) =>
		registry.Functions.Single(f => f.Method.Name == name);

	private static FunctionGraph GraphWithConstant(NodeFunctionRegistry registry)
	{
		FunctionGraph graph = new FunctionGraph();

		graph.AddNode(registry.Get("json.constant"), Vector2.Zero);

		return graph;
	}

	public static class JsonFunctions
	{
		[NodeFunction("json.constant")]
		public static int Constant([NodeInline] int value = 1) => value;

		[NodeFunction("json.identity")]
		public static int Identity(int value) => value;

		[NodeFunction("json.special")]
		public static (double number, Vector2 vector, string text) Special() => (double.NaN, new Vector2(2, 3), null);
	}
}
