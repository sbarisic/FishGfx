using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using FishGfx;
using FishGfx.Formats;
using FishGfx.Game;
using FishGfx.Graphics;
using FishGfx.NodeGraph;

namespace FishGfx.NodeEditor;

internal sealed class VisualNodeEditorApplication
{
	private const int InitialWidth = 1920;
	private const int InitialHeight = 1080;

	private readonly Camera renderCamera = new Camera();
	private readonly VisualEditorSession session = new VisualEditorSession();
	private readonly NodeCanvas canvas = new NodeCanvas();
	private readonly VisualContextMenu menu;
	private readonly VisualInlineEditor editor = new VisualInlineEditor();
	private readonly HashSet<Guid> selectedNodes = new HashSet<Guid>();
	private readonly List<string> typedCharacters = new List<string>();
	private readonly bool autoMode;
	private readonly string initialPath;
	private string filePath;
	private RenderWindow window;
	private InputManager input;
	private VisualNodeRenderer renderer;
	private VisualConnection selectedConnection;
	private VisualNode draggedNode;
	private VisualPort draggedPort;
	private VisualPort hoverPort;
	private Vector2 dragStartWorld;
	private Dictionary<Guid, Vector2> dragStartPositions;
	private string dragMutation;
	private string editMutation;
	private Vector2 previousMouse;
	private float scrollDelta;
	private string fileStatus;
	private bool fileStatusError;
	private bool showSource;

	internal VisualNodeEditorApplication(string[] args)
	{
		autoMode = args.Any(argument => string.Equals(argument, "--auto", StringComparison.OrdinalIgnoreCase));
		initialPath = args.FirstOrDefault(argument => !argument.StartsWith("--", StringComparison.Ordinal));
		filePath = initialPath ?? Path.Combine(AppContext.BaseDirectory, "visual-program.fishcode.json");
		menu = new VisualContextMenu(session.Catalog.Definitions);
	}

	internal void Run()
	{
		window = new RenderWindow(InitialWidth, InitialHeight, "FishGfx Visual C# Editor", true);
		input = new InputManager(window);
		TrueTypeFont graphFont = new TrueTypeFont(
			Path.Combine(AppContext.BaseDirectory, "data", "fonts", "Consolas-Regular.ttf")
		);
		TrueTypeFont interfaceFont = new TrueTypeFont(
			Path.Combine(AppContext.BaseDirectory, "data", "fonts", "Consolas-Regular.ttf")
		);

		renderer = new VisualNodeRenderer(graphFont, interfaceFont);
		window.Scrolled += OnScroll;
		window.TextInput += OnTextInput;
		window.Resized += OnResize;

		if (initialPath != null && File.Exists(initialPath))
		{
			LoadProgram();
		}

		canvas.Apply(session.CurrentFunction.View);
		ConfigureProjection();
		previousMouse = input.MousePosition;

		if (autoMode)
		{
			Vector2 menuPoint = new Vector2(820, 820);

			menu.Open(menuPoint, canvas.ScreenToWorld(menuPoint), window.Width, window.Height);
		}

		Stopwatch runtime = Stopwatch.StartNew();

		while (!window.IsCloseRequested)
		{
			input.BeginFrame();
			typedCharacters.Clear();
			scrollDelta = 0;
			window.PollEvents();
			Update();
			Render();

			if (autoMode && runtime.Elapsed.TotalSeconds >= 2)
			{
				window.IsCloseRequested = true;
			}
		}

		window.Scrolled -= OnScroll;
		window.TextInput -= OnTextInput;
		window.Resized -= OnResize;
		session.Dispose();
		renderer.Dispose();
		input.Dispose();
		window.Graphics.CollectGarbage();
		window.Dispose();
	}

	private void Render()
	{
		RenderState editorState = RenderState.Default with
		{
			CullMode = CullMode.None,
			DepthTestEnabled = false,
			DepthWriteEnabled = false,
		};

		using RenderFrame frame = window.Graphics.BeginFrame();
		using (RenderPass pass = frame.BeginPass(
			window.Graphics.Backbuffer,
			new RenderPassDescriptor
			{
				View = new RenderView(renderCamera),
				State = editorState,
				ColorLoadAction = RenderLoadAction.Clear,
				DepthLoadAction = RenderLoadAction.Clear,
				StencilLoadAction = RenderLoadAction.Clear,
				ClearColor = VisualNodeRenderer.CanvasColor,
			}
		))
		{
			renderer.Draw(
				pass,
				session,
				canvas,
				selectedNodes,
				selectedConnection,
				hoverPort,
				draggedPort,
				canvas.ScreenToWorld(input.MousePosition),
				menu,
				editor,
				showSource,
				fileStatus,
				fileStatusError,
				window.Width,
				window.Height
			);
		}

		frame.Present();
	}

	private void Update()
	{
		session.PollExecution();
		Vector2 mouse = input.MousePosition;
		Vector2 world = canvas.ScreenToWorld(mouse);
		Vector2 delta = mouse - previousMouse;

		previousMouse = mouse;

		if (menu.IsOpen)
		{
			UpdateMenu(mouse, world);
			return;
		}

		if (editor.IsActive)
		{
			UpdateEditor();
			return;
		}

		bool canvasPoint = VisualEditorLayout.IsCanvasPoint(mouse, window.Width, window.Height);

		if (scrollDelta != 0 && canvasPoint)
		{
			canvas.ZoomAt(mouse, scrollDelta);
		}

		if (input.IsMouseButtonDown(MouseButton.Middle) && canvasPoint)
		{
			canvas.PanBy(delta);
		}

		hoverPort = canvasPoint
			? VisualNodeHitTester.FindPort(session.CurrentFunction.Graph, canvas, world)
			: null;

		UpdateShortcuts();

		if (input.WasMouseButtonPressed(MouseButton.Right) && canvasPoint)
		{
			menu.Open(mouse, world, window.Width, window.Height);
			return;
		}

		if (input.WasMouseButtonPressed(MouseButton.Left))
		{
			HandleLeftPress(mouse, world);
		}

		if (draggedNode != null && input.IsMouseButtonDown(MouseButton.Left))
		{
			Vector2 movement = world - dragStartWorld;

			foreach (KeyValuePair<Guid, Vector2> start in dragStartPositions)
			{
				VisualNode node = session.CurrentFunction.Graph.Nodes.FirstOrDefault(candidate => candidate.Id == start.Key);

				if (node != null)
				{
					node.Position = start.Value + movement;
				}
			}
		}

		if (input.WasMouseButtonReleased(MouseButton.Left))
		{
			HandleLeftRelease(world);
		}
	}

	private void UpdateMenu(Vector2 mouse, Vector2 world)
	{
		menu.UpdateHover(mouse);
		menu.Scroll(mouse, scrollDelta);

		foreach (string characters in typedCharacters)
		{
			menu.Append(characters);
		}

		if (input.WasKeyPressed(Key.Backspace))
		{
			menu.Backspace();
		}

		if (input.WasKeyPressed(Key.Left))
		{
			menu.MoveCategory(-1);
		}

		if (input.WasKeyPressed(Key.Right))
		{
			menu.MoveCategory(1);
		}

		if (input.WasKeyPressed(Key.Up))
		{
			menu.MoveNode(-1);
		}

		if (input.WasKeyPressed(Key.Down))
		{
			menu.MoveNode(1);
		}

		if (input.WasKeyPressed(Key.Escape))
		{
			menu.Escape();
		}

		if (input.WasKeyPressed(Key.Enter))
		{
			CreateFromMenu(menu.Activate());
		}

		if (input.WasMouseButtonPressed(MouseButton.Left))
		{
			CreateFromMenu(menu.Click(mouse));
		}

		if (input.WasMouseButtonPressed(MouseButton.Right))
		{
			menu.Open(mouse, world, window.Width, window.Height);
		}
	}

	private void UpdateEditor()
	{
		foreach (string characters in typedCharacters)
		{
			editor.Append(characters);
		}

		if (input.WasKeyPressed(Key.Backspace))
		{
			editor.Backspace();
		}

		if (input.WasKeyPressed(Key.Enter))
		{
			editor.Commit();
			session.CommitMutation(editMutation);
			editMutation = null;
		}

		if (input.WasKeyPressed(Key.Escape))
		{
			editor.Cancel();
			editMutation = null;
		}
	}

	private void UpdateShortcuts()
	{
		bool control = input.IsKeyDown(Key.LeftControl) || input.IsKeyDown(Key.RightControl);
		bool shift = input.IsKeyDown(Key.LeftShift) || input.IsKeyDown(Key.RightShift);

		if (input.WasKeyPressed(Key.Escape))
		{
			if (draggedPort != null)
			{
				draggedPort = null;
			}
			else
			{
				selectedNodes.Clear();
				selectedConnection = null;
			}
		}

		if (input.WasKeyPressed(Key.Delete))
		{
			DeleteSelection();
		}

		if (control && input.WasKeyPressed(Key.S))
		{
			SaveProgram();
		}

		if (control && input.WasKeyPressed(Key.O))
		{
			LoadProgram();
		}

		if (control && input.WasKeyPressed(Key.Z))
		{
			if (shift)
			{
				Redo();
			}
			else
			{
				Undo();
			}
		}

		if (control && input.WasKeyPressed(Key.Y))
		{
			Redo();
		}

		if (control && input.WasKeyPressed(Key.C))
		{
			session.Copy(selectedNodes);
		}

		if (control && input.WasKeyPressed(Key.V))
		{
			IReadOnlyList<VisualNode> pasted = session.Paste(new Vector2(40, -40));

			selectedNodes.Clear();
			selectedNodes.UnionWith(pasted.Select(node => node.Id));
		}

		if (control && shift && input.WasKeyPressed(Key.B))
		{
			session.Refresh();
			session.SetOutput(session.Generation.Success ? "Generated C# successfully." : "Build validation failed.");
		}

		if (input.WasKeyPressed(Key.F5))
		{
			session.Run();
		}

		if (input.WasKeyPressed(Key.F6))
		{
			showSource = !showSource;
		}
	}

	private void HandleLeftPress(Vector2 screen, Vector2 world)
	{
		if (HandlePanelPress(screen))
		{
			return;
		}

		if (!VisualEditorLayout.IsCanvasPoint(screen, window.Width, window.Height))
		{
			return;
		}

		VisualGraph graph = session.CurrentFunction.Graph;
		VisualPort port = VisualNodeHitTester.FindPort(graph, canvas, world);

		if (port != null)
		{
			if (port.Direction == VisualPortDirection.Input
				&& graph.TryGetInputConnection(port, out VisualConnection existing))
			{
				session.Disconnect(existing);
				draggedPort = existing.Output;
			}
			else
			{
				draggedPort = port;
			}

			selectedNodes.Clear();
			selectedConnection = null;
			return;
		}

		VisualNode node = VisualNodeHitTester.FindNode(graph, world);

		if (node != null)
		{
			IReadOnlyList<VisualEditableField> fields = VisualNodeGeometry.Fields(node);

			for (int index = 0; index < fields.Count; index++)
			{
				if (VisualNodeGeometry.FieldBounds(node, index).Contains(world))
				{
					editMutation = session.CaptureMutation();
					editor.Begin(fields[index]);
					return;
				}
			}

			bool shift = input.IsKeyDown(Key.LeftShift) || input.IsKeyDown(Key.RightShift);

			if (shift)
			{
				if (!selectedNodes.Add(node.Id))
				{
					selectedNodes.Remove(node.Id);
				}
			}
			else if (!selectedNodes.Contains(node.Id))
			{
				selectedNodes.Clear();
				selectedNodes.Add(node.Id);
			}

			selectedConnection = null;

			if (VisualNodeGeometry.HeaderOf(node).Contains(world))
			{
				draggedNode = node;
				dragStartWorld = world;
				dragStartPositions = graph.Nodes
					.Where(candidate => selectedNodes.Contains(candidate.Id))
					.ToDictionary(candidate => candidate.Id, candidate => candidate.Position);
				dragMutation = session.CaptureMutation();
			}

			return;
		}

		selectedNodes.Clear();
		selectedConnection = VisualNodeHitTester.FindConnection(graph, canvas, world);
	}

	private bool HandlePanelPress(Vector2 screen)
	{
		if (HandleDiagnosticPress(screen))
		{
			return true;
		}

		if (VisualEditorLayout.RunButton(window.Height).Contains(screen))
		{
			session.Run();
			return true;
		}

		if (VisualEditorLayout.StopButton(window.Height).Contains(screen))
		{
			session.Stop();
			return true;
		}

		if (VisualEditorLayout.CodeButton(window.Height).Contains(screen))
		{
			showSource = !showSource;
			return true;
		}

		if (VisualEditorLayout.AddFunctionButton().Contains(screen))
		{
			session.AddFunction(canvas.CaptureVisual());
			canvas.Apply(session.CurrentFunction.View);
			selectedNodes.Clear();
			selectedConnection = null;
			return true;
		}

		if (VisualEditorLayout.AddParameterButton().Contains(screen))
		{
			session.AddParameter();
			return true;
		}

		if (HandleFunctionTab(screen))
		{
			return true;
		}

		IReadOnlyList<VisualNodeDefinition> definitions = VisualEditorLayout.ToolboxDefinitions(session.Catalog, window.Height);

		for (int index = 0; index < definitions.Count; index++)
		{
			if (VisualEditorLayout.ToolboxRow(index, window.Height).Contains(screen))
			{
				VisualNode node = session.AddNode(definitions[index], canvas.ScreenToWorld(new Vector2(window.Width * .5f, window.Height * .55f)));

				SelectOnly(node.Id);
				return true;
			}
		}

		if (showSource)
		{
			int line = VisualEditorLayout.SourceLineAt(screen, window.Width, window.Height);

			if (line > 0)
			{
				GeneratedNodeSpan span = session.Generation.SourceMap.Find(line);

				if (span != null)
				{
					if (span.FunctionId != session.CurrentFunction.Id)
					{
						session.SelectFunction(span.FunctionId, canvas.CaptureVisual());
						canvas.Apply(session.CurrentFunction.View);
					}

					SelectOnly(span.NodeId);
				}

				return true;
			}
		}

		return false;
	}

	private bool HandleDiagnosticPress(Vector2 screen)
	{
		if (screen.X > 610 || screen.Y > VisualEditorLayout.BottomHeight - 45)
		{
			return false;
		}

		VisualProgramDiagnostic[] diagnostics = session.Validation.Diagnostics.Take(4).ToArray();
		float y = VisualEditorLayout.BottomHeight - 58;

		for (int index = 0; index < diagnostics.Length; index++)
		{
			if (new Bounds(10, y - 5, 590, 20).Contains(screen))
			{
				VisualProgramDiagnostic diagnostic = diagnostics[index];

				if (diagnostic.FunctionId.HasValue
					&& diagnostic.FunctionId.Value != session.CurrentFunction.Id)
				{
					session.SelectFunction(diagnostic.FunctionId.Value, canvas.CaptureVisual());
					canvas.Apply(session.CurrentFunction.View);
				}

				if (diagnostic.NodeId.HasValue)
				{
					SelectOnly(diagnostic.NodeId.Value);
				}

				return true;
			}

			y -= 22;
		}

		return false;
	}

	private bool HandleFunctionTab(Vector2 screen)
	{
		float x = 570;

		foreach (VisualFunction function in session.Program.Functions)
		{
			Bounds bounds = new Bounds(x, window.Height - 52, 130, 36);

			if (bounds.Contains(screen))
			{
				session.SelectFunction(function.Id, canvas.CaptureVisual());
				canvas.Apply(session.CurrentFunction.View);
				selectedNodes.Clear();
				selectedConnection = null;
				return true;
			}

			x += 138;
		}

		return false;
	}

	private void HandleLeftRelease(Vector2 world)
	{
		if (draggedNode != null)
		{
			session.CommitMutation(dragMutation);
			draggedNode = null;
			dragStartPositions = null;
			dragMutation = null;
		}

		if (draggedPort == null)
		{
			return;
		}

		VisualPort target = VisualNodeHitTester.FindPort(session.CurrentFunction.Graph, canvas, world);

		if (target != null && target != draggedPort)
		{
			VisualPort output = draggedPort.Direction == VisualPortDirection.Output ? draggedPort : target;
			VisualPort inputPort = draggedPort.Direction == VisualPortDirection.Input ? draggedPort : target;

			if (session.TryConnect(output, inputPort, out VisualConnection connection))
			{
				selectedConnection = connection;
			}
		}

		draggedPort = null;
	}

	private void CreateFromMenu(VisualNodeDefinition definition)
	{
		if (definition == null)
		{
			return;
		}

		VisualNode node = session.AddNode(definition, menu.InsertionWorld);

		SelectOnly(node.Id);
		menu.Close();
	}

	private void DeleteSelection()
	{
		if (selectedConnection != null)
		{
			session.Disconnect(selectedConnection);
			selectedConnection = null;
		}
		else if (selectedNodes.Count > 0)
		{
			session.RemoveNodes(selectedNodes);
			selectedNodes.Clear();
		}
	}

	private void Undo()
	{
		Guid functionId = session.CurrentFunction.Id;
		VisualProgramViewState currentView = canvas.CaptureVisual();

		if (session.Undo())
		{
			ApplyHistoryView(functionId, currentView);
			selectedNodes.Clear();
			selectedConnection = null;
		}
	}

	private void Redo()
	{
		Guid functionId = session.CurrentFunction.Id;
		VisualProgramViewState currentView = canvas.CaptureVisual();

		if (session.Redo())
		{
			ApplyHistoryView(functionId, currentView);
			selectedNodes.Clear();
			selectedConnection = null;
		}
	}

	private void ApplyHistoryView(Guid previousFunctionId, VisualProgramViewState currentView)
	{
		if (session.CurrentFunction.Id == previousFunctionId)
		{
			session.CurrentFunction.View = currentView;
			canvas.Apply(currentView);
			return;
		}

		canvas.Apply(session.CurrentFunction.View);
	}

	private void SelectOnly(Guid id)
	{
		selectedNodes.Clear();
		selectedNodes.Add(id);
		selectedConnection = null;
	}

	private void SaveProgram()
	{
		try
		{
			session.Save(filePath, canvas.CaptureVisual());
			fileStatus = "Saved " + Path.GetFileName(filePath);
			fileStatusError = false;
		}
		catch (Exception exception)
		{
			fileStatus = "Save failed: " + exception.Message;
			fileStatusError = true;
		}
	}

	private void LoadProgram()
	{
		if (!File.Exists(filePath))
		{
			fileStatus = "File not found: " + Path.GetFileName(filePath);
			fileStatusError = true;
			return;
		}

		if (!session.TryLoad(filePath, out IReadOnlyList<string> errors))
		{
			fileStatus = "Load failed: " + string.Join(" | ", errors);
			fileStatusError = true;
			return;
		}

		canvas.Apply(session.CurrentFunction.View);
		selectedNodes.Clear();
		selectedConnection = null;
		fileStatus = "Loaded " + Path.GetFileName(filePath);
		fileStatusError = false;
	}

	private void ConfigureProjection()
	{
		renderCamera.SetOrthogonal(0, 0, window.Width, window.Height);
	}

	private void OnResize(object sender, WindowResizeEventArgs args)
	{
		ConfigureProjection();
	}

	private void OnScroll(object sender, ScrollEventArgs args)
	{
		scrollDelta += args.Offset.Y;
	}

	private void OnTextInput(object sender, TextInputEventArgs args)
	{
		typedCharacters.Add(args.Text);
	}
}
