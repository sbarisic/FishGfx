using System.Text.Json;
using System.Text.Json.Nodes;

namespace FishGfx.Cad;

public sealed class RunnerCollectionLoadResult
{
	public IReadOnlyList<CadRunner> Runners { get; internal set; } = Array.Empty<CadRunner>();
	public IReadOnlyList<CadCollectorSystem> CollectorSystems { get; internal set; } =
		Array.Empty<CadCollectorSystem>();
	public IReadOnlyList<string> Errors { get; internal set; } = Array.Empty<string>();
	public bool Success => Errors.Count == 0;
}

public static class RunnerCollectionJson
{
	public const string Schema = "fishgfx.runner-collection";
	public const int CurrentVersion = 3;

	private static readonly JsonSerializerOptions Options = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true,
	};

	public static string Serialize(IEnumerable<CadRunner> runners)
	{
		return SerializeCore(runners, Array.Empty<CadCollectorSystem>());
	}

	public static string Serialize(ManifoldProject project)
	{
		ArgumentNullException.ThrowIfNull(project);
		return SerializeCore(project.Runners, project.CollectorSystems);
	}

	private static string SerializeCore(
		IEnumerable<CadRunner> runners,
		IEnumerable<CadCollectorSystem> collectorSystems
	)
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
			["collectorSystems"] = JsonSerializer.SerializeToNode(
				collectorSystems.Select(ToDto).ToArray(),
				Options
			),
		}.ToJsonString(Options);
	}

	public static RunnerCollectionLoadResult Deserialize(string json)
	{
		if (string.IsNullOrWhiteSpace(json))
			return Failure("Runner collection JSON is required.");
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
				|| versionNumber is not 2 and not CurrentVersion)
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
			List<CadCollectorSystem> collectors = new();
			if (versionNumber >= 3)
			{
				if (!root.TryGetProperty("collectorSystems", out JsonElement collectorItems)
					|| collectorItems.ValueKind != JsonValueKind.Array)
				{
					errors.Add("The version-three runner collection requires a collectorSystems array.");
				}
				else
				{
					foreach (JsonElement item in collectorItems.EnumerateArray())
					{
						try
						{
							CollectorDto dto = JsonSerializer.Deserialize<CollectorDto>(
								item.GetRawText(),
								Options
							);
							collectors.Add(FromDto(dto));
						}
						catch (Exception exception) when (exception is JsonException
							or ArgumentException or InvalidOperationException or InvalidDataException)
						{
							errors.Add("Invalid collector system: " + exception.Message);
						}
					}
				}
			}
			if (collectors.Select(system => system.Id).Distinct().Count() != collectors.Count)
			{
				errors.Add("Collector system IDs must be unique.");
			}
			Guid[] boundRunnerIds = collectors.SelectMany(system => system.Inlets)
				.Where(inlet => inlet.Binding != null)
				.Select(inlet => inlet.Binding.RunnerId)
				.ToArray();
			if (boundRunnerIds.Distinct().Count() != boundRunnerIds.Length)
			{
				errors.Add("A runner may belong to only one collector system.");
			}
			HashSet<Guid> runnerIds = result.Select(runner => runner.Id).ToHashSet();
			if (boundRunnerIds.Any(id => !runnerIds.Contains(id)))
			{
				errors.Add("A collector system references a missing runner.");
			}
			Dictionary<Guid, RunnerGraph> graphs = result
				.GroupBy(runner => runner.Id)
				.ToDictionary(group => group.Key, group => group.First().Graph);
			foreach (CadCollectorSystem collector in collectors)
			{
				if (!CollectorSystemTransaction.ValidateSystem(
					collector,
					graphs,
					out string validationError
				))
				{
					errors.Add(validationError);
				}
			}
			return new RunnerCollectionLoadResult
			{
				Runners = result.AsReadOnly(),
				CollectorSystems = collectors.AsReadOnly(),
				Errors = errors.AsReadOnly(),
			};
		}
	}

	private static CollectorDto ToDto(CadCollectorSystem system)
	{
		return new CollectorDto
		{
			Id = system.Id,
			Name = system.Name,
			OutletFrame = system.OutletFrame,
			OutletProfile = system.OutletProfile,
			OutletStubLength = system.OutletStubLength,
			MergeLength = system.MergeLength,
			OverlapLength = system.OverlapLength,
			BranchEndHandleLength = system.BranchEndHandleLength,
			GenerationRevision = system.GenerationRevision,
			Inlets = system.Inlets.Select(inlet => new CollectorInletDto
			{
				Id = inlet.Id,
				Name = inlet.Name,
				LocalFrame = inlet.LocalFrame,
				MergeStation = inlet.MergeStation,
				BranchStartHandleLength = inlet.BranchStartHandleLength,
				ClockingTransitionLength = inlet.ClockingTransitionLength,
				Binding = inlet.Binding == null
					? null
					: new CollectorBindingDto
					{
						RunnerId = inlet.Binding.RunnerId,
						TerminalBezierNodeId = inlet.Binding.TerminalBezierNodeId,
						ClockingTransitionNodeId = inlet.Binding.ClockingTransitionNodeId,
					},
			}).ToList(),
		};
	}

	private static CadCollectorSystem FromDto(CollectorDto dto)
	{
		if (dto == null || dto.Id == Guid.Empty || string.IsNullOrWhiteSpace(dto.Name)
			|| dto.Inlets == null || dto.Inlets.Count < 2)
		{
			throw new InvalidDataException("A collector requires an ID, name, and at least two inlets.");
		}
		CadCollectorSystem system = new()
		{
			Id = dto.Id,
			Name = dto.Name,
			OutletFrame = dto.OutletFrame,
			OutletProfile = dto.OutletProfile,
			OutletStubLength = dto.OutletStubLength,
			MergeLength = dto.MergeLength,
			OverlapLength = dto.OverlapLength,
			BranchEndHandleLength = dto.BranchEndHandleLength,
			Inlets = dto.Inlets.Select(item =>
			{
				if (item == null
					|| item.Id == Guid.Empty
					|| string.IsNullOrWhiteSpace(item.Name)
					|| item.Binding == null
					|| item.Binding.RunnerId == Guid.Empty
					|| item.Binding.TerminalBezierNodeId == Guid.Empty)
				{
					throw new InvalidDataException("Every collector inlet requires stable IDs and a binding.");
				}
				return new CadCollectorInlet
				{
					Id = item.Id,
					Name = item.Name,
					LocalFrame = item.LocalFrame,
					MergeStation = item.MergeStation,
					BranchStartHandleLength = item.BranchStartHandleLength,
					ClockingTransitionLength = item.ClockingTransitionLength,
					Binding = new CadCollectorBinding
					{
						RunnerId = item.Binding.RunnerId,
						TerminalBezierNodeId = item.Binding.TerminalBezierNodeId,
						ClockingTransitionNodeId = item.Binding.ClockingTransitionNodeId,
					},
				};
			}).ToList(),
		};
		system.SetGenerationRevision(dto.GenerationRevision);
		return system;
	}

	private static RunnerCollectionLoadResult MigrateVersionOne(string json)
	{
		RunnerGraphLoadResult loaded = RunnerGraphJson.Deserialize(json);
		if (!loaded.Success)
			return new RunnerCollectionLoadResult { Errors = loaded.Errors };
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
			if (connection.OutputNodeId == mateNode.Id || connection.InputNodeId == mateNode.Id)
				continue;
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

	private sealed class CollectorDto
	{
		public Guid Id { get; set; }
		public string Name { get; set; }
		public CadFrame OutletFrame { get; set; }
		public PipeProfile OutletProfile { get; set; }
		public double OutletStubLength { get; set; }
		public double MergeLength { get; set; }
		public double OverlapLength { get; set; }
		public double BranchEndHandleLength { get; set; }
		public long GenerationRevision { get; set; }
		public List<CollectorInletDto> Inlets { get; set; }
	}

	private sealed class CollectorInletDto
	{
		public Guid Id { get; set; }
		public string Name { get; set; }
		public CadFrame LocalFrame { get; set; }
		public double MergeStation { get; set; }
		public double BranchStartHandleLength { get; set; }
		public double ClockingTransitionLength { get; set; }
		public CollectorBindingDto Binding { get; set; }
	}

	private sealed class CollectorBindingDto
	{
		public Guid RunnerId { get; set; }
		public Guid TerminalBezierNodeId { get; set; }
		public Guid? ClockingTransitionNodeId { get; set; }
	}
}
