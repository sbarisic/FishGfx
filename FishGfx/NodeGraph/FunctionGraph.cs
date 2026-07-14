using System;
using System.Collections.Generic;
using System.Numerics;

namespace FishGfx.NodeGraph;

public sealed class FunctionGraph
{
	private readonly List<FunctionNode> nodes = new List<FunctionNode>();
	private readonly List<NodeConnection> connections = new List<NodeConnection>();

	public IReadOnlyList<FunctionNode> Nodes { get; }
	public IReadOnlyList<NodeConnection> Connections { get; }

	public FunctionGraph()
	{
		Nodes = nodes.AsReadOnly();
		Connections = connections.AsReadOnly();
	}

	public FunctionNode AddNode(NodeFunctionDescriptor descriptor, Vector2 position)
	{
		FunctionNode node = new FunctionNode(descriptor, position);

		nodes.Add(node);
		InvalidateEvaluation();

		return node;
	}

	internal FunctionNode AddNode(NodeFunctionDescriptor descriptor, Vector2 position, Guid id)
	{
		FunctionNode node = new FunctionNode(descriptor, position, id);

		nodes.Add(node);

		return node;
	}

	public bool TryConnect(NodePort output, NodePort input, out NodeConnection connection)
	{
		connection = null;

		if (output == null)
		{
			throw new ArgumentNullException(nameof(output));
		}

		if (input == null)
		{
			throw new ArgumentNullException(nameof(input));
		}

		if (!nodes.Contains(output.Node) || !nodes.Contains(input.Node))
		{
			return false;
		}

		if (output.Direction != NodePortDirection.Output
			|| input.Direction != NodePortDirection.Input
			|| output.Type != input.Type)
		{
			return false;
		}

		if (TryGetInputConnection(input, out NodeConnection existing))
		{
			connections.Remove(existing);
		}

		connection = new NodeConnection(output, input);
		connections.Add(connection);
		InvalidateEvaluation();

		return true;
	}

	public bool RemoveNode(FunctionNode node)
	{
		if (node == null)
		{
			throw new ArgumentNullException(nameof(node));
		}

		if (!nodes.Remove(node))
		{
			return false;
		}

		connections.RemoveAll(connection => connection.Input.Node == node || connection.Output.Node == node);
		InvalidateEvaluation();

		return true;
	}

	public bool Disconnect(NodeConnection connection)
	{
		if (connection == null)
		{
			throw new ArgumentNullException(nameof(connection));
		}

		if (!connections.Remove(connection))
		{
			return false;
		}

		InvalidateEvaluation();

		return true;
	}

	public bool TryGetInputConnection(NodePort input, out NodeConnection connection)
	{
		if (input == null)
		{
			throw new ArgumentNullException(nameof(input));
		}

		foreach (NodeConnection candidate in connections)
		{
			if (candidate.Input == input)
			{
				connection = candidate;
				return true;
			}
		}

		connection = null;

		return false;
	}

	public void InvalidateEvaluation()
	{
		foreach (FunctionNode node in nodes)
		{
			node.EvaluationState = NodeEvaluationState.NotEvaluated;
			node.EvaluationMessage = null;

			foreach (NodePort output in node.Outputs)
			{
				output.Value = null;
			}
		}
	}
}
