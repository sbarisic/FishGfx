using FishGfx;
using FishGfx.Formats;
using FishGfx.Game;
using FishGfx.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Diagnostics;
using System.IO;

namespace FishGfx.NodeEditor {
	internal sealed class NodeEditorApplication {
		private const int InitialWidth = 1920;
		private const int InitialHeight = 1080;
		private RenderWindow window;
		private InputManager input;
		private readonly NodeGraph graph = new NodeGraph();
		private readonly NodeCanvas canvas = new NodeCanvas();
		private readonly ContextMenu menu = new ContextMenu();
		private readonly InlineValueEditor editor = new InlineValueEditor();
		private readonly List<string> typedCharacters = new List<string>();
		private NodeRenderer renderer;
		private object selected;
		private Node draggedNode;
		private Vector2 nodeDragOffset;
		private NodePort draggedPort;
		private NodePort hoverPort;
		private Vector2 previousMouse;
		private float scrollDelta;
		private readonly bool autoMode;

		internal NodeEditorApplication(string[] args) {
			autoMode = args.Any(a => string.Equals(a, "--auto", StringComparison.OrdinalIgnoreCase));
		}

		internal void Run() {
			window = new RenderWindow(InitialWidth, InitialHeight, "FishGfx Visual Node Editor", true);
			input = new InputManager(window);
			renderer = new NodeRenderer(new BMFont(Path.Combine(AppContext.BaseDirectory, "data", "fonts", "proggy.fnt"), 24));
			window.OnScroll += OnScroll;
			window.OnChar += OnChar;
			window.OnWindowResize += OnResize;
			SeedGraph();
			ConfigureProjection();
			previousMouse = input.GetMousePos();
			Stopwatch runtime = Stopwatch.StartNew();

			while (!window.ShouldClose) {
				input.BeginNewFrame();
				typedCharacters.Clear();
				scrollDelta = 0;
				Events.Poll();
				Update();
				RenderState editorState = Gfx.PeekRenderState();
				editorState.EnableDepthTest = false;
				editorState.EnableDepthMask = false;
				editorState.EnableCullFace = false;
				Gfx.PushRenderState(editorState);
				try {
					renderer.Draw(graph, canvas, selected, hoverPort, draggedPort, canvas.ScreenToWorld(input.GetMousePos()), menu, editor, window.WindowWidth, window.WindowHeight);
				} finally {
					Gfx.PopRenderState();
				}
				window.SwapBuffers();
				if (autoMode && runtime.Elapsed.TotalSeconds >= 2) window.ShouldClose = true;
			}

			window.OnScroll -= OnScroll;
			window.OnChar -= OnChar;
			window.OnWindowResize -= OnResize;
			renderer.Dispose();
			RenderAPI.CollectGarbage();
			window.Close();
		}

		private void Update() {
			Vector2 mouse = input.GetMousePos();
			Vector2 world = canvas.ScreenToWorld(mouse);
			Vector2 delta = mouse - previousMouse;
			previousMouse = mouse;

			if (scrollDelta != 0 && !menu.IsOpen) canvas.ZoomAt(mouse, scrollDelta);
			if (input.GetKeyDown(Key.MouseMiddle)) canvas.PanBy(delta);
			hoverPort = FindPort(world);
			menu.HoverIndex = menu.Hit(world);

			if (editor.IsActive) {
				foreach (string chars in typedCharacters) editor.Append(chars);
				if (input.GetKeyPressed(Key.Backspace)) editor.Backspace();
				if (input.GetKeyPressed(Key.Enter)) editor.Commit();
				if (input.GetKeyPressed(Key.Escape)) editor.Cancel();
				return;
			}

			if (input.GetKeyPressed(Key.Escape)) {
				if (draggedPort != null) draggedPort = null;
				else if (menu.IsOpen) menu.Close();
				else window.ShouldClose = true;
			}

			if (input.GetKeyPressed(Key.Delete)) DeleteSelected();
			if (input.GetKeyPressed(Key.MouseRight)) { menu.Open(world); selected = null; }

			if (input.GetKeyPressed(Key.MouseLeft)) HandleLeftPress(world);
			if (draggedNode != null && input.GetKeyDown(Key.MouseLeft)) draggedNode.Position = world - nodeDragOffset;
			if (input.GetKeyReleased(Key.MouseLeft)) HandleLeftRelease(world);
		}

		private void HandleLeftPress(Vector2 world) {
			if (menu.IsOpen) {
				int index = menu.Hit(world);
				if (index >= 0) { Node node = NodeTemplates.Create(NodeTemplates.Names[index], world); graph.Nodes.Add(node); selected = node; }
				menu.Close();
				return;
			}

			Node nodeAt = FindNode(world);
			if (nodeAt != null && NodeGeometry.CloseOf(nodeAt).Contains(world)) { graph.Remove(nodeAt); if (selected == nodeAt) selected = null; return; }
			NodePort port = FindPort(world);
			if (port != null) {
				if (port.Direction == PortDirection.Input) {
					NodeConnection existing = graph.ConnectionAtInput(port);
					if (existing != null) { graph.Remove(existing); draggedPort = existing.Output; selected = null; return; }
				}
				draggedPort = port; selected = null; return;
			}

			if (nodeAt != null) {
				for (int i = 0; i < nodeAt.Values.Count; i++)
					if (NodeGeometry.ValueBounds(nodeAt, i).Contains(world)) { editor.Begin(nodeAt.Values[i]); return; }
				selected = nodeAt;
				if (NodeGeometry.HeaderOf(nodeAt).Contains(world)) { draggedNode = nodeAt; nodeDragOffset = world - nodeAt.Position; }
				return;
			}

			NodeConnection connection = FindConnection(world);
			selected = connection;
		}

		private void HandleLeftRelease(Vector2 world) {
			draggedNode = null;
			if (draggedPort == null) return;
			NodePort target = FindPort(world);
			if (target != null && target != draggedPort) selected = graph.Connect(draggedPort, target);
			draggedPort = null;
		}

		private Node FindNode(Vector2 world) {
			for (int i = graph.Nodes.Count - 1; i >= 0; i--) if (NodeGeometry.BoundsOf(graph.Nodes[i]).Contains(world)) return graph.Nodes[i];
			return null;
		}

		private NodePort FindPort(Vector2 world) {
			float radius = NodeGeometry.PortRadius + 5 / canvas.Zoom;
			for (int n = graph.Nodes.Count - 1; n >= 0; n--)
				foreach (NodePort port in graph.Nodes[n].Inputs.Concat(graph.Nodes[n].Outputs))
					if (Vector2.Distance(world, NodeGeometry.PortPosition(port)) <= radius) return port;
			return null;
		}

		private NodeConnection FindConnection(Vector2 world) {
			foreach (NodeConnection connection in graph.Connections)
				if (NodeGeometry.NearConnection(world, NodeGeometry.PortPosition(connection.Output), NodeGeometry.PortPosition(connection.Input), 10 / canvas.Zoom)) return connection;
			return null;
		}

		private void DeleteSelected() {
			if (selected is Node node) graph.Remove(node);
			else if (selected is NodeConnection connection) graph.Remove(connection);
			selected = null;
		}

		private void SeedGraph() {
			Node scalarA = NodeTemplates.Create("Scalar Source", new Vector2(20, 710)); scalarA.Values[0].Value = 1;
			Node scalarB = NodeTemplates.Create("Scalar Source", new Vector2(20, 500)); scalarB.Values[0].Value = 2;
			Node scalarProcess = NodeTemplates.Create("Scalar Process", new Vector2(360, 620));
			Node vectorA = NodeTemplates.Create("Vector Source", new Vector2(20, 270));
			Node vectorProcess = NodeTemplates.Create("Vector Process", new Vector2(390, 260));
			Node output = NodeTemplates.Create("Vector Output", new Vector2(760, 440));
			graph.Nodes.AddRange(new[] { scalarA, scalarB, scalarProcess, vectorA, vectorProcess, output });
			graph.Connect(scalarA.Outputs[0], scalarProcess.Inputs[0]);
			graph.Connect(scalarB.Outputs[0], scalarProcess.Inputs[1]);
			graph.Connect(vectorA.Outputs[0], vectorProcess.Inputs[0]);
			graph.Connect(vectorProcess.Outputs[0], output.Inputs[0]);
		}

		private void ConfigureProjection() {
			ShaderUniforms.Current.Camera.SetOrthogonal(0, 0, window.WindowWidth, window.WindowHeight);
			ShaderUniforms.Current.Resolution = window.WindowSize;
		}
		private void OnResize(RenderWindow wnd, int width, int height) => ConfigureProjection();
		private void OnScroll(RenderWindow wnd, float x, float y) => scrollDelta += y;
		private void OnChar(RenderWindow wnd, string chars, uint unicode) => typedCharacters.Add(chars);
	}
}
