using System.Globalization;
using System.Numerics;
using FishGfx.Cad;
using FishGfx.Game;
using FishGfx.Graphics;

namespace FishGfx.ManifoldCad;

internal sealed class CadNodeCanvas
{
	private const float NodeWidth = 190;
	private const float NodeHeight = 82;
	private Vector2 pan = new(24, 28);
	private float zoom = 0.72f;
	private RunnerNode draggedNode;
	private Vector2 dragOffset;
	private bool panning;
	private Vector2 panStart;
	private Vector2 panOrigin;

	internal RunnerNode SelectedNode { get; private set; }

	internal event Action<RunnerNode> SelectionChanged;

	internal void Update(
		RunnerGraph graph,
		CadRect bounds,
		InputManager input,
		Vector2 mouse,
		float scrollDelta
	)
	{
		if (!bounds.Contains(mouse))
		{
			return;
		}

		if (scrollDelta != 0)
		{
			Vector2 before = ScreenToWorld(bounds, mouse);
			zoom = Math.Clamp(zoom * (scrollDelta > 0 ? 1.1f : 0.9f), 0.35f, 1.5f);
			Vector2 after = ScreenToWorld(bounds, mouse);
			pan += (after - before) * zoom;
		}

		if (input.WasMouseButtonPressed(MouseButton.Middle))
		{
			panning = true;
			panStart = mouse;
			panOrigin = pan;
		}

		if (panning && input.IsMouseButtonDown(MouseButton.Middle))
		{
			pan = panOrigin + mouse - panStart;
		}

		if (input.WasMouseButtonReleased(MouseButton.Middle))
		{
			panning = false;
		}

		if (input.WasMouseButtonPressed(MouseButton.Left))
		{
			RunnerNode hit = HitTest(graph, bounds, mouse);
			Select(hit);

			if (hit != null)
			{
				draggedNode = hit;
				dragOffset = ScreenToWorld(bounds, mouse) - new Vector2((float)hit.X, (float)hit.Y);
			}
		}

		if (draggedNode != null && input.IsMouseButtonDown(MouseButton.Left))
		{
			Vector2 world = ScreenToWorld(bounds, mouse) - dragOffset;
			draggedNode.X = world.X;
			draggedNode.Y = world.Y;
		}

		if (input.WasMouseButtonReleased(MouseButton.Left))
		{
			draggedNode = null;
		}
	}

	internal void SelectBySource(Guid? nodeId, RunnerGraph graph)
	{
		Select(nodeId.HasValue ? graph.Nodes.FirstOrDefault(node => node.Id == nodeId) : null);
	}

	internal void Render(
		RenderPass pass,
		RunnerGraph graph,
		CadRect bounds,
		GraphicsFont font,
		RunnerEvaluationResult evaluation
	)
	{
		pass.FillRectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height, new Color(24, 28, 34));
		pass.DrawRectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height, 1, new Color(72, 78, 88));
		DrawGrid(pass, bounds);

		foreach (RunnerConnection connection in graph.Connections)
		{
			RunnerNode output = graph.Nodes.FirstOrDefault(node => node.Id == connection.OutputNodeId);
			RunnerNode input = graph.Nodes.FirstOrDefault(node => node.Id == connection.InputNodeId);

			if (output == null || input == null)
			{
				continue;
			}

			Vector2 start = WorldToScreen(bounds, new Vector2((float)output.X + NodeWidth, (float)output.Y + NodeHeight * 0.5f));
			Vector2 end = WorldToScreen(bounds, new Vector2((float)input.X, (float)input.Y + NodeHeight * 0.5f));
			Vector2 middleA = new((start.X + end.X) * 0.5f, start.Y);
			Vector2 middleB = new((start.X + end.X) * 0.5f, end.Y);
			Color wire = PortColor(graph, connection);
			pass.DrawLine(new Vertex2(start, wire), new Vertex2(middleA, wire), 2);
			pass.DrawLine(new Vertex2(middleA, wire), new Vertex2(middleB, wire), 2);
			pass.DrawLine(new Vertex2(middleB, wire), new Vertex2(end, wire), 2);
		}

		foreach (RunnerNode node in graph.Nodes)
		{
			DrawNode(pass, bounds, font, node, evaluation);
		}

		pass.DrawText(
			font,
			new Vector2(bounds.X + 12, bounds.Y + bounds.Height - 24),
			"RUNNER GRAPH  |  wheel zoom, middle pan, drag nodes",
			new Color(155, 166, 180),
			15
		);
	}

	internal string[] EditableProperties()
	{
		return SelectedNode?.DefinitionId switch
		{
			RunnerNodes.Straight => new[] { "length" },
			RunnerNodes.Bend => new[] { "radius", "angle", "rotation" },
			RunnerNodes.CircularPipe => new[] { "outerDiameter", "wallThickness" },
			_ => Array.Empty<string>(),
		};
	}

	private void Select(RunnerNode node)
	{
		if (ReferenceEquals(SelectedNode, node))
		{
			return;
		}

		SelectedNode = node;
		SelectionChanged?.Invoke(node);
	}

	private RunnerNode HitTest(RunnerGraph graph, CadRect bounds, Vector2 screen)
	{
		Vector2 world = ScreenToWorld(bounds, screen);

		return graph.Nodes.LastOrDefault(node =>
			world.X >= node.X
			&& world.X <= node.X + NodeWidth
			&& world.Y >= node.Y
			&& world.Y <= node.Y + NodeHeight
		);
	}

	private void DrawNode(
		RenderPass pass,
		CadRect bounds,
		GraphicsFont font,
		RunnerNode node,
		RunnerEvaluationResult evaluation
	)
	{
		Vector2 position = WorldToScreen(bounds, new Vector2((float)node.X, (float)node.Y));
		float width = NodeWidth * zoom;
		float height = NodeHeight * zoom;
		bool hasError = evaluation?.Diagnostics.Any(diagnostic =>
			diagnostic.NodeId == node.Id && diagnostic.Severity == CadDiagnosticSeverity.Error
		) == true;
		Color background = ReferenceEquals(node, SelectedNode)
			? new Color(55, 79, 105)
			: new Color(43, 48, 57);
		Color border = hasError ? new Color(190, 62, 62) : new Color(91, 103, 118);
		pass.FillRectangle(position.X, position.Y, width, height, background);
		pass.DrawRectangle(position.X, position.Y, width, height, 2, border);
		string title = RunnerNodes.TryGet(node.DefinitionId, out RunnerNodeDefinition definition)
			? definition.Title
			: "Missing: " + node.DefinitionId;
		pass.DrawText(
			font,
			position + new Vector2(10 * zoom, height - 23 * zoom),
			title,
			Color.White,
			Math.Max(10, 15 * zoom)
		);
		string detail = node.DefinitionId switch
		{
			RunnerNodes.MateReference => ShortMate(node),
			RunnerNodes.Straight => Property(node, "length") + " mm",
			RunnerNodes.Bend => $"R {Property(node, "radius")} | {Property(node, "angle")} deg | rot {Property(node, "rotation")}",
			RunnerNodes.CircularPipe => $"OD {Property(node, "outerDiameter")} | wall {Property(node, "wallThickness")}",
			RunnerNodes.RunnerLength => evaluation?.Path == null ? "-- mm" : evaluation.LengthMillimetres.ToString("F2", CultureInfo.InvariantCulture) + " mm",
			_ => string.Empty,
		};
		pass.DrawText(
			font,
			position + new Vector2(10 * zoom, 17 * zoom),
			detail,
			new Color(185, 193, 203),
			Math.Max(9, 12 * zoom)
		);
	}

	private void DrawGrid(RenderPass pass, CadRect bounds)
	{
		float spacing = 40 * zoom;
		float startX = bounds.X + Mod(pan.X, spacing);
		float startY = bounds.Y + Mod(pan.Y, spacing);
		Color color = new(34, 39, 47);

		for (float x = startX; x < bounds.X + bounds.Width; x += spacing)
		{
			pass.DrawLine(
				new Vertex2(new Vector2(x, bounds.Y), color),
				new Vertex2(new Vector2(x, bounds.Y + bounds.Height), color)
			);
		}

		for (float y = startY; y < bounds.Y + bounds.Height; y += spacing)
		{
			pass.DrawLine(
				new Vertex2(new Vector2(bounds.X, y), color),
				new Vertex2(new Vector2(bounds.X + bounds.Width, y), color)
			);
		}
	}

	private Vector2 WorldToScreen(CadRect bounds, Vector2 world)
	{
		return bounds.Minimum + pan + world * zoom;
	}

	private Vector2 ScreenToWorld(CadRect bounds, Vector2 screen)
	{
		return (screen - bounds.Minimum - pan) / zoom;
	}

	private static Color PortColor(RunnerGraph graph, RunnerConnection connection)
	{
		RunnerNode output = graph.Nodes.First(node => node.Id == connection.OutputNodeId);

		if (!RunnerNodes.TryGet(output.DefinitionId, out RunnerNodeDefinition definition))
		{
			return new Color(120, 120, 120);
		}

		return definition.FindPort(connection.OutputPort, RunnerPortDirection.Output)?.Type switch
		{
			RunnerPortType.MateFrame => new Color(226, 174, 71),
			RunnerPortType.RunnerPath => new Color(72, 176, 215),
			RunnerPortType.PipeProfile => new Color(111, 203, 132),
			RunnerPortType.CadSolid => new Color(190, 111, 210),
			_ => new Color(150, 155, 165),
		};
	}

	private static string Property(RunnerNode node, string name)
	{
		return node.Properties.TryGetValue(name, out string value) ? value : "?";
	}

	private static string ShortMate(RunnerNode node)
	{
		return node.Properties.TryGetValue("mateId", out string text)
			&& Guid.TryParse(text, out Guid id)
			? id.ToString("N")[..8]
			: "unassigned";
	}

	private static float Mod(float value, float divisor)
	{
		return (value % divisor + divisor) % divisor;
	}
}
