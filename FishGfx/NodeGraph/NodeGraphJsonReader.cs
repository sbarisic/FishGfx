using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace FishGfx.NodeGraph;

public static partial class NodeGraphJson
{
	public static NodeGraphLoadResult Deserialize(string json, NodeFunctionRegistry registry)
	{
		if (registry == null)
		{
			throw new ArgumentNullException(nameof(registry));
		}

		if (json == null)
		{
			return Failure("JSON content is required.");
		}

		NodeGraphDocument document;

		try
		{
			document = JsonSerializer.Deserialize<NodeGraphDocument>(json, FileOptions);
		}
		catch (JsonException exception)
		{
			return Failure($"Invalid JSON: {exception.Message}");
		}

		if (document == null)
		{
			return Failure("The document is empty.");
		}

		List<string> errors = new List<string>();

		ValidateDocument(document, registry, errors);

		if (errors.Count > 0)
		{
			return new NodeGraphLoadResult
			{
				Errors = errors,
			};
		}

		return BuildGraph(document, registry);
	}

	private static void ValidateDocument(
		NodeGraphDocument document,
		NodeFunctionRegistry registry,
		List<string> errors
	)
	{
		if (document.Version != CurrentVersion)
		{
			errors.Add($"Unsupported schema version {document.Version}; expected {CurrentVersion}.");
		}

		ValidateViewport(document.Viewport, errors);

		if (document.Nodes == null)
		{
			errors.Add("The nodes collection is required.");
			document.Nodes = new List<NodeGraphNodeDto>();
		}

		if (document.Connections == null)
		{
			errors.Add("The connections collection is required.");
			document.Connections = new List<NodeGraphConnectionDto>();
		}

		ValidateNodeIds(document.Nodes, errors);

		Dictionary<Guid, NodeFunctionDescriptor> descriptors = ValidateNodes(document.Nodes, registry, errors);

		ValidateConnections(document, descriptors, errors);
	}

	private static void ValidateViewport(NodeGraphViewportDto viewport, List<string> errors)
	{
		if (viewport == null
			|| viewport.Pan == null
			|| !Finite(viewport.Pan.X, viewport.Pan.Y, viewport.Zoom)
			|| viewport.Zoom < 0.35f
			|| viewport.Zoom > 2.5f)
		{
			errors.Add("Viewport pan/zoom is invalid.");
		}
	}

	private static void ValidateNodeIds(IEnumerable<NodeGraphNodeDto> nodes, List<string> errors)
	{
		foreach (IGrouping<Guid, NodeGraphNodeDto> duplicate in nodes
			.GroupBy(node => node.Id)
			.Where(group => group.Key == Guid.Empty || group.Count() > 1))
		{
			errors.Add($"Node id {duplicate.Key} is empty or duplicated.");
		}
	}

	private static Dictionary<Guid, NodeFunctionDescriptor> ValidateNodes(
		IEnumerable<NodeGraphNodeDto> nodes,
		NodeFunctionRegistry registry,
		List<string> errors
	)
	{
		Dictionary<Guid, NodeFunctionDescriptor> descriptors = new Dictionary<Guid, NodeFunctionDescriptor>();

		foreach (NodeGraphNodeDto node in nodes)
		{
			if (node.Position == null
				|| !Finite(node.Position.X, node.Position.Y, node.Width)
				|| node.Width <= 0)
			{
				errors.Add($"Node {node.Id} has invalid layout values.");
			}

			if (!registry.TryGet(node.Function, out NodeFunctionDescriptor descriptor))
			{
				errors.Add($"Node {node.Id} references unknown function '{node.Function}'.");
				continue;
			}

			if (!descriptors.ContainsKey(node.Id))
			{
				descriptors.Add(node.Id, descriptor);
			}

			ValidateInlineValues(node, descriptor, errors);
		}

		return descriptors;
	}

	private static void ValidateInlineValues(
		NodeGraphNodeDto node,
		NodeFunctionDescriptor descriptor,
		List<string> errors
	)
	{
		Dictionary<string, string> values = node.InlineValues
			?? new Dictionary<string, string>(StringComparer.Ordinal);
		Dictionary<string, NodeParameterDescriptor> inlineParameters = descriptor.Parameters
			.Where(parameter => parameter.IsInline)
			.ToDictionary(parameter => parameter.Name, StringComparer.Ordinal);

		foreach (KeyValuePair<string, string> value in values)
		{
			if (!inlineParameters.TryGetValue(value.Key, out NodeParameterDescriptor parameter))
			{
				errors.Add($"Node {node.Id} has unknown inline value '{value.Key}'.");
				continue;
			}

			if (value.Value == null || !NodeValueConverter.TryParse(value.Value, parameter.Type, out _))
			{
				errors.Add($"Node {node.Id} inline value '{value.Key}' is invalid.");
			}
		}
	}

	private static void ValidateConnections(
		NodeGraphDocument document,
		IReadOnlyDictionary<Guid, NodeFunctionDescriptor> descriptors,
		List<string> errors
	)
	{
		HashSet<(Guid Node, string Port)> occupiedInputs = new HashSet<(Guid Node, string Port)>();
		Dictionary<Guid, List<Guid>> dependencies = new Dictionary<Guid, List<Guid>>();

		foreach (NodeGraphNodeDto node in document.Nodes)
		{
			dependencies.TryAdd(node.Id, new List<Guid>());
		}

		foreach (NodeGraphConnectionDto connection in document.Connections)
		{
			if (!TryResolveConnection(
				connection,
				descriptors,
				out NodeOutputDescriptor output,
				out NodeParameterDescriptor input
			))
			{
				errors.Add("A connection references a missing node or named port.");
				continue;
			}

			if (output.Type != input.Type)
			{
				errors.Add(
					$"Connection {connection.From.Node}:{connection.From.Port} -> "
					+ $"{connection.To.Node}:{connection.To.Port} has mismatched port types."
				);
			}

			if (!occupiedInputs.Add((connection.To.Node, connection.To.Port)))
			{
				errors.Add($"Node {connection.To.Node} input '{connection.To.Port}' has multiple connections.");
			}

			dependencies[connection.To.Node].Add(connection.From.Node);
		}

		if (ContainsCycle(dependencies))
		{
			errors.Add("The graph contains a cycle.");
		}
	}

	private static bool TryResolveConnection(
		NodeGraphConnectionDto connection,
		IReadOnlyDictionary<Guid, NodeFunctionDescriptor> descriptors,
		out NodeOutputDescriptor output,
		out NodeParameterDescriptor input
	)
	{
		output = null;
		input = null;

		if (connection?.From == null
			|| connection.To == null
			|| !descriptors.TryGetValue(connection.From.Node, out NodeFunctionDescriptor outputFunction)
			|| !descriptors.TryGetValue(connection.To.Node, out NodeFunctionDescriptor inputFunction))
		{
			return false;
		}

		output = outputFunction.Outputs.SingleOrDefault(candidate =>
			string.Equals(candidate.Name, connection.From.Port, StringComparison.Ordinal)
		);
		input = inputFunction.Parameters.SingleOrDefault(candidate =>
			!candidate.IsInline
			&& string.Equals(candidate.Name, connection.To.Port, StringComparison.Ordinal)
		);

		return output != null && input != null;
	}

	private static bool ContainsCycle(IReadOnlyDictionary<Guid, List<Guid>> dependencies)
	{
		Dictionary<Guid, int> states = new Dictionary<Guid, int>();

		foreach (Guid node in dependencies.Keys)
		{
			if (VisitForCycle(node, dependencies, states))
			{
				return true;
			}
		}

		return false;
	}

	private static bool VisitForCycle(
		Guid node,
		IReadOnlyDictionary<Guid, List<Guid>> dependencies,
		Dictionary<Guid, int> states
	)
	{
		if (states.TryGetValue(node, out int state))
		{
			return state == 1;
		}

		states[node] = 1;

		foreach (Guid dependency in dependencies[node])
		{
			if (VisitForCycle(dependency, dependencies, states))
			{
				return true;
			}
		}

		states[node] = 2;

		return false;
	}

	private static NodeGraphLoadResult BuildGraph(
		NodeGraphDocument document,
		NodeFunctionRegistry registry
	)
	{
		FunctionGraph graph = new FunctionGraph();
		Dictionary<Guid, FunctionNode> nodes = new Dictionary<Guid, FunctionNode>();

		foreach (NodeGraphNodeDto dto in document.Nodes)
		{
			FunctionNode node = graph.AddNode(
				registry.Get(dto.Function),
				new Vector2(dto.Position.X, dto.Position.Y),
				dto.Id
			);

			node.Width = dto.Width;
			ApplyInlineValues(node, dto.InlineValues);
			nodes.Add(dto.Id, node);
		}

		foreach (NodeGraphConnectionDto dto in document.Connections)
		{
			FunctionNode outputNode = nodes[dto.From.Node];
			FunctionNode inputNode = nodes[dto.To.Node];

			graph.TryConnect(
				outputNode.GetOutput(dto.From.Port),
				inputNode.GetInput(dto.To.Port),
				out _
			);
		}

		graph.InvalidateEvaluation();

		return new NodeGraphLoadResult
		{
			Graph = graph,
			View = new NodeGraphViewState(
				new Vector2(document.Viewport.Pan.X, document.Viewport.Pan.Y),
				document.Viewport.Zoom
			),
		};
	}

	private static void ApplyInlineValues(
		FunctionNode node,
		IReadOnlyDictionary<string, string> values
	)
	{
		if (values == null)
		{
			return;
		}

		foreach (KeyValuePair<string, string> value in values)
		{
			NodeInlineValue inlineValue = node.GetInlineValue(value.Key);

			inlineValue.Text = value.Value;
			inlineValue.Parse();
		}
	}

	private static bool Finite(params float[] values)
	{
		return values.All(float.IsFinite);
	}
}
