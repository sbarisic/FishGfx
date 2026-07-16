using System;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using FishGfx.NodeGraph;
using Xunit;

namespace FishGfx.Tests;

public class VisualProgramJsonTests
{
	[Fact]
	public void UnknownDefinitionsLoadAsPreservedDisabledPlaceholders()
	{
		VisualProgram program = VisualProgram.CreateDefault(VisualNodeCatalog.CreateCore());
		VisualNode node = program.Functions[0].Graph.AddNode(CoreVisualNodes.TextLiteral, Vector2.Zero);
		JsonNode json = JsonNode.Parse(VisualProgramJson.Serialize(program));
		JsonNode nodeJson = json["functions"][0]["nodes"]
			.AsArray()
			.Single(candidate => candidate["id"].GetValue<Guid>() == node.Id);

		nodeJson["definition"] = "extension.missing";

		VisualProgramLoadResult load = VisualProgramJson.Deserialize(
			json.ToJsonString(),
			VisualNodeCatalog.CreateCore()
		);

		Assert.True(load.Success, string.Join(Environment.NewLine, load.Errors));
		VisualNode placeholder = load.Program.Functions[0].Graph.GetNode(node.Id);
		Assert.True(placeholder.IsMissingDefinition);
		Assert.Equal("extension.missing", placeholder.DefinitionId);
		Assert.Contains(load.Diagnostics, diagnostic => diagnostic.Code == "VPG031" && diagnostic.NodeId == node.Id);
		Assert.Contains("extension.missing", VisualProgramJson.Serialize(load.Program));
		Assert.False(new CSharpProgramGenerator().Generate(load.Program).Success);
	}

	[Fact]
	public void RejectsUnsupportedVersionsAndMalformedConnections()
	{
		VisualProgram program = VisualProgram.CreateDefault(VisualNodeCatalog.CreateCore());
		JsonNode version = JsonNode.Parse(VisualProgramJson.Serialize(program));

		version["version"] = 99;

		VisualProgramLoadResult unsupported = VisualProgramJson.Deserialize(
			version.ToJsonString(),
			VisualNodeCatalog.CreateCore()
		);

		Assert.False(unsupported.Success);
		Assert.Contains(unsupported.Errors, error => error.Contains("Unsupported schema version 99"));

		JsonNode malformed = JsonNode.Parse(VisualProgramJson.Serialize(program));
		malformed["functions"][0]["connections"].AsArray().Add(
			new JsonObject
			{
				["id"] = Guid.NewGuid(),
				["fromNode"] = Guid.NewGuid(),
				["fromPort"] = "next",
				["toNode"] = Guid.NewGuid(),
				["toPort"] = "in",
			}
		);

		VisualProgramLoadResult badConnection = VisualProgramJson.Deserialize(
			malformed.ToJsonString(),
			VisualNodeCatalog.CreateCore()
		);

		Assert.False(badConnection.Success);
		Assert.Contains(badConnection.Errors, error => error.Contains("missing endpoint"));
	}

	[Fact]
	public void AtomicSaveLeavesNoTemporaryFile()
	{
		string directory = System.IO.Path.Combine(
			System.IO.Path.GetTempPath(),
			"FishGfxTests",
			Guid.NewGuid().ToString("N")
		);
		string path = System.IO.Path.Combine(directory, "program.fishcode.json");

		try
		{
			VisualProgram program = VisualProgram.CreateDefault(VisualNodeCatalog.CreateCore());

			VisualProgramJson.SaveFile(path, program);

			Assert.True(System.IO.File.Exists(path));
			Assert.False(System.IO.File.Exists(path + ".tmp"));
			Assert.True(VisualProgramJson.LoadFile(path, VisualNodeCatalog.CreateCore()).Success);
		}
		finally
		{
			if (System.IO.Directory.Exists(directory))
			{
				System.IO.Directory.Delete(directory, true);
			}
		}
	}

	[Fact]
	public void RejectsDuplicateConnectionIdsAndNullCollectionEntries()
	{
		VisualProgram program = VisualProgram.CreateDefault(VisualNodeCatalog.CreateCore());
		VisualGraph graph = program.Functions[0].Graph;
		VisualNode entry = graph.Nodes.Single();
		VisualNode first = graph.AddNode(CoreVisualNodes.Comment, Vector2.Zero);
		VisualNode second = graph.AddNode(CoreVisualNodes.Comment, Vector2.One);

		Assert.True(graph.TryConnect(entry.GetOutput("next"), first.GetInput("in"), out _));
		Assert.True(graph.TryConnect(first.GetOutput("next"), second.GetInput("in"), out _));

		JsonNode duplicate = JsonNode.Parse(VisualProgramJson.Serialize(program));
		JsonArray connections = duplicate["functions"][0]["connections"].AsArray();

		connections[1]["id"] = connections[0]["id"].GetValue<Guid>();

		VisualProgramLoadResult duplicateResult = VisualProgramJson.Deserialize(
			duplicate.ToJsonString(),
			VisualNodeCatalog.CreateCore()
		);

		Assert.False(duplicateResult.Success);
		Assert.Contains(duplicateResult.Errors, error => error.Contains("duplicate connection ids"));

		JsonNode nullNode = JsonNode.Parse(VisualProgramJson.Serialize(program));
		nullNode["functions"][0]["nodes"].AsArray().Add(null);

		VisualProgramLoadResult nullResult = VisualProgramJson.Deserialize(
			nullNode.ToJsonString(),
			VisualNodeCatalog.CreateCore()
		);

		Assert.False(nullResult.Success);
		Assert.Contains(nullResult.Errors, error => error.Contains("null collection entries"));
	}

	[Fact]
	public void NodeDefinitionsRejectAmbiguousOrIncompatiblePorts()
	{
		VisualPortDefinition input = new VisualPortDefinition(
			"value",
			"Value",
			VisualPortKind.Value,
			VisualPortDirection.Input,
			VisualValueType.Integer
		);

		Assert.Throws<ArgumentException>(() => new VisualNodeDefinition(
			"test.duplicate",
			"Duplicate",
			"Tests",
			VisualNodeRole.Expression,
			new[] { input, input }
		));
		Assert.Throws<ArgumentException>(() => new VisualPortDefinition(
			"flow",
			"Flow",
			VisualPortKind.Execution,
			VisualPortDirection.Input,
			VisualValueType.Integer
		));
		Assert.Throws<ArgumentException>(() => new VisualPortDefinition(
			"result",
			"Result",
			VisualPortKind.Value,
			VisualPortDirection.Output,
			VisualValueType.Integer,
			true,
			"0"
		));
	}

	[Fact]
	public void KnownNodesWithTamperedPortsProduceDiagnosticsInsteadOfGeneratorFailures()
	{
		VisualProgram program = VisualProgram.CreateDefault(VisualNodeCatalog.CreateCore());
		VisualNode write = program.Functions[0].Graph.AddNode(CoreVisualNodes.ConsoleWriteLine, Vector2.Zero);
		JsonNode json = JsonNode.Parse(VisualProgramJson.Serialize(program));
		JsonNode writeJson = json["functions"][0]["nodes"]
			.AsArray()
			.Single(node => node["id"].GetValue<Guid>() == write.Id);
		JsonArray ports = writeJson["ports"].AsArray();
		JsonNode valuePort = ports.Single(port => port["name"].GetValue<string>() == "value");

		ports.Remove(valuePort);

		VisualProgramLoadResult load = VisualProgramJson.Deserialize(
			json.ToJsonString(),
			VisualNodeCatalog.CreateCore()
		);

		Assert.True(load.Success, string.Join(Environment.NewLine, load.Errors));
		Assert.Contains(load.Diagnostics, diagnostic => diagnostic.Code == "VPG036");
		Assert.False(new CSharpProgramGenerator().Generate(load.Program).Success);
	}
}
