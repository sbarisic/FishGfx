using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace FishGfx.NodeGraph {
	public enum NodePortDirection { Input, Output }
	public enum NodeEvaluationState { NotEvaluated, Success, Error, Skipped }

	public sealed class NodePort {
		public Guid Id { get; } = Guid.NewGuid();
		public string Name { get; }
		public Type Type { get; }
		public NodePortDirection Direction { get; }
		public FunctionNode Node { get; internal set; }
		public object Value { get; internal set; }
		internal NodePort(string name, Type type, NodePortDirection direction) { Name = name; Type = type; Direction = direction; }
	}

	public sealed class NodeBodyValue {
		public NodeParameterDescriptor Descriptor { get; }
		public string Name => Descriptor.Name;
		public Type Type => Descriptor.Type;
		public string Text { get; set; }
		public object Value { get; internal set; }
		public bool IsValid { get; internal set; }

		internal NodeBodyValue(NodeParameterDescriptor descriptor) {
			Descriptor = descriptor;
			object initial = descriptor.Parameter.HasDefaultValue && descriptor.Parameter.DefaultValue != DBNull.Value ? descriptor.Parameter.DefaultValue : NodeValueConverter.Default(Type);
			Text = NodeValueConverter.Format(initial, Type);
			IsValid = NodeValueConverter.TryParse(Text, Type, out object parsed);
			Value = parsed;
		}

		public bool Parse() { IsValid = NodeValueConverter.TryParse(Text, Type, out object parsed); if (IsValid) Value = parsed; return IsValid; }
	}

	public sealed class FunctionNode {
		public Guid Id { get; } = Guid.NewGuid();
		public NodeFunctionDescriptor Function { get; }
		public string Title => Function.Title;
		public Vector2 Position { get; set; }
		public float Width { get; set; } = 240;
		public List<NodePort> Inputs { get; } = new List<NodePort>();
		public List<NodePort> Outputs { get; } = new List<NodePort>();
		public List<NodeBodyValue> BodyValues { get; } = new List<NodeBodyValue>();
		public NodeEvaluationState EvaluationState { get; internal set; }
		public string EvaluationMessage { get; internal set; }

		internal FunctionNode(NodeFunctionDescriptor function, Vector2 position) {
			Function = function; Position = position;
			foreach (NodeParameterDescriptor parameter in function.Parameters) {
				if (parameter.IsBody) BodyValues.Add(new NodeBodyValue(parameter));
				else Inputs.Add(NewPort(parameter.Name, parameter.Type, NodePortDirection.Input));
			}
			foreach (NodeOutputDescriptor output in function.Outputs) Outputs.Add(NewPort(output.Name, output.Type, NodePortDirection.Output));
		}

		private NodePort NewPort(string name, Type type, NodePortDirection direction) { NodePort port = new NodePort(name, type, direction) { Node = this }; return port; }
	}

	public sealed class NodeConnection {
		public Guid Id { get; } = Guid.NewGuid();
		public NodePort Output { get; }
		public NodePort Input { get; }
		internal NodeConnection(NodePort output, NodePort input) { Output = output; Input = input; }
	}

	public sealed class FunctionNodeGraph {
		public List<FunctionNode> Nodes { get; } = new List<FunctionNode>();
		public List<NodeConnection> Connections { get; } = new List<NodeConnection>();
		public FunctionNode CreateNode(NodeFunctionDescriptor descriptor, Vector2 position) { FunctionNode node = new FunctionNode(descriptor ?? throw new ArgumentNullException(nameof(descriptor)), position); Nodes.Add(node); InvalidateEvaluation(); return node; }

		public NodeConnection Connect(NodePort a, NodePort b) {
			if (a == null) throw new ArgumentNullException(nameof(a)); if (b == null) throw new ArgumentNullException(nameof(b));
			NodePort output = a.Direction == NodePortDirection.Output ? a : b;
			NodePort input = a.Direction == NodePortDirection.Input ? a : b;
			if (output.Direction != NodePortDirection.Output || input.Direction != NodePortDirection.Input || output.Type != input.Type) return null;
			Connections.RemoveAll(c => c.Input == input);
			NodeConnection connection = new NodeConnection(output, input); Connections.Add(connection); InvalidateEvaluation(); return connection;
		}

		public void Remove(FunctionNode node) { Connections.RemoveAll(c => c.Input.Node == node || c.Output.Node == node); Nodes.Remove(node); InvalidateEvaluation(); }
		public void Remove(NodeConnection connection) { Connections.Remove(connection); InvalidateEvaluation(); }
		public NodeConnection ConnectionAtInput(NodePort input) => Connections.FirstOrDefault(c => c.Input == input);
		public void InvalidateEvaluation() {
			foreach (FunctionNode node in Nodes) { node.EvaluationState = NodeEvaluationState.NotEvaluated; node.EvaluationMessage = null; foreach (NodePort output in node.Outputs) output.Value = null; }
		}
	}

	public sealed class NodeEvaluationResult {
		public int SuccessfulNodes { get; internal set; }
		public int FailedNodes { get; internal set; }
		public bool Success => FailedNodes == 0;
		public string Summary => Success ? $"Evaluated {SuccessfulNodes} nodes" : $"{FailedNodes} errors, {SuccessfulNodes} nodes evaluated";
	}

	public sealed class FunctionNodeEvaluator {
		private enum VisitState { Visiting, Done }
		public NodeEvaluationResult Evaluate(FunctionNodeGraph graph) {
			if (graph == null) throw new ArgumentNullException(nameof(graph));
			foreach (FunctionNode node in graph.Nodes) { node.EvaluationState = NodeEvaluationState.NotEvaluated; node.EvaluationMessage = null; foreach (NodePort output in node.Outputs) output.Value = null; }
			Dictionary<FunctionNode, VisitState> states = new Dictionary<FunctionNode, VisitState>();
			List<FunctionNode> stack = new List<FunctionNode>();
			foreach (FunctionNode node in graph.Nodes) Visit(node, graph, states, stack);
			return new NodeEvaluationResult { SuccessfulNodes = graph.Nodes.Count(n => n.EvaluationState == NodeEvaluationState.Success), FailedNodes = graph.Nodes.Count(n => n.EvaluationState == NodeEvaluationState.Error || n.EvaluationState == NodeEvaluationState.Skipped) };
		}

		private static bool Visit(FunctionNode node, FunctionNodeGraph graph, Dictionary<FunctionNode, VisitState> states, List<FunctionNode> stack) {
			if (states.TryGetValue(node, out VisitState state)) {
				if (state == VisitState.Done) return node.EvaluationState == NodeEvaluationState.Success;
				int cycleStart = stack.IndexOf(node);
				for (int i = cycleStart; i < stack.Count; i++) Fail(stack[i], "Cycle detected", NodeEvaluationState.Error);
				return false;
			}
			states[node] = VisitState.Visiting; stack.Add(node);
			bool dependencyFailed = false;
			foreach (NodePort input in node.Inputs) {
				NodeConnection connection = graph.ConnectionAtInput(input);
				if (connection != null && !Visit(connection.Output.Node, graph, states, stack)) dependencyFailed = true;
			}
			if (node.EvaluationState == NodeEvaluationState.Error) dependencyFailed = true;
			if (dependencyFailed && node.EvaluationState != NodeEvaluationState.Error) Fail(node, "Dependency failed", NodeEvaluationState.Skipped);
			else if (!dependencyFailed) Invoke(node, graph);
			stack.RemoveAt(stack.Count - 1); states[node] = VisitState.Done;
			return node.EvaluationState == NodeEvaluationState.Success;
		}

		private static void Invoke(FunctionNode node, FunctionNodeGraph graph) {
			try {
				object[] args = new object[node.Function.Parameters.Count]; int inputIndex = 0, bodyIndex = 0;
				for (int i = 0; i < args.Length; i++) {
					NodeParameterDescriptor parameter = node.Function.Parameters[i];
					if (parameter.IsBody) {
						NodeBodyValue body = node.BodyValues[bodyIndex++];
						if (!body.Parse()) { Fail(node, $"Invalid {body.Name}: {body.Text}", NodeEvaluationState.Error); return; }
						args[i] = body.Value;
					} else {
						NodePort input = node.Inputs[inputIndex++]; NodeConnection connection = graph.ConnectionAtInput(input);
						args[i] = connection == null ? NodeValueConverter.Default(input.Type) : connection.Output.Value;
					}
				}
				object result = node.Function.Method.Invoke(null, args);
				if (node.Outputs.Count == 1 && node.Function.Outputs[0].TupleIndex < 0) node.Outputs[0].Value = result;
				else if (node.Outputs.Count > 0) {
					List<object> values = new List<object>(); FlattenTupleValues(result, values);
					for (int i = 0; i < node.Outputs.Count; i++) node.Outputs[i].Value = values[i];
				}
				node.EvaluationState = NodeEvaluationState.Success; node.EvaluationMessage = "OK";
			} catch (TargetInvocationException ex) { Fail(node, ex.InnerException?.Message ?? ex.Message, NodeEvaluationState.Error); }
			catch (Exception ex) { Fail(node, ex.Message, NodeEvaluationState.Error); }
		}

		private static void FlattenTupleValues(object value, List<object> result) {
			if (!(value is ITuple tuple)) return;
			for (int i = 0; i < tuple.Length; i++) {
				object item = tuple[i];
				if (i == 7 && item is ITuple) FlattenTupleValues(item, result); else result.Add(item);
			}
		}
		private static void Fail(FunctionNode node, string message, NodeEvaluationState state) { node.EvaluationState = state; node.EvaluationMessage = message; }
	}
}
