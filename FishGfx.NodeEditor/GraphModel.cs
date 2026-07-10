using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace FishGfx.NodeEditor {
	internal enum PortType { Scalar, Vector }
	internal enum PortDirection { Input, Output }

	internal sealed class NodePort {
		internal Guid Id { get; } = Guid.NewGuid();
		internal string Name { get; }
		internal PortType Type { get; }
		internal PortDirection Direction { get; }
		internal Node Node { get; set; }

		internal NodePort(string name, PortType type, PortDirection direction) {
			Name = name; Type = type; Direction = direction;
		}
	}

	internal sealed class NodeValue {
		internal string Name { get; }
		internal float Value { get; set; }
		internal NodeValue(string name, float value) { Name = name; Value = value; }
	}

	internal sealed class Node {
		internal Guid Id { get; } = Guid.NewGuid();
		internal string Title { get; }
		internal Vector2 Position { get; set; }
		internal float Width { get; }
		internal List<NodePort> Inputs { get; } = new List<NodePort>();
		internal List<NodePort> Outputs { get; } = new List<NodePort>();
		internal List<NodeValue> Values { get; } = new List<NodeValue>();
		internal float Height => 48 + Math.Max(Inputs.Count + Outputs.Count, Values.Count) * 30 + 18;

		internal Node(string title, Vector2 position, float width = 210) { Title = title; Position = position; Width = width; }
		internal Node AddPort(string name, PortType type, PortDirection direction) {
			NodePort port = new NodePort(name, type, direction) { Node = this };
			(direction == PortDirection.Input ? Inputs : Outputs).Add(port);
			return this;
		}
		internal Node AddValue(string name, float value) { Values.Add(new NodeValue(name, value)); return this; }
	}

	internal sealed class NodeConnection {
		internal Guid Id { get; } = Guid.NewGuid();
		internal NodePort Output { get; }
		internal NodePort Input { get; }
		internal NodeConnection(NodePort output, NodePort input) { Output = output; Input = input; }
	}

	internal sealed class NodeGraph {
		internal List<Node> Nodes { get; } = new List<Node>();
		internal List<NodeConnection> Connections { get; } = new List<NodeConnection>();

		internal NodeConnection Connect(NodePort a, NodePort b) {
			NodePort output = a.Direction == PortDirection.Output ? a : b;
			NodePort input = a.Direction == PortDirection.Input ? a : b;
			if (output.Direction != PortDirection.Output || input.Direction != PortDirection.Input || output.Type != input.Type)
				return null;
			Connections.RemoveAll(c => c.Input == input);
			NodeConnection connection = new NodeConnection(output, input);
			Connections.Add(connection);
			return connection;
		}

		internal void Remove(Node node) {
			Connections.RemoveAll(c => c.Input.Node == node || c.Output.Node == node);
			Nodes.Remove(node);
		}

		internal void Remove(NodeConnection connection) => Connections.Remove(connection);
		internal NodeConnection ConnectionAtInput(NodePort input) => Connections.FirstOrDefault(c => c.Input == input);
	}

	internal static class NodeTemplates {
		internal static readonly string[] Names = { "Scalar Source", "Vector Source", "Scalar Process", "Vector Process", "Scalar Output", "Vector Output" };

		internal static Node Create(string name, Vector2 position) {
			switch (name) {
				case "Scalar Source": return new Node(name, position).AddValue("value", 1).AddPort("out", PortType.Scalar, PortDirection.Output);
				case "Vector Source": return new Node(name, position).AddValue("x", 2).AddValue("y", 3).AddPort("out", PortType.Vector, PortDirection.Output);
				case "Scalar Process": return new Node(name, position).AddPort("A", PortType.Scalar, PortDirection.Input).AddPort("B", PortType.Scalar, PortDirection.Input).AddPort("out", PortType.Scalar, PortDirection.Output);
				case "Vector Process": return new Node(name, position).AddPort("A", PortType.Vector, PortDirection.Input).AddPort("B", PortType.Vector, PortDirection.Input).AddPort("out", PortType.Vector, PortDirection.Output);
				case "Scalar Output": return new Node(name, position).AddPort("value", PortType.Scalar, PortDirection.Input);
				case "Vector Output": return new Node(name, position).AddPort("value", PortType.Vector, PortDirection.Input);
				default: throw new ArgumentException("Unknown node template", nameof(name));
			}
		}
	}
}
