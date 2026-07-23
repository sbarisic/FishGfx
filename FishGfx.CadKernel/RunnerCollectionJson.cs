using System.Text.Json;
using System.Text.Json.Nodes;

namespace FishGfx.Cad;

public sealed class RunnerCollectionLoadResult
{
	public IReadOnlyList<CadRunner> Runners { get; internal set; } = Array.Empty<CadRunner>();
	public IReadOnlyList<string> Errors { get; internal set; } = Array.Empty<string>();
	public bool Success => Errors.Count == 0;
}

public static class RunnerCollectionJson
{
	public const string Schema = "fishgfx.runner-collection";
	public const int CurrentVersion = 2;

	private static readonly JsonSerializerOptions Options = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true,
	};

	public static string Serialize(IEnumerable<CadRunner> runners)
	{
		JsonArray values = new();
		foreach (CadRunner runner in runners)
		{
			values.Add(new JsonObject
			{
				["id"] = runner.Id,
				["name"] = runner.Name,
				["startMateId"] = runner.StartMateId,
				["graph"] = JsonNode.Parse(RunnerGraphJson.Serialize(runner.Graph)),
			});
		}

		return new JsonObject
		{
			["schema"] = Schema,
			["version"] = CurrentVersion,
			["runners"] = values,
		}.ToJsonString(Options);
	}

	public static RunnerCollectionLoadResult Deserialize(string json)
	{
		if (string.IsNullOrWhiteSpace(json)) return Failure("Runner collection JSON is required.");
		JsonDocument document;
		try
		{
			document = JsonDocument.Parse(json);
		}
		catch (JsonException exception)
		{
			return Failure("Invalid runner collection JSON: " + exception.Message);
		}

		using (document)
		{
			JsonElement root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object)
			{
				return Failure("The runner collection root must be an object.");
			}
			string schema = root.TryGetProperty("schema", out JsonElement schemaElement)
				&& schemaElement.ValueKind == JsonValueKind.String
				? schemaElement.GetString()
				: null;
			if (string.Equals(schema, RunnerGraphJson.Schema, StringComparison.Ordinal))
			{
				return MigrateVersionOne(json);
			}
			if (!string.Equals(schema, Schema, StringComparison.Ordinal)
				|| !root.TryGetProperty("version", out JsonElement version)
				|| version.ValueKind != JsonValueKind.Number
				|| !version.TryGetInt32(out int versionNumber)
				|| versionNumber != CurrentVersion)
			{
				return Failure("The runner collection schema or version is unsupported.");
			}

			List<CadRunner> result = new();
			List<string> errors = new();
			if (!root.TryGetProperty("runners", out JsonElement items) || items.ValueKind != JsonValueKind.Array)
			{
				return Failure("The runner collection requires a runners array.");
			}

			foreach (JsonElement item in items.EnumerateArray())
			{
				if (item.ValueKind != JsonValueKind.Object
					|| !item.TryGetProperty("id", out JsonElement idValue)
					|| !item.TryGetProperty("startMateId", out JsonElement mateValue)
					|| !item.TryGetProperty("graph", out JsonElement graphValue)
					|| idValue.ValueKind != JsonValueKind.String
					|| mateValue.ValueKind != JsonValueKind.String
					|| graphValue.ValueKind != JsonValueKind.Object
					|| !Guid.TryParse(idValue.GetString(), out Guid id)
					|| !Guid.TryParse(mateValue.GetString(), out Guid mateId)
					|| id == Guid.Empty || mateId == Guid.Empty)
				{
					errors.Add("Every runner requires an ID, start mate ID, and graph.");
					continue;
				}
				RunnerGraphLoadResult graph = RunnerGraphJson.Deserialize(graphValue.GetRawText());
				if (!graph.Success)
				{
					errors.AddRange(graph.Errors);
					continue;
				}
				result.Add(new CadRunner
				{
					Id = id,
					Name = item.TryGetProperty("name", out JsonElement name)
						&& name.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(name.GetString())
						? name.GetString()
						: "Runner",
					StartMateId = mateId,
					Graph = graph.Graph,
				});
			}

			if (result.Select(runner => runner.Id).Distinct().Count() != result.Count)
			{
				errors.Add("Runner IDs must be unique.");
			}
			if (result.Select(runner => runner.StartMateId).Distinct().Count() != result.Count)
			{
				errors.Add("A start mate may own only one runner.");
			}
			return new RunnerCollectionLoadResult { Runners = result.AsReadOnly(), Errors = errors.AsReadOnly() };
		}
	}

	private static RunnerCollectionLoadResult MigrateVersionOne(string json)
	{
		RunnerGraphLoadResult loaded = RunnerGraphJson.Deserialize(json);
		if (!loaded.Success) return new RunnerCollectionLoadResult { Errors = loaded.Errors };
		RunnerGraph graph = loaded.Graph;
		RunnerNode[] mateNodes = graph.Nodes.Where(node => node.DefinitionId == RunnerNodes.LegacyMateReference).ToArray();
		RunnerNode[] startNodes = graph.Nodes.Where(node => node.DefinitionId == RunnerNodes.StartRunner).ToArray();
		RunnerNode[] outputNodes = graph.Nodes.Where(node => node.DefinitionId == RunnerNodes.LegacySweepPipe).ToArray();
		if (mateNodes.Length != 1 || startNodes.Length != 1 || outputNodes.Length != 1)
		{
			return Failure("The version-one graph requires exactly one mate, start, and sweep node.");
		}
		RunnerNode mateNode = mateNodes[0];
		RunnerNode startNode = startNodes[0];
		RunnerNode outputNode = outputNodes[0];
		if (!mateNode.Properties.TryGetValue("mateId", out string mateText)
			|| !Guid.TryParse(mateText, out Guid mateId))
		{
			return Failure("The version-one graph cannot be migrated because its mate ID is missing or invalid.");
		}

		outputNode.DefinitionId = RunnerNodes.RunnerOutput;
		startNode.Properties.TryAdd("wallThickness", "2");
		List<RunnerConnection> migrated = new();
		foreach (RunnerConnection connection in graph.Connections)
		{
			if (connection.OutputNodeId == mateNode.Id || connection.InputNodeId == mateNode.Id) continue;
			if (connection.InputNodeId == outputNode.Id && connection.InputPort == "profile")
			{
				migrated.Add(connection with { InputNodeId = startNode.Id, InputPort = "profile" });
				continue;
			}
			migrated.Add(connection with
			{
				OutputPort = connection.OutputPort == "path" ? "runner" : connection.OutputPort,
				InputPort = connection.InputPort == "path" ? "runner" : connection.InputPort,
			});
		}
		graph.RemoveNode(mateNode.Id);
		graph.ReplaceConnections(migrated);
		if (!graph.TryValidate(out string validationError))
		{
			return Failure("The migrated version-one graph is invalid: " + validationError);
		}
		CadRunner runner = new()
		{
			Id = graph.Id,
			Name = "Runner 1",
			StartMateId = mateId,
			Graph = graph,
		};
		return new RunnerCollectionLoadResult { Runners = new[] { runner } };
	}

	private static RunnerCollectionLoadResult Failure(string message)
	{
		return new RunnerCollectionLoadResult { Errors = new[] { message } };
	}
}
