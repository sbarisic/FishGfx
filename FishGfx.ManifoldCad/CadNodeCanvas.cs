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
	private const float PortRadius = 8;
	private static readonly string[] PaletteDefinitions =
	{
		RunnerNodes.StartRunner,
		RunnerNodes.Straight,
		RunnerNodes.Bend,
		RunnerNodes.CircularPipe,
		RunnerNodes.LoftTransition,
		RunnerNodes.RunnerLength,
		RunnerNodes.RunnerOutput,
	};

	private Vector2 pan = new(24, 28);
	private float zoom = 0.72f;
	private RunnerNode draggedNode;
	private Vector2 dragOffset;
	private bool panning;
	private Vector2 panStart;
	private Vector2 panOrigin;
	private PortHit draggedPort;
	private RunnerConnection selectedConnection;
	private bool paletteOpen;
	private Vector2 paletteScreen;
	private Vector2 paletteWorld;
	private Guid? paletteConnectionId;
	private Vector2 currentMouse;

	internal RunnerNode SelectedNode { get; private set; }
	internal event Action<RunnerNode> SelectionChanged;
	internal event Action GraphChanged;
	internal event Action<string, bool> StatusChanged;

	internal void CaptureView(ManifoldViewState view)
	{
		ArgumentNullException.ThrowIfNull(view);
		view.GraphPanX = pan.X;
		view.GraphPanY = pan.Y;
		view.GraphZoom = zoom;
	}

	internal void RestoreView(ManifoldViewState view)
	{
		ArgumentNullException.ThrowIfNull(view);
		pan = new Vector2((float)view.GraphPanX, (float)view.GraphPanY);
		zoom = Math.Clamp((float)view.GraphZoom, 0.35f, 1.5f);
		paletteOpen = false;
		panning = false;
		draggedNode = null;
		draggedPort = null;
		ClearSelection();
	}

	internal void Update(RunnerGraph graph, CadRect bounds, InputManager input, Vector2 mouse, float scrollDelta)
	{
		currentMouse = mouse;
		if (!bounds.Contains(mouse)) return;

		if (paletteOpen)
		{
			if (input.WasKeyPressed(Key.Escape) || input.WasMouseButtonPressed(MouseButton.Right))
			{
				paletteOpen = false;
				return;
			}
			if (input.WasMouseButtonPressed(MouseButton.Left))
			{
				int index = PaletteIndex(mouse);
				if (index >= 0) CreateFromPalette(graph, PaletteDefinitions[index]);
				else paletteOpen = false;
			}
			return;
		}

		if (scrollDelta != 0)
		{
			Vector2 before = ScreenToWorld(bounds, mouse);
			zoom = Math.Clamp(zoom * (scrollDelta > 0 ? 1.1f : 0.9f), 0.35f, 1.5f);
			Vector2 after = ScreenToWorld(bounds, mouse);
			pan += (after - before) * zoom;
		}

		if (input.WasMouseButtonPressed(MouseButton.Right))
		{
			OpenPalette(graph, bounds, mouse);
			return;
		}

		if (input.WasKeyPressed(Key.Delete))
		{
			if (selectedConnection != null)
			{
				graph.RemoveConnection(selectedConnection.Id);
				selectedConnection = null;
				GraphChanged?.Invoke();
			}
			else if (SelectedNode != null)
			{
				graph.RemoveNode(SelectedNode.Id);
				Select(null);
				GraphChanged?.Invoke();
			}
		}

		if (input.WasMouseButtonPressed(MouseButton.Middle))
		{
			panning = true;
			panStart = mouse;
			panOrigin = pan;
		}
		if (panning && input.IsMouseButtonDown(MouseButton.Middle)) pan = panOrigin + mouse - panStart;
		if (input.WasMouseButtonReleased(MouseButton.Middle)) panning = false;

		if (input.WasMouseButtonPressed(MouseButton.Left))
		{
			PortHit port = HitPort(graph, bounds, mouse);
			if (port != null)
			{
				draggedPort = port;
				Select(port.Node);
				selectedConnection = null;
				return;
			}

			RunnerConnection connection = HitConnection(graph, bounds, mouse);
			if (connection != null)
			{
				selectedConnection = connection;
				Select(null);
				return;
			}

			RunnerNode hit = HitTest(graph, bounds, mouse);
			Select(hit);
			selectedConnection = null;
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
			if (draggedPort != null)
			{
				CompleteConnection(graph, HitPort(graph, bounds, mouse));
				draggedPort = null;
			}
			draggedNode = null;
		}
	}

	internal void OpenPalette(RunnerGraph graph, CadRect bounds)
	{
		Vector2 screen = bounds.Minimum + new Vector2(bounds.Width * 0.5f, bounds.Height * 0.35f);
		OpenPalette(graph, bounds, screen);
	}

	internal void SelectBySource(Guid? nodeId, RunnerGraph graph)
	{
		Select(nodeId.HasValue ? graph.Nodes.FirstOrDefault(node => node.Id == nodeId) : null);
	}

	internal void ClearSelection()
	{
		selectedConnection = null;
		Select(null);
	}

	internal void Render(RenderPass pass, RunnerGraph graph, CadRect bounds, GraphicsFont font, RunnerEvaluationResult evaluation)
	{
		pass.FillRectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height, new Color(24, 28, 34));
		pass.DrawRectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height, 1, new Color(72, 78, 88));
		DrawGrid(pass, bounds);

		foreach (RunnerConnection connection in graph.Connections) DrawConnection(pass, graph, bounds, connection);
		if (draggedPort != null)
		{
			Vector2 start = WorldToScreen(bounds, PortPosition(draggedPort.Node, draggedPort.Port));
			pass.DrawLine(new Vertex2(start, PortColor(draggedPort.Port.Type)),
				new Vertex2(currentMouse, PortColor(draggedPort.Port.Type)), 2);
		}
		foreach (RunnerNode node in graph.Nodes) DrawNode(pass, bounds, font, node, evaluation);

		pass.DrawText(font, new Vector2(bounds.X + 12, bounds.Y + bounds.Height - 24),
			"RUNNER GRAPH  |  right click/Add Node, drag ports, Delete, wheel zoom, middle pan",
			new Color(155, 166, 180), 15);
		if (paletteOpen) DrawPalette(pass, font);
	}

	internal string[] EditableProperties()
	{
		return SelectedNode?.DefinitionId switch
		{
			RunnerNodes.StartRunner => new[] { "wallThickness" },
			RunnerNodes.Straight => new[] { "length" },
			RunnerNodes.Bend => new[] { "radius", "angle", "rotation" },
			RunnerNodes.CircularPipe => new[] { "outerDiameter", "wallThickness" },
			RunnerNodes.LoftTransition => new[] { "length", "rotation" },
			_ => Array.Empty<string>(),
		};
	}

	private void OpenPalette(RunnerGraph graph, CadRect bounds, Vector2 screen)
	{
		paletteOpen = true;
		paletteScreen = new Vector2(Math.Min(screen.X, bounds.X + bounds.Width - 220),
			Math.Min(screen.Y, bounds.Y + bounds.Height - PaletteDefinitions.Length * 28 - 12));
		paletteWorld = ScreenToWorld(bounds, screen);
		paletteConnectionId = HitConnection(graph, bounds, screen)?.Id;
	}

	private void CreateFromPalette(RunnerGraph graph, string definitionId)
	{
		paletteOpen = false;
		if (definitionId == RunnerNodes.RunnerOutput
			&& graph.Nodes.Any(node => node.DefinitionId == RunnerNodes.RunnerOutput))
		{
			StatusChanged?.Invoke("This runner graph already has its terminal Runner Output.", true);
			return;
		}

		RunnerNode node;
		if (paletteConnectionId.HasValue && definitionId is RunnerNodes.Straight
			or RunnerNodes.Bend or RunnerNodes.LoftTransition)
		{
			if (!graph.TrySpliceConnection(paletteConnectionId.Value, definitionId, paletteWorld.X, paletteWorld.Y,
				out node, out string error))
			{
				StatusChanged?.Invoke(error, true);
				return;
			}
		}
		else
		{
			node = graph.AddNode(definitionId, paletteWorld.X, paletteWorld.Y);
		}
		Select(node);
		selectedConnection = null;
		GraphChanged?.Invoke();
	}

	private int PaletteIndex(Vector2 mouse)
	{
		if (mouse.X < paletteScreen.X || mouse.X > paletteScreen.X + 210
			|| mouse.Y < paletteScreen.Y || mouse.Y > paletteScreen.Y + PaletteDefinitions.Length * 28)
		{
			return -1;
		}
		return Math.Clamp((int)((mouse.Y - paletteScreen.Y) / 28), 0, PaletteDefinitions.Length - 1);
	}

	private void DrawPalette(RenderPass pass, GraphicsFont font)
	{
		pass.FillRectangle(paletteScreen.X, paletteScreen.Y, 210, PaletteDefinitions.Length * 28,
			new Color(38, 43, 51));
		pass.DrawRectangle(paletteScreen.X, paletteScreen.Y, 210, PaletteDefinitions.Length * 28, 1,
			new Color(110, 122, 138));
		int hover = PaletteIndex(currentMouse);
		for (int index = 0; index < PaletteDefinitions.Length; index++)
		{
			if (index == hover) pass.FillRectangle(paletteScreen.X + 1, paletteScreen.Y + index * 28 + 1,
				208, 26, new Color(58, 76, 96));
			RunnerNodes.TryGet(PaletteDefinitions[index], out RunnerNodeDefinition definition);
			pass.DrawText(font, paletteScreen + new Vector2(10, index * 28 + 7), definition.Title, Color.White, 13);
		}
	}

	private void CompleteConnection(RunnerGraph graph, PortHit target)
	{
		if (target == null || target.Node.Id == draggedPort.Node.Id
			|| target.Port.Direction == draggedPort.Port.Direction)
		{
			return;
		}
		PortHit output = draggedPort.Port.Direction == RunnerPortDirection.Output ? draggedPort : target;
		PortHit input = draggedPort.Port.Direction == RunnerPortDirection.Input ? draggedPort : target;
		if (!graph.TryConnect(output.Node.Id, output.Port.Name, input.Node.Id, input.Port.Name,
			out _, out string error))
		{
			StatusChanged?.Invoke(error, true);
			return;
		}
		GraphChanged?.Invoke();
	}

	private void DrawConnection(RenderPass pass, RunnerGraph graph, CadRect bounds, RunnerConnection connection)
	{
		RunnerNode output = graph.Nodes.FirstOrDefault(node => node.Id == connection.OutputNodeId);
		RunnerNode input = graph.Nodes.FirstOrDefault(node => node.Id == connection.InputNodeId);
		if (output == null || input == null || !RunnerNodes.TryGet(output.DefinitionId, out RunnerNodeDefinition outputDefinition)
			|| !RunnerNodes.TryGet(input.DefinitionId, out RunnerNodeDefinition inputDefinition)) return;
		RunnerPortDefinition outputPort = outputDefinition.FindPort(connection.OutputPort, RunnerPortDirection.Output);
		RunnerPortDefinition inputPort = inputDefinition.FindPort(connection.InputPort, RunnerPortDirection.Input);
		if (outputPort == null || inputPort == null) return;
		Vector2 start = WorldToScreen(bounds, PortPosition(output, outputPort));
		Vector2 end = WorldToScreen(bounds, PortPosition(input, inputPort));
		Vector2 middleA = new((start.X + end.X) * 0.5f, start.Y);
		Vector2 middleB = new((start.X + end.X) * 0.5f, end.Y);
		Color wire = ReferenceEquals(connection, selectedConnection) ? new Color(255, 220, 80) : PortColor(outputPort.Type);
		pass.DrawLine(new Vertex2(start, wire), new Vertex2(middleA, wire), 2);
		pass.DrawLine(new Vertex2(middleA, wire), new Vertex2(middleB, wire), 2);
		pass.DrawLine(new Vertex2(middleB, wire), new Vertex2(end, wire), 2);
	}

	private RunnerConnection HitConnection(RunnerGraph graph, CadRect bounds, Vector2 screen)
	{
		foreach (RunnerConnection connection in graph.Connections.Reverse())
		{
			RunnerNode output = graph.Nodes.FirstOrDefault(node => node.Id == connection.OutputNodeId);
			RunnerNode input = graph.Nodes.FirstOrDefault(node => node.Id == connection.InputNodeId);
			if (output == null || input == null || !RunnerNodes.TryGet(output.DefinitionId, out RunnerNodeDefinition od)
				|| !RunnerNodes.TryGet(input.DefinitionId, out RunnerNodeDefinition id)) continue;
			RunnerPortDefinition op = od.FindPort(connection.OutputPort, RunnerPortDirection.Output);
			RunnerPortDefinition ip = id.FindPort(connection.InputPort, RunnerPortDirection.Input);
			if (op == null || ip == null) continue;
			Vector2 a = WorldToScreen(bounds, PortPosition(output, op));
			Vector2 d = WorldToScreen(bounds, PortPosition(input, ip));
			Vector2 b = new((a.X + d.X) * 0.5f, a.Y);
			Vector2 c = new(b.X, d.Y);
			if (DistanceToSegment(screen, a, b) <= 6 || DistanceToSegment(screen, b, c) <= 6
				|| DistanceToSegment(screen, c, d) <= 6) return connection;
		}
		return null;
	}

	private PortHit HitPort(RunnerGraph graph, CadRect bounds, Vector2 screen)
	{
		foreach (RunnerNode node in graph.Nodes.Reverse())
		{
			if (!RunnerNodes.TryGet(node.DefinitionId, out RunnerNodeDefinition definition)) continue;
			foreach (RunnerPortDefinition port in definition.Ports)
			{
				if (Vector2.Distance(WorldToScreen(bounds, PortPosition(node, port)), screen) <= PortRadius + 4)
					return new PortHit(node, port);
			}
		}
		return null;
	}

	private static Vector2 PortPosition(RunnerNode node, RunnerPortDefinition port)
	{
		RunnerNodeDefinition definition = RunnerNodes.Definitions[node.DefinitionId];
		RunnerPortDefinition[] side = definition.Ports.Where(item => item.Direction == port.Direction).ToArray();
		int index = Array.IndexOf(side, port);
		float y = (float)node.Y + NodeHeight * (index + 1) / (side.Length + 1);
		float x = (float)node.X + (port.Direction == RunnerPortDirection.Output ? NodeWidth : 0);
		return new Vector2(x, y);
	}

	private void Select(RunnerNode node)
	{
		if (ReferenceEquals(SelectedNode, node)) return;
		SelectedNode = node;
		SelectionChanged?.Invoke(node);
	}

	private RunnerNode HitTest(RunnerGraph graph, CadRect bounds, Vector2 screen)
	{
		Vector2 world = ScreenToWorld(bounds, screen);
		return graph.Nodes.LastOrDefault(node => world.X >= node.X && world.X <= node.X + NodeWidth
			&& world.Y >= node.Y && world.Y <= node.Y + NodeHeight);
	}

	private void DrawNode(RenderPass pass, CadRect bounds, GraphicsFont font, RunnerNode node, RunnerEvaluationResult evaluation)
	{
		Vector2 position = WorldToScreen(bounds, new Vector2((float)node.X, (float)node.Y));
		float width = NodeWidth * zoom;
		float height = NodeHeight * zoom;
		bool hasError = evaluation?.Diagnostics.Any(diagnostic => diagnostic.NodeId == node.Id
			&& diagnostic.Severity == CadDiagnosticSeverity.Error) == true;
		Color background = ReferenceEquals(node, SelectedNode) ? new Color(55, 79, 105) : new Color(43, 48, 57);
		Color border = hasError ? new Color(190, 62, 62) : new Color(91, 103, 118);
		pass.FillRectangle(position.X, position.Y, width, height, background);
		pass.DrawRectangle(position.X, position.Y, width, height, 2, border);
		string title = RunnerNodes.TryGet(node.DefinitionId, out RunnerNodeDefinition definition)
			? definition.Title : "Missing: " + node.DefinitionId;
		pass.DrawText(font, position + new Vector2(10 * zoom, height - 23 * zoom), title, Color.White,
			Math.Max(10, 15 * zoom));
		string detail = node.DefinitionId switch
		{
			RunnerNodes.StartRunner => "wall " + Property(node, "wallThickness") + " mm",
			RunnerNodes.Straight => Property(node, "length") + " mm",
			RunnerNodes.Bend => $"R {Property(node, "radius")} | {Property(node, "angle")} deg | rot {Property(node, "rotation")}",
			RunnerNodes.CircularPipe => $"OD {Property(node, "outerDiameter")} | wall {Property(node, "wallThickness")}",
			RunnerNodes.LoftTransition => $"{Property(node, "length")} mm | rot {Property(node, "rotation")}",
			RunnerNodes.RunnerLength or RunnerNodes.RunnerOutput => evaluation?.Chain == null ? "-- mm"
				: evaluation.LengthMillimetres.ToString("F2", CultureInfo.InvariantCulture) + " mm",
			_ => string.Empty,
		};
		pass.DrawText(font, position + new Vector2(10 * zoom, 17 * zoom), detail, new Color(185, 193, 203),
			Math.Max(9, 12 * zoom));
		if (definition != null)
		{
			foreach (RunnerPortDefinition port in definition.Ports)
			{
				Vector2 portPosition = WorldToScreen(bounds, PortPosition(node, port));
				if (draggedPort != null && node.Id != draggedPort.Node.Id
					&& port.Direction != draggedPort.Port.Direction && port.Type == draggedPort.Port.Type)
				{
					pass.FillCircle(portPosition, (PortRadius + 3) * zoom, Color.White, 16);
				}
				pass.FillCircle(portPosition, PortRadius * zoom, PortColor(port.Type), 16);
			}
		}
	}

	private void DrawGrid(RenderPass pass, CadRect bounds)
	{
		float spacing = 40 * zoom;
		float startX = bounds.X + Mod(pan.X, spacing);
		float startY = bounds.Y + Mod(pan.Y, spacing);
		Color color = new(34, 39, 47);
		for (float x = startX; x < bounds.X + bounds.Width; x += spacing)
			pass.DrawLine(new Vertex2(new Vector2(x, bounds.Y), color),
				new Vertex2(new Vector2(x, bounds.Y + bounds.Height), color));
		for (float y = startY; y < bounds.Y + bounds.Height; y += spacing)
			pass.DrawLine(new Vertex2(new Vector2(bounds.X, y), color),
				new Vertex2(new Vector2(bounds.X + bounds.Width, y), color));
	}

	private Vector2 WorldToScreen(CadRect bounds, Vector2 world) => bounds.Minimum + pan + world * zoom;
	private Vector2 ScreenToWorld(CadRect bounds, Vector2 screen) => (screen - bounds.Minimum - pan) / zoom;

	private static Color PortColor(RunnerPortType type) => type switch
	{
		RunnerPortType.RunnerFeatures => new Color(72, 176, 215),
		RunnerPortType.PipeProfile => new Color(111, 203, 132),
		RunnerPortType.Number => new Color(205, 174, 86),
		_ => new Color(150, 155, 165),
	};

	private static string Property(RunnerNode node, string name) =>
		node.Properties.TryGetValue(name, out string value) ? value : "?";

	private static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
	{
		Vector2 delta = end - start;
		float lengthSquared = delta.LengthSquared();
		if (lengthSquared <= 0.0001f) return Vector2.Distance(point, start);
		float amount = Math.Clamp(Vector2.Dot(point - start, delta) / lengthSquared, 0, 1);
		return Vector2.Distance(point, start + delta * amount);
	}

	private static float Mod(float value, float divisor) => (value % divisor + divisor) % divisor;

	private sealed record PortHit(RunnerNode Node, RunnerPortDefinition Port);
}
