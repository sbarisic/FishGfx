using System.Text.Json;
using System.Text.Json.Serialization;

namespace FishGfx.Cad;

public sealed class RunnerGraphLoadResult
{
	public RunnerGraph Graph { get; internal set; }

	public IReadOnlyList<string> Errors { get; internal set; } = Array.Empty<string>();

	public bool Success => Graph != null && Errors.Count == 0;
}

public static class RunnerGraphJson
{
	public const string Schema = "fishgfx.runner-graph";

	public const int CurrentVersion = 1;

	private static readonly JsonSerializerOptions Options = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	};

	public static string Serialize(RunnerGraph graph)
	{
		ArgumentNullException.ThrowIfNull(graph);
		RunnerGraphDocument document = new()
		{
			Schema = Schema,
			Version = CurrentVersion,
			Id = graph.Id,
			Nodes = graph.Nodes.Select(node => new RunnerNodeDto
			{
				Id = node.Id,
				DefinitionId = node.DefinitionId,
				X = node.X,
				Y = node.Y,
				Properties = new Dictionary<string, string>(node.Properties, StringComparer.Ordinal),
			}).ToList(),
			Connections = graph.Connections.Select(connection => new RunnerConnectionDto
			{
				Id = connection.Id,
				OutputNodeId = connection.OutputNodeId,
				OutputPort = connection.OutputPort,
				InputNodeId = connection.InputNodeId,
				InputPort = connection.InputPort,
			}).ToList(),
		};

		return JsonSerializer.Serialize(document, Options);
	}

	public static RunnerGraphLoadResult Deserialize(string json)
	{
		if (json == null)
		{
			return Failure("Runner graph JSON is required.");
		}

		RunnerGraphDocument document;

		try
		{
			document = JsonSerializer.Deserialize<RunnerGraphDocument>(json, Options);
		}
		catch (JsonException exception)
		{
			return Failure("Invalid runner graph JSON: " + exception.Message);
		}

		List<string> errors = Validate(document);

		if (errors.Count > 0)
		{
			return new RunnerGraphLoadResult { Errors = errors.AsReadOnly() };
		}

		try
		{
			RunnerGraph graph = new(document.Id);

			foreach (RunnerNodeDto item in document.Nodes)
			{
				RunnerNode node = new(item.DefinitionId, item.X, item.Y, item.Id);
				node.ReplaceProperties(item.Properties ?? new Dictionary<string, string>());
				graph.AddLoadedNode(node);
			}

			foreach (RunnerConnectionDto item in document.Connections)
			{
				graph.AddLoadedConnection(new RunnerConnection(
					item.Id,
					item.OutputNodeId,
					item.OutputPort,
					item.InputNodeId,
					item.InputPort
				));
			}

			return new RunnerGraphLoadResult { Graph = graph };
		}
		catch (Exception exception) when (
			exception is ArgumentException
			|| exception is InvalidDataException
		)
		{
			return Failure(exception.Message);
		}
	}

	private static List<string> Validate(RunnerGraphDocument document)
	{
		List<string> errors = new();

		if (document == null)
		{
			errors.Add("The runner graph document is empty.");
			return errors;
		}

		if (!string.Equals(document.Schema, Schema, StringComparison.Ordinal))
		{
			errors.Add($"Unsupported runner graph schema '{document.Schema}'.");
		}

		if (document.Version != CurrentVersion)
		{
			errors.Add($"Unsupported runner graph version '{document.Version}'.");
		}

		if (document.Id == Guid.Empty)
		{
			errors.Add("Runner graph ID is required.");
		}

		if (document.Nodes == null || document.Connections == null)
		{
			errors.Add("Runner graph nodes and connections are required.");
			return errors;
		}

		if (document.Nodes.Any(node =>
			node == null || node.Id == Guid.Empty || string.IsNullOrWhiteSpace(node.DefinitionId)
		))
		{
			errors.Add("Every runner node requires an ID and definition ID.");
		}

		if (document.Nodes.Where(node => node != null).Select(node => node.Id).Distinct().Count()
			!= document.Nodes.Count)
		{
			errors.Add("Runner node IDs must be unique.");
		}

		if (document.Connections.Any(connection =>
			connection == null
			|| connection.Id == Guid.Empty
			|| connection.OutputNodeId == Guid.Empty
			|| connection.InputNodeId == Guid.Empty
			|| string.IsNullOrWhiteSpace(connection.OutputPort)
			|| string.IsNullOrWhiteSpace(connection.InputPort)
		))
		{
			errors.Add("Every runner connection requires IDs and named ports.");
		}

		return errors;
	}

	private static RunnerGraphLoadResult Failure(string error)
	{
		return new RunnerGraphLoadResult { Errors = new[] { error } };
	}

	private sealed class RunnerGraphDocument
	{
		public string Schema { get; set; }

		public int Version { get; set; }

		public Guid Id { get; set; }

		public List<RunnerNodeDto> Nodes { get; set; }

		public List<RunnerConnectionDto> Connections { get; set; }
	}

	private sealed class RunnerNodeDto
	{
		public Guid Id { get; set; }

		public string DefinitionId { get; set; }

		public double X { get; set; }

		public double Y { get; set; }

		public Dictionary<string, string> Properties { get; set; }
	}

	private sealed class RunnerConnectionDto
	{
		public Guid Id { get; set; }

		public Guid OutputNodeId { get; set; }

		public string OutputPort { get; set; }

		public Guid InputNodeId { get; set; }

		public string InputPort { get; set; }
	}
}
