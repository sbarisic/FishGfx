using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace FishGfx.NodeGraph;

public sealed class FunctionGraphEvaluator
{
	private enum VisitState
	{
		Visiting,
		Done,
	}

	public NodeEvaluationResult Evaluate(FunctionGraph graph)
	{
		if (graph == null)
		{
			throw new ArgumentNullException(nameof(graph));
		}

		graph.InvalidateEvaluation();

		Dictionary<FunctionNode, VisitState> states = new Dictionary<FunctionNode, VisitState>();
		List<FunctionNode> stack = new List<FunctionNode>();

		foreach (FunctionNode node in graph.Nodes)
		{
			Visit(node, graph, states, stack);
		}

		return new NodeEvaluationResult
		{
			SuccessfulNodeCount = graph.Nodes.Count(node => node.EvaluationState == NodeEvaluationState.Success),
			FailedNodeCount = graph.Nodes.Count(node =>
				node.EvaluationState == NodeEvaluationState.Error
				|| node.EvaluationState == NodeEvaluationState.Skipped
			),
		};
	}

	private static bool Visit(
		FunctionNode node,
		FunctionGraph graph,
		Dictionary<FunctionNode, VisitState> states,
		List<FunctionNode> stack
	)
	{
		if (states.TryGetValue(node, out VisitState state))
		{
			if (state == VisitState.Done)
			{
				return node.EvaluationState == NodeEvaluationState.Success;
			}

			int cycleStart = stack.IndexOf(node);

			for (int index = cycleStart; index < stack.Count; index++)
			{
				Fail(stack[index], "Cycle detected", NodeEvaluationState.Error);
			}

			return false;
		}

		states[node] = VisitState.Visiting;
		stack.Add(node);

		bool dependencyFailed = false;

		foreach (NodePort input in node.Inputs)
		{
			if (graph.TryGetInputConnection(input, out NodeConnection connection)
				&& !Visit(connection.Output.Node, graph, states, stack))
			{
				dependencyFailed = true;
			}
		}

		if (node.EvaluationState == NodeEvaluationState.Error)
		{
			dependencyFailed = true;
		}

		if (dependencyFailed && node.EvaluationState != NodeEvaluationState.Error)
		{
			Fail(node, "Dependency failed", NodeEvaluationState.Skipped);
		}
		else if (!dependencyFailed)
		{
			Invoke(node, graph);
		}

		stack.RemoveAt(stack.Count - 1);
		states[node] = VisitState.Done;

		return node.EvaluationState == NodeEvaluationState.Success;
	}

	private static void Invoke(FunctionNode node, FunctionGraph graph)
	{
		try
		{
			object[] arguments = CreateArguments(node, graph);

			if (arguments == null)
			{
				return;
			}

			object result = node.Function.Method.Invoke(null, arguments);

			PublishOutputs(node, result);

			node.EvaluationState = NodeEvaluationState.Success;
			node.EvaluationMessage = "OK";
		}
		catch (TargetInvocationException exception)
		{
			Fail(
				node,
				exception.InnerException?.Message ?? exception.Message,
				NodeEvaluationState.Error
			);
		}
		catch (Exception exception)
		{
			Fail(node, exception.Message, NodeEvaluationState.Error);
		}
	}

	private static object[] CreateArguments(FunctionNode node, FunctionGraph graph)
	{
		object[] arguments = new object[node.Function.Parameters.Count];
		int inputCursor = 0;
		int inlineIndex = 0;

		for (int parameterIndex = 0; parameterIndex < arguments.Length; parameterIndex++)
		{
			NodeParameterDescriptor parameter = node.Function.Parameters[parameterIndex];

			if (parameter.IsInline)
			{
				NodeInlineValue inlineValue = node.InlineValues[inlineIndex];

				inlineIndex++;

				if (!inlineValue.Parse())
				{
					Fail(
						node,
						$"Invalid {inlineValue.Name}: {inlineValue.Text}",
						NodeEvaluationState.Error
					);

					return null;
				}

				arguments[parameterIndex] = inlineValue.Value;
				continue;
			}

			NodePort input = node.Inputs[inputCursor];

			inputCursor++;

			arguments[parameterIndex] = graph.TryGetInputConnection(input, out NodeConnection connection)
				? connection.Output.Value
				: NodeValueConverter.Default(input.Type);
		}

		return arguments;
	}

	private static void PublishOutputs(FunctionNode node, object result)
	{
		if (node.Outputs.Count == 1 && node.Function.Outputs[0].TupleIndex < 0)
		{
			node.Outputs[0].Value = result;
			return;
		}

		if (node.Outputs.Count == 0)
		{
			return;
		}

		List<object> values = new List<object>();

		FlattenTupleValues(result, values);

		for (int index = 0; index < node.Outputs.Count; index++)
		{
			node.Outputs[index].Value = values[index];
		}
	}

	private static void FlattenTupleValues(object value, List<object> result)
	{
		if (!(value is ITuple tuple))
		{
			return;
		}

		for (int index = 0; index < tuple.Length; index++)
		{
			object item = tuple[index];

			if (index == 7 && item is ITuple)
			{
				FlattenTupleValues(item, result);
			}
			else
			{
				result.Add(item);
			}
		}
	}

	private static void Fail(FunctionNode node, string message, NodeEvaluationState state)
	{
		node.EvaluationState = state;
		node.EvaluationMessage = message;
	}
}
