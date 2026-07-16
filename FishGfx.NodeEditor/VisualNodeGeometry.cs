using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using FishGfx.NodeGraph;

namespace FishGfx.NodeEditor;

internal static class VisualNodeGeometry
{
	internal const float HeaderHeight = 42;
	internal const float PortRadius = 8;
	internal const float RowHeight = 30;

	internal static int PortRowCount(VisualNode node)
	{
		return Math.Max(node.Inputs.Count, node.Outputs.Count);
	}

	internal static IReadOnlyList<VisualEditableField> Fields(VisualNode node)
	{
		List<VisualEditableField> fields = node.Properties
			.Where(property => property.Key == "value" || property.Key == "text" || property.Key == "name")
			.OrderBy(property => property.Key, StringComparer.Ordinal)
			.Select(property => new VisualEditableField(node, property.Key))
			.ToList();

		fields.AddRange(
			node.Inputs
				.Where(port => port.Kind == VisualPortKind.Value && port.Optional)
				.Select(port => new VisualEditableField(port))
		);

		return fields;
	}

	internal static float HeightOf(VisualNode node)
	{
		return 68 + (PortRowCount(node) + Fields(node).Count) * RowHeight;
	}

	internal static Bounds BoundsOf(VisualNode node)
	{
		return new Bounds(node.Position.X, node.Position.Y, node.Width, HeightOf(node));
	}

	internal static Bounds HeaderOf(VisualNode node)
	{
		return new Bounds(
			node.Position.X,
			node.Position.Y + HeightOf(node) - HeaderHeight,
			node.Width,
			HeaderHeight
		);
	}

	internal static Vector2 PortPosition(VisualPort port)
	{
		IReadOnlyList<VisualPort> ports = port.Direction == VisualPortDirection.Input
			? port.Node.Inputs
			: port.Node.Outputs;
		int index = IndexOf(ports, port);
		float y = port.Node.Position.Y + HeightOf(port.Node) - HeaderHeight - 21 - index * RowHeight;
		float x = port.Direction == VisualPortDirection.Input
			? port.Node.Position.X
			: port.Node.Position.X + port.Node.Width;

		return new Vector2(x, y);
	}

	internal static Bounds FieldBounds(VisualNode node, int index)
	{
		return new Bounds(
			node.Position.X + 82,
			node.Position.Y + HeightOf(node) - HeaderHeight - 33 - (PortRowCount(node) + index) * RowHeight,
			node.Width - 98,
			25
		);
	}

	internal static bool NearConnection(Vector2 point, VisualConnection connection, float tolerance)
	{
		Vector2 start = PortPosition(connection.Output);
		Vector2 end = PortPosition(connection.Input);
		Vector2 direction = connection.Kind == VisualPortKind.Execution
			? new Vector2(Math.Max(60, Math.Abs(end.X - start.X) * .45f), 0)
			: new Vector2(Math.Max(45, Math.Abs(end.X - start.X) * .35f), 0);
		Vector2 previous = start;

		for (int index = 1; index <= 32; index++)
		{
			float t = index / 32f;
			float u = 1 - t;
			Vector2 current = u * u * u * start
				+ 3 * u * u * t * (start + direction)
				+ 3 * u * t * t * (end - direction)
				+ t * t * t * end;

			if (DistanceToSegment(point, previous, current) <= tolerance)
			{
				return true;
			}

			previous = current;
		}

		return false;
	}

	private static int IndexOf(IReadOnlyList<VisualPort> ports, VisualPort port)
	{
		for (int index = 0; index < ports.Count; index++)
		{
			if (ports[index] == port)
			{
				return index;
			}
		}

		return -1;
	}

	private static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
	{
		Vector2 segment = end - start;
		float lengthSquared = segment.LengthSquared();

		if (lengthSquared == 0)
		{
			return Vector2.Distance(point, start);
		}

		float t = Math.Clamp(Vector2.Dot(point - start, segment) / lengthSquared, 0, 1);

		return Vector2.Distance(point, start + segment * t);
	}
}

internal sealed class VisualEditableField
{
	private readonly VisualNode node;
	private readonly string property;
	private readonly VisualPort port;

	internal string Label => property ?? port.Label;
	internal VisualNode Node => node ?? port.Node;
	internal string Value
	{
		get => property != null ? node.Properties[property] : port.DefaultValue ?? "";
		set
		{
			if (property != null)
			{
				node.Properties[property] = value;
			}
			else
			{
				port.DefaultValue = value;
			}
		}
	}

	internal VisualEditableField(VisualNode node, string property)
	{
		this.node = node;
		this.property = property;
	}

	internal VisualEditableField(VisualPort port)
	{
		this.port = port;
	}

	internal bool Matches(VisualEditableField other)
	{
		return other != null
			&& Node == other.Node
			&& string.Equals(property, other.property, StringComparison.Ordinal)
			&& ReferenceEquals(port, other.port);
	}
}

internal sealed class VisualInlineEditor
{
	internal VisualEditableField Target { get; private set; }
	internal string Text { get; private set; } = "";
	internal bool IsActive => Target != null;

	internal bool IsEditing(VisualEditableField field)
	{
		return Target?.Matches(field) == true;
	}

	internal void Begin(VisualEditableField field)
	{
		Target = field;
		Text = field.Value;
	}

	internal void Append(string value)
	{
		if (IsActive)
		{
			Text += value;
		}
	}

	internal void Backspace()
	{
		if (IsActive && Text.Length > 0)
		{
			Text = Text.Substring(0, Text.Length - 1);
		}
	}

	internal void Commit()
	{
		if (IsActive)
		{
			Target.Value = Text;
			Cancel();
		}
	}

	internal void Cancel()
	{
		Target = null;
		Text = "";
	}
}

internal static class VisualNodeHitTester
{
	internal static VisualNode FindNode(VisualGraph graph, Vector2 world)
	{
		for (int index = graph.Nodes.Count - 1; index >= 0; index--)
		{
			VisualNode node = graph.Nodes[index];

			if (VisualNodeGeometry.BoundsOf(node).Contains(world))
			{
				return node;
			}
		}

		return null;
	}

	internal static VisualPort FindPort(VisualGraph graph, NodeCanvas canvas, Vector2 world)
	{
		float radius = VisualNodeGeometry.PortRadius + 5 / canvas.Zoom;

		for (int index = graph.Nodes.Count - 1; index >= 0; index--)
		{
			foreach (VisualPort port in graph.Nodes[index].Inputs.Concat(graph.Nodes[index].Outputs))
			{
				if (Vector2.Distance(world, VisualNodeGeometry.PortPosition(port)) <= radius)
				{
					return port;
				}
			}
		}

		return null;
	}

	internal static VisualConnection FindConnection(VisualGraph graph, NodeCanvas canvas, Vector2 world)
	{
		return graph.Connections.FirstOrDefault(connection =>
			VisualNodeGeometry.NearConnection(world, connection, 10 / canvas.Zoom)
		);
	}
}
