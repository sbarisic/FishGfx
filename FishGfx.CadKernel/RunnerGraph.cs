using System.Collections.ObjectModel;

namespace FishGfx.Cad;

public enum RunnerPortType
{
	RunnerFeatures,
	PipeProfile,
	Number,
}

public enum RunnerPortDirection
{
	Input,
	Output,
}

public sealed record RunnerPortDefinition(
	string Name,
	RunnerPortType Type,
	RunnerPortDirection Direction,
	bool Required = true
);

public sealed class RunnerNodeDefinition
{
	public RunnerNodeDefinition(string id, string title, params RunnerPortDefinition[] ports)
	{
		Id = id;
		Title = title;
		Ports = Array.AsReadOnly(ports);
	}

	public string Id { get; }
	public string Title { get; }
	public IReadOnlyList<RunnerPortDefinition> Ports { get; }

	public RunnerPortDefinition FindPort(string name, RunnerPortDirection direction)
	{
		return Ports.FirstOrDefault(port => port.Direction == direction
			&& string.Equals(port.Name, name, StringComparison.Ordinal));
	}
}

public static class RunnerNodes
{
	public const string StartRunner = "cad.start-runner";
	public const string Straight = "cad.straight";
	public const string Bend = "cad.bend";
	public const string CubicBezier = "cad.cubic-bezier";
	public const string CircularPipe = "cad.circular-pipe";
	public const string LoftTransition = "cad.loft-transition";
	public const string ClockingTransition = "cad.clocking-transition";
	public const string RunnerOutput = "cad.runner-output";
	public const string RunnerLength = "cad.runner-length";

	// Read only by the version-one project migrator.
	internal const string LegacyMateReference = "cad.mate-reference";
	internal const string LegacySweepPipe = "cad.sweep-pipe";

	private static readonly ReadOnlyDictionary<string, RunnerNodeDefinition> DefinitionsValue =
		new(new Dictionary<string, RunnerNodeDefinition>(StringComparer.Ordinal)
		{
			[StartRunner] = new RunnerNodeDefinition(
				StartRunner,
				"Start Runner",
				OptionalIn("profile", RunnerPortType.PipeProfile),
				Out("runner", RunnerPortType.RunnerFeatures)
			),
			[Straight] = new RunnerNodeDefinition(
				Straight,
				"Straight",
				In("runner", RunnerPortType.RunnerFeatures),
				Out("runner", RunnerPortType.RunnerFeatures)
			),
			[Bend] = new RunnerNodeDefinition(
				Bend,
				"Bend",
				In("runner", RunnerPortType.RunnerFeatures),
				Out("runner", RunnerPortType.RunnerFeatures)
			),
			[CubicBezier] = new RunnerNodeDefinition(
				CubicBezier,
				"Cubic Bézier",
				In("runner", RunnerPortType.RunnerFeatures),
				Out("runner", RunnerPortType.RunnerFeatures)
			),
			[CircularPipe] = new RunnerNodeDefinition(
				CircularPipe,
				"Circular Pipe",
				Out("profile", RunnerPortType.PipeProfile)
			),
			[LoftTransition] = new RunnerNodeDefinition(
				LoftTransition,
				"Loft Transition",
				In("runner", RunnerPortType.RunnerFeatures),
				In("targetProfile", RunnerPortType.PipeProfile),
				Out("runner", RunnerPortType.RunnerFeatures)
			),
			[ClockingTransition] = new RunnerNodeDefinition(
				ClockingTransition,
				"Clocking Transition",
				In("runner", RunnerPortType.RunnerFeatures),
				Out("runner", RunnerPortType.RunnerFeatures)
			),
			[RunnerOutput] = new RunnerNodeDefinition(
				RunnerOutput,
				"Runner Output",
				In("runner", RunnerPortType.RunnerFeatures)
			),
			[RunnerLength] = new RunnerNodeDefinition(
				RunnerLength,
				"Runner Length",
				In("runner", RunnerPortType.RunnerFeatures),
				Out("length", RunnerPortType.Number)
			),
		});

	public static IReadOnlyDictionary<string, RunnerNodeDefinition> Definitions => DefinitionsValue;

	public static bool TryGet(string id, out RunnerNodeDefinition definition)
	{
		return DefinitionsValue.TryGetValue(id, out definition);
	}

	private static RunnerPortDefinition In(string name, RunnerPortType type) =>
		new(name, type, RunnerPortDirection.Input);

	private static RunnerPortDefinition OptionalIn(string name, RunnerPortType type) =>
		new(name, type, RunnerPortDirection.Input, false);

	private static RunnerPortDefinition Out(string name, RunnerPortType type) =>
		new(name, type, RunnerPortDirection.Output);
}

public sealed class RunnerNode
{
	private readonly Dictionary<string, string> properties;

	public RunnerNode(string definitionId, double x = 0, double y = 0, Guid? id = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
		Id = id ?? Guid.NewGuid();
		DefinitionId = definitionId;
		X = x;
		Y = y;
		properties = new Dictionary<string, string>(StringComparer.Ordinal);
		Properties = properties;
		ApplyDefaults();
	}

	public Guid Id { get; }
	public string DefinitionId { get; internal set; }
	public double X { get; set; }
	public double Y { get; set; }
	public IDictionary<string, string> Properties { get; }
	public bool IsKnown => RunnerNodes.TryGet(DefinitionId, out _);

	internal void ReplaceProperties(IEnumerable<KeyValuePair<string, string>> values)
	{
		properties.Clear();
		foreach (KeyValuePair<string, string> value in values)
		{
			properties[value.Key] = value.Value;
		}
	}

	private void ApplyDefaults()
	{
		switch (DefinitionId)
		{
			case RunnerNodes.StartRunner:
				properties["wallThickness"] = "2";
				break;
			case RunnerNodes.Straight:
				properties["length"] = "100";
				break;
			case RunnerNodes.Bend:
				properties["radius"] = "50";
				properties["angle"] = "45";
				properties["rotation"] = "0";
				break;
			case RunnerNodes.CubicBezier:
				properties["startHandleLength"] = "33.333333333333336";
				properties["control2T"] = "66.66666666666667";
				properties["control2U"] = "0";
				properties["control2V"] = "0";
				properties["endT"] = "100";
				properties["endU"] = "0";
				properties["endV"] = "0";
				properties["endHandleLength"] = "33.333333333333336";
				break;
			case RunnerNodes.CircularPipe:
				properties["outerDiameter"] = "42.4";
				properties["wallThickness"] = "2";
				break;
			case RunnerNodes.LoftTransition:
				properties["length"] = "30";
				properties["rotation"] = "0";
				break;
			case RunnerNodes.ClockingTransition:
				properties["length"] = "20";
				properties["rotation"] = "0";
				break;
		}
	}
}

public sealed record RunnerConnection(
	Guid Id,
	Guid OutputNodeId,
	string OutputPort,
	Guid InputNodeId,
	string InputPort
)
{
	public RunnerConnection(Guid outputNodeId, string outputPort, Guid inputNodeId, string inputPort)
		: this(Guid.NewGuid(), outputNodeId, outputPort, inputNodeId, inputPort)
	{
	}
}

public sealed class RunnerGraph
{
	private readonly List<RunnerNode> nodes = new();
	private readonly List<RunnerConnection> connections = new();

	public RunnerGraph(Guid? id = null)
	{
		Id = id ?? Guid.NewGuid();
		Nodes = nodes.AsReadOnly();
		Connections = connections.AsReadOnly();
	}

	public Guid Id { get; }
	public IReadOnlyList<RunnerNode> Nodes { get; }
	public IReadOnlyList<RunnerConnection> Connections { get; }

	public RunnerNode AddNode(string definitionId, double x = 0, double y = 0)
	{
		RunnerNode node = new(definitionId, x, y);
		nodes.Add(node);
		return node;
	}

	public bool RemoveNode(Guid nodeId)
	{
		int removed = nodes.RemoveAll(node => node.Id == nodeId);
		if (removed == 0)
		{
			return false;
		}

		connections.RemoveAll(connection => connection.OutputNodeId == nodeId
			|| connection.InputNodeId == nodeId);
		return true;
	}

	public bool TryConnect(
		Guid outputNodeId,
		string outputPort,
		Guid inputNodeId,
		string inputPort,
		out RunnerConnection connection,
		out string error
	)
	{
		connection = null;
		error = null;
		RunnerConnection candidate = new(outputNodeId, outputPort, inputNodeId, inputPort);
		List<RunnerConnection> staged = connections.Where(item => item.InputNodeId != inputNodeId
			|| !string.Equals(item.InputPort, inputPort, StringComparison.Ordinal)).ToList();
		staged.Add(candidate);

		if (!ValidateConnections(nodes, staged, out error))
		{
			return false;
		}

		connections.Clear();
		connections.AddRange(staged);
		connection = candidate;
		return true;
	}

	public bool TrySpliceConnection(
		Guid connectionId,
		string definitionId,
		double x,
		double y,
		out RunnerNode node,
		out string error
	)
	{
		node = null;
		error = null;
		RunnerConnection original = connections.FirstOrDefault(item => item.Id == connectionId);

		if (original == null || !RunnerNodes.TryGet(definitionId, out RunnerNodeDefinition definition))
		{
			error = "The selected connection or node definition is unavailable.";
			return false;
		}

		RunnerNode outputNode = nodes.Single(item => item.Id == original.OutputNodeId);
		RunnerNode inputNode = nodes.Single(item => item.Id == original.InputNodeId);
		RunnerNodeDefinition outputDefinition = RunnerNodes.Definitions[outputNode.DefinitionId];
		RunnerNodeDefinition inputDefinition = RunnerNodes.Definitions[inputNode.DefinitionId];
		RunnerPortType type = outputDefinition.FindPort(original.OutputPort, RunnerPortDirection.Output).Type;
		RunnerPortDefinition spliceInput = definition.Ports.SingleOrDefault(port =>
			port.Direction == RunnerPortDirection.Input && port.Type == type);
		RunnerPortDefinition spliceOutput = definition.Ports.SingleOrDefault(port =>
			port.Direction == RunnerPortDirection.Output && port.Type == type);

		if (spliceInput == null || spliceOutput == null)
		{
			error = "The node cannot splice the selected connection type.";
			return false;
		}

		RunnerNode candidateNode = new(definitionId, x, y);
		List<RunnerNode> stagedNodes = new(nodes) { candidateNode };
		List<RunnerConnection> staged = connections.Where(item => item.Id != connectionId).ToList();
		staged.Add(new RunnerConnection(
			original.OutputNodeId,
			original.OutputPort,
			candidateNode.Id,
			spliceInput.Name
		));
		staged.Add(new RunnerConnection(
			candidateNode.Id,
			spliceOutput.Name,
			original.InputNodeId,
			original.InputPort
		));

		if (!ValidateConnections(stagedNodes, staged, out error))
		{
			return false;
		}

		nodes.Add(candidateNode);
		connections.Clear();
		connections.AddRange(staged);
		node = candidateNode;
		return true;
	}

	public bool RemoveConnection(Guid connectionId)
	{
		return connections.RemoveAll(connection => connection.Id == connectionId) > 0;
	}

	public RunnerGraph DeepClone()
	{
		RunnerGraph clone = new(Id);
		foreach (RunnerNode source in nodes)
		{
			RunnerNode node = new(source.DefinitionId, source.X, source.Y, source.Id);
			node.ReplaceProperties(source.Properties);
			clone.AddLoadedNode(node);
		}
		foreach (RunnerConnection connection in connections)
		{
			clone.AddLoadedConnection(connection with { });
		}
		return clone;
	}

	public static RunnerGraph CreateDefault(CadTopologyKind mateKind)
	{
		RunnerGraph graph = new();
		RunnerNode start = graph.AddNode(RunnerNodes.StartRunner, 20, 220);
		RunnerNode profile = graph.AddNode(RunnerNodes.CircularPipe, 300, 20);
		RunnerNode straightA;

		if (mateKind == CadTopologyKind.ClosedProfile)
		{
			RunnerNode loft = graph.AddNode(RunnerNodes.LoftTransition, 300, 220);
			straightA = graph.AddNode(RunnerNodes.Straight, 580, 220);
			graph.ConnectRequired(start, "runner", loft, "runner");
			graph.ConnectRequired(profile, "profile", loft, "targetProfile");
			graph.ConnectRequired(loft, "runner", straightA, "runner");
		}
		else
		{
			straightA = graph.AddNode(RunnerNodes.Straight, 300, 220);
			graph.ConnectRequired(profile, "profile", start, "profile");
			graph.ConnectRequired(start, "runner", straightA, "runner");
		}

		RunnerNode bend = graph.AddNode(RunnerNodes.Bend, 860, 220);
		RunnerNode straightB = graph.AddNode(RunnerNodes.Straight, 1140, 220);
		RunnerNode output = graph.AddNode(RunnerNodes.RunnerOutput, 1420, 170);
		RunnerNode length = graph.AddNode(RunnerNodes.RunnerLength, 1420, 360);
		graph.ConnectRequired(straightA, "runner", bend, "runner");
		graph.ConnectRequired(bend, "runner", straightB, "runner");
		graph.ConnectRequired(straightB, "runner", output, "runner");
		graph.ConnectRequired(straightB, "runner", length, "runner");
		return graph;
	}

	internal void AddLoadedNode(RunnerNode node)
	{
		if (nodes.Any(existing => existing.Id == node.Id))
		{
			throw new InvalidDataException($"Duplicate runner node ID '{node.Id}'.");
		}
		nodes.Add(node);
	}

	internal void AddLoadedConnection(RunnerConnection connection)
	{
		if (connections.Any(existing => existing.Id == connection.Id))
		{
			throw new InvalidDataException($"Duplicate runner connection ID '{connection.Id}'.");
		}
		connections.Add(connection);
	}

	internal void ReplaceConnections(IEnumerable<RunnerConnection> values)
	{
		connections.Clear();
		connections.AddRange(values);
	}

	internal bool TryValidate(out string error)
	{
		return ValidateConnections(nodes, connections, out error);
	}

	private void ConnectRequired(RunnerNode output, string outputPort, RunnerNode input, string inputPort)
	{
		if (!TryConnect(output.Id, outputPort, input.Id, inputPort, out _, out string error))
		{
			throw new InvalidOperationException(error);
		}
	}

	private static bool ValidateConnections(
		IReadOnlyList<RunnerNode> candidateNodes,
		IReadOnlyList<RunnerConnection> candidateConnections,
		out string error
	)
	{
		error = null;
		if (candidateNodes.Select(node => node.Id).Distinct().Count() != candidateNodes.Count)
		{
			error = "Runner node IDs must be unique.";
			return false;
		}
		if (candidateConnections.Select(connection => connection.Id).Distinct().Count()
			!= candidateConnections.Count)
		{
			error = "Runner connection IDs must be unique.";
			return false;
		}
		Dictionary<Guid, RunnerNode> byId = candidateNodes.ToDictionary(node => node.Id);
		HashSet<(Guid, string)> inputs = new();

		foreach (RunnerConnection item in candidateConnections)
		{
			if (!byId.TryGetValue(item.OutputNodeId, out RunnerNode outputNode)
				|| !byId.TryGetValue(item.InputNodeId, out RunnerNode inputNode))
			{
				error = "Both connection nodes must exist in the graph.";
				return false;
			}

			if (!RunnerNodes.TryGet(outputNode.DefinitionId, out RunnerNodeDefinition outputDefinition)
				|| !RunnerNodes.TryGet(inputNode.DefinitionId, out RunnerNodeDefinition inputDefinition))
			{
				error = "Unknown nodes cannot be connected.";
				return false;
			}

			RunnerPortDefinition source = outputDefinition.FindPort(item.OutputPort, RunnerPortDirection.Output);
			RunnerPortDefinition destination = inputDefinition.FindPort(item.InputPort, RunnerPortDirection.Input);

			if (source == null || destination == null || source.Type != destination.Type)
			{
				error = "The selected ports do not exist or have incompatible types.";
				return false;
			}

			if (!inputs.Add((item.InputNodeId, item.InputPort)))
			{
				error = "A node input may have only one connection.";
				return false;
			}
		}

		if (HasCycle(candidateNodes, candidateConnections))
		{
			error = "Runner graphs must remain acyclic.";
			return false;
		}
		return true;
	}

	private static bool HasCycle(
		IReadOnlyList<RunnerNode> candidateNodes,
		IReadOnlyList<RunnerConnection> candidateConnections
	)
	{
		Dictionary<Guid, int> states = candidateNodes.ToDictionary(node => node.Id, _ => 0);
		Dictionary<Guid, Guid[]> outgoing = candidateConnections.GroupBy(connection => connection.OutputNodeId)
			.ToDictionary(group => group.Key, group => group.Select(connection => connection.InputNodeId).Distinct().ToArray());

		bool Visit(Guid nodeId)
		{
			if (states[nodeId] == 1)
				return true;
			if (states[nodeId] == 2)
				return false;
			states[nodeId] = 1;
			if (outgoing.TryGetValue(nodeId, out Guid[] targets))
			{
				foreach (Guid target in targets)
				{
					if (Visit(target))
						return true;
				}
			}
			states[nodeId] = 2;
			return false;
		}

		return candidateNodes.Any(node => Visit(node.Id));
	}
}
