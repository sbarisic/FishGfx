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
using FishGfx.NodeGraph;

namespace FishGfx.NodeEditor {
	internal sealed class NodeEditorApplication {
		private const int InitialWidth = 1920;
		private const int InitialHeight = 1080;
		private RenderWindow window;
		private InputManager input;
		private FunctionNodeGraph graph = new FunctionNodeGraph();
		private readonly NodeFunctionRegistry registry = new NodeFunctionRegistry();
		private readonly FunctionNodeEvaluator evaluator = new FunctionNodeEvaluator();
		private readonly NodeCanvas canvas = new NodeCanvas();
		private readonly ContextMenu menu;
		private readonly InlineValueEditor editor = new InlineValueEditor();
		private readonly List<string> typedCharacters = new List<string>();
		private NodeRenderer renderer;
		private object selected;
		private FunctionNode draggedNode;
		private Vector2 nodeDragOffset;
		private NodePort draggedPort;
		private NodePort hoverPort;
		private Vector2 previousMouse;
		private float scrollDelta;
		private readonly bool autoMode;
		private NodeEvaluationResult evaluationResult;
		private readonly string layoutPath = Path.Combine(AppContext.BaseDirectory, "node-layout.json");
		private string fileStatus;
		private bool fileStatusError;

		internal NodeEditorApplication(string[] args) {
			autoMode = args.Any(a => string.Equals(a, "--auto", StringComparison.OrdinalIgnoreCase));
			registry.Register(typeof(SampleNodeFunctions));
			menu = new ContextMenu(registry.Functions);
		}

		internal void Run() {
			window = new RenderWindow(InitialWidth, InitialHeight, "FishGfx Visual Node Editor", true);
			input = new InputManager(window);
			BMFont menuFont = new BMFont(Path.Combine(AppContext.BaseDirectory, "data", "fonts", "opensans.fnt"), 18);
			foreach (Texture texture in menuFont.PageNames.Values) texture.SetFilter(TextureFilter.Linear);
			renderer = new NodeRenderer(new BMFont(Path.Combine(AppContext.BaseDirectory, "data", "fonts", "proggy.fnt"), 24), menuFont);
			window.OnScroll += OnScroll;
			window.OnChar += OnChar;
			window.OnWindowResize += OnResize;
			if (!autoMode && File.Exists(layoutPath)) {
				SeedGraph();
				if (!LoadLayout()) { graph = new FunctionNodeGraph(); SeedGraph(); }
			} else SeedGraph();
			ConfigureProjection();
			previousMouse = input.GetMousePos();
			if (autoMode) {
				Vector2 menuPoint = new Vector2(820, 820);
				menu.Open(menuPoint, canvas.ScreenToWorld(menuPoint), window.WindowWidth, window.WindowHeight);
			}
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
					renderer.Draw(graph, canvas, selected, hoverPort, draggedPort, canvas.ScreenToWorld(input.GetMousePos()), menu, editor, evaluationResult, fileStatus, fileStatusError, window.WindowWidth, window.WindowHeight);
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

			if (menu.IsOpen) {
				menu.UpdateHover(mouse);
				menu.Scroll(mouse, scrollDelta);
				foreach (string chars in typedCharacters) menu.Append(chars);
				if (input.GetKeyPressed(Key.Backspace)) menu.Backspace();
				if (input.GetKeyPressed(Key.Left)) menu.MoveCategory(-1);
				if (input.GetKeyPressed(Key.Right)) menu.MoveCategory(1);
				if (input.GetKeyPressed(Key.Up)) menu.MoveFunction(-1);
				if (input.GetKeyPressed(Key.Down)) menu.MoveFunction(1);
				if (input.GetKeyPressed(Key.Escape)) menu.Escape();
				if (input.GetKeyPressed(Key.Enter)) CreateFromMenu(menu.Activate());
				if (input.GetKeyPressed(Key.MouseLeft)) CreateFromMenu(menu.Click(mouse));
				if (input.GetKeyPressed(Key.MouseRight)) menu.Open(mouse, world, window.WindowWidth, window.WindowHeight);
				return;
			}

			if (scrollDelta != 0) canvas.ZoomAt(mouse, scrollDelta);
			if (input.GetKeyDown(Key.MouseMiddle)) canvas.PanBy(delta);
			hoverPort = FindPort(world);

			if (editor.IsActive) {
				foreach (string chars in typedCharacters) editor.Append(chars);
				if (input.GetKeyPressed(Key.Backspace)) editor.Backspace();
				if (input.GetKeyPressed(Key.Enter) && editor.Commit()) { graph.InvalidateEvaluation(); evaluationResult = null; }
				if (input.GetKeyPressed(Key.Escape)) editor.Cancel();
				return;
			}

			if (input.GetKeyPressed(Key.Escape)) {
				if (draggedPort != null) draggedPort = null;
				else window.ShouldClose = true;
			}

			if (input.GetKeyPressed(Key.Delete)) DeleteSelected();
			bool control = input.GetKeyDown(Key.LeftControl) || input.GetKeyDown(Key.RightControl);
			if (control && input.GetKeyPressed(Key.S)) SaveLayout();
			if (control && input.GetKeyPressed(Key.O)) LoadLayout();
			if (input.GetKeyPressed(Key.F5)) evaluationResult = evaluator.Evaluate(graph);
			if (input.GetKeyPressed(Key.MouseRight)) { menu.Open(mouse, world, window.WindowWidth, window.WindowHeight); selected = null; }

			if (input.GetKeyPressed(Key.MouseLeft)) HandleLeftPress(world);
			if (draggedNode != null && input.GetKeyDown(Key.MouseLeft)) draggedNode.Position = world - nodeDragOffset;
			if (input.GetKeyReleased(Key.MouseLeft)) HandleLeftRelease(world);
		}

		private void HandleLeftPress(Vector2 world) {
			Vector2 screen = input.GetMousePos();
			if (screen.X >= 220 && screen.X <= 352 && screen.Y >= window.WindowHeight - 58 && screen.Y <= window.WindowHeight - 20) {
				evaluationResult = evaluator.Evaluate(graph); return;
			}
			FunctionNode nodeAt = FindNode(world);
			if (nodeAt != null && NodeGeometry.CloseOf(nodeAt).Contains(world)) { graph.Remove(nodeAt); evaluationResult = null; if (selected == nodeAt) selected = null; return; }
			NodePort port = FindPort(world);
			if (port != null) {
				if (port.Direction == NodePortDirection.Input) {
					NodeConnection existing = graph.ConnectionAtInput(port);
					if (existing != null) { graph.Remove(existing); evaluationResult = null; draggedPort = existing.Output; selected = null; return; }
				}
				draggedPort = port; selected = null; return;
			}

			if (nodeAt != null) {
				for (int i = 0; i < nodeAt.BodyValues.Count; i++)
					if (NodeGeometry.ValueBounds(nodeAt, i).Contains(world)) { editor.Begin(nodeAt.BodyValues[i]); return; }
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
			if (target != null && target != draggedPort) { selected = graph.Connect(draggedPort, target); evaluationResult = null; }
			draggedPort = null;
		}

		private void CreateFromMenu(NodeFunctionDescriptor descriptor) {
			if (descriptor == null) return;
			selected = graph.CreateNode(descriptor, menu.InsertionWorld); evaluationResult = null; menu.Close();
		}

		private FunctionNode FindNode(Vector2 world) {
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
			if (selected is FunctionNode node) graph.Remove(node);
			else if (selected is NodeConnection connection) graph.Remove(connection);
			evaluationResult = null;
			selected = null;
		}

		private void SaveLayout() {
			try { NodeGraphJson.SaveFile(layoutPath, graph, canvas.Capture()); fileStatus = "Saved node-layout.json"; fileStatusError = false; }
			catch (Exception ex) { fileStatus = "Save failed: " + ex.Message; fileStatusError = true; }
		}

		private bool LoadLayout() {
			NodeGraphLoadResult load = NodeGraphJson.LoadFile(layoutPath, registry);
			if (!load.Success) { fileStatus = "Load failed: " + string.Join(" | ", load.Errors); fileStatusError = true; return false; }
			graph = load.Graph; canvas.Apply(load.View); selected = null; draggedNode = null; draggedPort = null; evaluationResult = null;
			fileStatus = "Loaded node-layout.json"; fileStatusError = false; return true;
		}

		private void SeedGraph() {
			NodeFunctionDescriptor scalar = registry.Functions.Single(f => f.Title == "Scalar");
			NodeFunctionDescriptor add = registry.Functions.Single(f => f.Title == "Add");
			NodeFunctionDescriptor vector = registry.Functions.Single(f => f.Title == "Vector");
			NodeFunctionDescriptor multiply = registry.Functions.Single(f => f.Title == "Multiply");
			NodeFunctionDescriptor split = registry.Functions.Single(f => f.Title == "Split Vector");
			NodeFunctionDescriptor display = registry.Functions.Single(f => f.Title == "Display");
			FunctionNode scalarA = graph.CreateNode(scalar, new Vector2(20, 710)); scalarA.BodyValues[0].Text = "2";
			FunctionNode scalarB = graph.CreateNode(scalar, new Vector2(20, 510)); scalarB.BodyValues[0].Text = "3";
			FunctionNode addNode = graph.CreateNode(add, new Vector2(350, 650));
			FunctionNode vectorNode = graph.CreateNode(vector, new Vector2(20, 260));
			FunctionNode multiplyNode = graph.CreateNode(multiply, new Vector2(690, 500));
			FunctionNode splitNode = graph.CreateNode(split, new Vector2(1010, 500));
			FunctionNode displayNode = graph.CreateNode(display, new Vector2(1320, 580));
			graph.Connect(scalarA.Outputs[0], addNode.Inputs[0]); graph.Connect(scalarB.Outputs[0], addNode.Inputs[1]);
			graph.Connect(vectorNode.Outputs[0], multiplyNode.Inputs[0]); graph.Connect(addNode.Outputs[0], multiplyNode.Inputs[1]);
			graph.Connect(multiplyNode.Outputs[0], splitNode.Inputs[0]); graph.Connect(splitNode.Outputs[0], displayNode.Inputs[0]);
			evaluationResult = evaluator.Evaluate(graph);
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
