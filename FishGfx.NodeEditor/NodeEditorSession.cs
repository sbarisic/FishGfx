using System;
using System.Collections.Generic;
using System.Numerics;
using FishGfx.NodeGraph;

namespace FishGfx.NodeEditor;

internal sealed class NodeEditorSession
{
	private readonly NodeFunctionRegistry registry = new NodeFunctionRegistry();
	private readonly FunctionGraphEvaluator evaluator = new FunctionGraphEvaluator();

	internal FunctionGraph Graph { get; private set; }
	internal NodeEvaluationResult EvaluationResult { get; private set; }
	internal IReadOnlyList<NodeFunctionDescriptor> Functions => registry.Functions;

	internal NodeEditorSession()
	{
		registry.Register(typeof(SampleNodeFunctions));
		ResetToSample();
	}

	internal FunctionNode AddNode(NodeFunctionDescriptor descriptor, Vector2 position)
	{
		FunctionNode node = Graph.AddNode(descriptor, position);

		EvaluationResult = null;

		return node;
	}

	internal bool TryConnect(NodePort output, NodePort input, out NodeConnection connection)
	{
		bool connected = Graph.TryConnect(output, input, out connection);

		if (connected)
		{
			EvaluationResult = null;
		}

		return connected;
	}

	internal void RemoveNode(FunctionNode node)
	{
		if (Graph.RemoveNode(node))
		{
			EvaluationResult = null;
		}
	}

	internal void Disconnect(NodeConnection connection)
	{
		if (Graph.Disconnect(connection))
		{
			EvaluationResult = null;
		}
	}

	internal void InvalidateEvaluation()
	{
		Graph.InvalidateEvaluation();
		EvaluationResult = null;
	}

	internal NodeEvaluationResult Evaluate()
	{
		EvaluationResult = evaluator.Evaluate(Graph);

		return EvaluationResult;
	}

	internal void Save(string path, NodeGraphViewState view)
	{
		NodeGraphJson.SaveFile(path, Graph, view);
	}

	internal bool TryLoad(string path, out NodeGraphViewState view, out IReadOnlyList<string> errors)
	{
		NodeGraphLoadResult load = NodeGraphJson.LoadFile(path, registry);

		if (!load.Success)
		{
			view = default;
			errors = load.Errors;

			return false;
		}

		Graph = load.Graph;
		EvaluationResult = null;
		view = load.View;
		errors = Array.Empty<string>();

		return true;
	}

	private void ResetToSample()
	{
		Graph = new FunctionGraph();

		FunctionNode scalarA = Graph.AddNode(registry.Get("value.scalar"), new Vector2(20, 710));
		FunctionNode scalarB = Graph.AddNode(registry.Get("value.scalar"), new Vector2(20, 510));
		FunctionNode add = Graph.AddNode(registry.Get("math.add"), new Vector2(350, 650));
		FunctionNode vector = Graph.AddNode(registry.Get("value.vector2"), new Vector2(20, 260));
		FunctionNode multiply = Graph.AddNode(registry.Get("vector.multiply"), new Vector2(690, 500));
		FunctionNode split = Graph.AddNode(registry.Get("vector.split"), new Vector2(1010, 500));
		FunctionNode display = Graph.AddNode(registry.Get("output.display"), new Vector2(1320, 580));

		scalarA.GetInlineValue("value").Text = "2";
		scalarB.GetInlineValue("value").Text = "3";

		Graph.TryConnect(scalarA.GetOutput("result"), add.GetInput("a"), out _);
		Graph.TryConnect(scalarB.GetOutput("result"), add.GetInput("b"), out _);
		Graph.TryConnect(vector.GetOutput("result"), multiply.GetInput("vector"), out _);
		Graph.TryConnect(add.GetOutput("result"), multiply.GetInput("scalar"), out _);
		Graph.TryConnect(multiply.GetOutput("result"), split.GetInput("vector"), out _);
		Graph.TryConnect(split.GetOutput("x"), display.GetInput("value"), out _);

		Evaluate();
	}
}
