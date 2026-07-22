using System.Collections.ObjectModel;

namespace FishGfx.Cad;

public enum RunnerPortType
{
	MateFrame,
	RunnerPath,
	PipeProfile,
	CadSolid,
	Number,
}

public enum RunnerPortDirection
{
	Input,
	Output,
}

public sealed record RunnerPortDefinition(string Name, RunnerPortType Type, RunnerPortDirection Direction);

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
		return Ports.FirstOrDefault(port =>
			port.Direction == direction
			&& string.Equals(port.Name, name, StringComparison.Ordinal)
		);
	}
}

public static class RunnerNodes
{
	public const string MateReference = "cad.mate-reference";
	public const string StartRunner = "cad.start-runner";
	public const string Straight = "cad.straight";
	public const string Bend = "cad.bend";
	public const string CircularPipe = "cad.circular-pipe";
	public const string SweepPipe = "cad.sweep-pipe";
	public const string RunnerLength = "cad.runner-length";

	private static readonly ReadOnlyDictionary<string, RunnerNodeDefinition> DefinitionsValue =
		new(new Dictionary<string, RunnerNodeDefinition>(StringComparer.Ordinal)
		{
			[MateReference] = new RunnerNodeDefinition(
				MateReference,
				"Mate Reference",
				Out("mate", RunnerPortType.MateFrame)
			),
			[StartRunner] = new RunnerNodeDefinition(
				StartRunner,
				"Start Runner",
				In("mate", RunnerPortType.MateFrame),
				Out("path", RunnerPortType.RunnerPath)
			),
			[Straight] = new RunnerNodeDefinition(
				Straight,
				"Straight",
				In("path", RunnerPortType.RunnerPath),
				Out("path", RunnerPortType.RunnerPath)
			),
			[Bend] = new RunnerNodeDefinition(
				Bend,
				"Bend",
				In("path", RunnerPortType.RunnerPath),
				Out("path", RunnerPortType.RunnerPath)
			),
			[CircularPipe] = new RunnerNodeDefinition(
				CircularPipe,
				"Circular Pipe",
				Out("profile", RunnerPortType.PipeProfile)
			),
			[SweepPipe] = new RunnerNodeDefinition(
				SweepPipe,
				"Sweep Pipe",
				In("path", RunnerPortType.RunnerPath),
				In("profile", RunnerPortType.PipeProfile),
				Out("solid", RunnerPortType.CadSolid)
			),
			[RunnerLength] = new RunnerNodeDefinition(
				RunnerLength,
				"Runner Length",
				In("path", RunnerPortType.RunnerPath),
				Out("length", RunnerPortType.Number)
			),
		});

	public static IReadOnlyDictionary<string, RunnerNodeDefinition> Definitions => DefinitionsValue;

	public static bool TryGet(string id, out RunnerNodeDefinition definition)
	{
		return DefinitionsValue.TryGetValue(id, out definition);
	}

	private static RunnerPortDefinition In(string name, RunnerPortType type)
	{
		return new RunnerPortDefinition(name, type, RunnerPortDirection.Input);
	}

	private static RunnerPortDefinition Out(string name, RunnerPortType type)
	{
		return new RunnerPortDefinition(name, type, RunnerPortDirection.Output);
	}
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

	public string DefinitionId { get; }

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
			case RunnerNodes.MateReference:
				properties["mateId"] = string.Empty;
				break;
			case RunnerNodes.Straight:
				properties["length"] = "100";
				break;
			case RunnerNodes.Bend:
				properties["radius"] = "50";
				properties["angle"] = "45";
				properties["rotation"] = "0";
				break;
			case RunnerNodes.CircularPipe:
				properties["outerDiameter"] = "42.4";
				properties["wallThickness"] = "2";
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

		connections.RemoveAll(connection =>
			connection.OutputNodeId == nodeId || connection.InputNodeId == nodeId
		);

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
		RunnerNode outputNode = nodes.FirstOrDefault(node => node.Id == outputNodeId);
		RunnerNode inputNode = nodes.FirstOrDefault(node => node.Id == inputNodeId);

		if (outputNode == null || inputNode == null)
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

		RunnerPortDefinition source = outputDefinition.FindPort(outputPort, RunnerPortDirection.Output);
		RunnerPortDefinition destination = inputDefinition.FindPort(inputPort, RunnerPortDirection.Input);

		if (source == null || destination == null || source.Type != destination.Type)
		{
			error = "The selected ports do not exist or have incompatible types.";
			return false;
		}

		connections.RemoveAll(candidate =>
			candidate.InputNodeId == inputNodeId
			&& string.Equals(candidate.InputPort, inputPort, StringComparison.Ordinal)
		);
		RunnerConnection candidateConnection = new(outputNodeId, outputPort, inputNodeId, inputPort);
		connections.Add(candidateConnection);

		if (HasCycle())
		{
			connections.Remove(candidateConnection);
			error = "Runner graphs must remain acyclic.";
			return false;
		}

		connection = candidateConnection;
		return true;
	}

	public bool RemoveConnection(Guid connectionId)
	{
		return connections.RemoveAll(connection => connection.Id == connectionId) > 0;
	}

	public static RunnerGraph CreateDefault(Guid mateId)
	{
		RunnerGraph graph = new();
		RunnerNode mate = graph.AddNode(RunnerNodes.MateReference, 20, 220);
		mate.Properties["mateId"] = mateId.ToString("D");
		RunnerNode start = graph.AddNode(RunnerNodes.StartRunner, 280, 220);
		RunnerNode straightA = graph.AddNode(RunnerNodes.Straight, 540, 220);
		RunnerNode bend = graph.AddNode(RunnerNodes.Bend, 800, 220);
		RunnerNode straightB = graph.AddNode(RunnerNodes.Straight, 1060, 220);
		RunnerNode profile = graph.AddNode(RunnerNodes.CircularPipe, 1060, 20);
		RunnerNode sweep = graph.AddNode(RunnerNodes.SweepPipe, 1320, 170);
		RunnerNode length = graph.AddNode(RunnerNodes.RunnerLength, 1320, 360);

		graph.ConnectRequired(mate, "mate", start, "mate");
		graph.ConnectRequired(start, "path", straightA, "path");
		graph.ConnectRequired(straightA, "path", bend, "path");
		graph.ConnectRequired(bend, "path", straightB, "path");
		graph.ConnectRequired(straightB, "path", sweep, "path");
		graph.ConnectRequired(profile, "profile", sweep, "profile");
		graph.ConnectRequired(straightB, "path", length, "path");

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

	private void ConnectRequired(RunnerNode output, string outputPort, RunnerNode input, string inputPort)
	{
		if (!TryConnect(output.Id, outputPort, input.Id, inputPort, out _, out string error))
		{
			throw new InvalidOperationException(error);
		}
	}

	private bool HasCycle()
	{
		Dictionary<Guid, int> states = nodes.ToDictionary(node => node.Id, _ => 0);
		Dictionary<Guid, Guid[]> outgoing = connections
			.GroupBy(connection => connection.OutputNodeId)
			.ToDictionary(
				group => group.Key,
				group => group.Select(connection => connection.InputNodeId).Distinct().ToArray()
			);

		bool Visit(Guid nodeId)
		{
			if (states[nodeId] == 1)
			{
				return true;
			}

			if (states[nodeId] == 2)
			{
				return false;
			}

			states[nodeId] = 1;

			if (outgoing.TryGetValue(nodeId, out Guid[] targets))
			{
				foreach (Guid target in targets)
				{
					if (Visit(target))
					{
						return true;
					}
				}
			}

			states[nodeId] = 2;
			return false;
		}

		return nodes.Any(node => Visit(node.Id));
	}
}
