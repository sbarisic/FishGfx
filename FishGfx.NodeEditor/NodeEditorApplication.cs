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

internal sealed class NodeEditorApplication
{
	private const int InitialWidth = 1920;
	private const int InitialHeight = 1080;

	private readonly Camera renderCamera = new Camera();
	private readonly NodeEditorSession session = new NodeEditorSession();
	private readonly NodeCanvas canvas = new NodeCanvas();
	private readonly ContextMenu menu;
	private readonly InlineValueEditor editor = new InlineValueEditor();
	private readonly List<string> typedCharacters = new List<string>();
	private readonly bool autoMode;
	private readonly string layoutPath;

	private RenderWindow window;
	private InputManager input;
	private NodeRenderer renderer;
	private object selected;
	private FunctionNode draggedNode;
	private Vector2 nodeDragOffset;
	private NodePort draggedPort;
	private NodePort hoverPort;
	private Vector2 previousMouse;
	private float scrollDelta;
	private string fileStatus;
	private bool fileStatusError;

	internal NodeEditorApplication(string[] args)
	{
		autoMode = args.Any(a => string.Equals(a, "--auto", StringComparison.OrdinalIgnoreCase));
		layoutPath = args.FirstOrDefault(argument => !argument.StartsWith("--", StringComparison.Ordinal))
			?? Path.Combine(AppContext.BaseDirectory, "node-layout.json");
		menu = new ContextMenu(session.Functions);
	}

	internal void Run()
	{
		window = new RenderWindow(InitialWidth, InitialHeight, "FishGfx Visual Node Editor", true);
		input = new InputManager(window);

		TrueTypeFont graphFont = new(
			Path.Combine(AppContext.BaseDirectory, "data", "fonts", "Consolas-Regular.ttf")
		);
		TrueTypeFont menuFont = new(
			Path.Combine(AppContext.BaseDirectory, "data", "fonts", "Consolas-Regular.ttf")
		);

		renderer = new NodeRenderer(graphFont, menuFont);
		window.Scrolled += OnScroll;
		window.TextInput += OnTextInput;
		window.Resized += OnResize;

		if (!autoMode && File.Exists(layoutPath))
		{
			LoadLayout();
		}

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
			RenderState editorState = RenderState.Default with
			{
				CullMode = CullMode.None,
				DepthTestEnabled = false,
				DepthWriteEnabled = false,
			};
			using RenderFrame frame = window.Graphics.BeginFrame();
			using (RenderPass pass = frame.BeginPass(window.Graphics.Backbuffer, new RenderPassDescriptor
			{
				View = new RenderView(renderCamera),
				State = editorState,
				ColorLoadAction = RenderLoadAction.Clear,
				DepthLoadAction = RenderLoadAction.Clear,
				StencilLoadAction = RenderLoadAction.Clear,
				ClearColor = NodeRenderer.CanvasColor,
			}))
			{
				renderer.Draw(
					pass,
					session.Graph,
					canvas,
					selected,
					hoverPort,
					draggedPort,
					canvas.ScreenToWorld(input.MousePosition),
					menu,
					editor,
					session.EvaluationResult,
					fileStatus,
					fileStatusError,
					window.Width,
					window.Height
				);
			}
			frame.Present();

			if (autoMode && runtime.Elapsed.TotalSeconds >= 2)
			{
				window.IsCloseRequested = true;
			}
		}

		window.Scrolled -= OnScroll;
		window.TextInput -= OnTextInput;
		window.Resized -= OnResize;
		renderer.Dispose();
		input.Dispose();
		window.Graphics.CollectGarbage();
		window.Dispose();
	}

	private void Update()
	{
		Vector2 mouse = input.MousePosition;
		Vector2 world = canvas.ScreenToWorld(mouse);
		Vector2 delta = mouse - previousMouse;
		previousMouse = mouse;

		if (menu.IsOpen)
		{
			menu.UpdateHover(mouse);
			menu.Scroll(mouse, scrollDelta);

			foreach (string chars in typedCharacters)
			{
				menu.Append(chars);
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
				menu.MoveFunction(-1);
			}

			if (input.WasKeyPressed(Key.Down))
			{
				menu.MoveFunction(1);
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

			return;
		}

		if (scrollDelta != 0)
		{
			canvas.ZoomAt(mouse, scrollDelta);
		}

		if (input.IsMouseButtonDown(MouseButton.Middle))
		{
			canvas.PanBy(delta);
		}

		hoverPort = NodeHitTester.FindPort(session.Graph, canvas, world);

		if (editor.IsActive)
		{
			foreach (string chars in typedCharacters)
			{
				editor.Append(chars);
			}

			if (input.WasKeyPressed(Key.Backspace))
			{
				editor.Backspace();
			}

			if (input.WasKeyPressed(Key.Enter) && editor.Commit())
			{
				session.InvalidateEvaluation();
			}

			if (input.WasKeyPressed(Key.Escape))
			{
				editor.Cancel();
			}

			return;
		}

		if (input.WasKeyPressed(Key.Escape))
		{
			if (draggedPort != null)
			{
				draggedPort = null;
			}
			else
			{
				window.IsCloseRequested = true;
			}
		}

		if (input.WasKeyPressed(Key.Delete))
		{
			DeleteSelected();
		}

		bool control = input.IsKeyDown(Key.LeftControl) || input.IsKeyDown(Key.RightControl);

		if (control && input.WasKeyPressed(Key.S))
		{
			SaveLayout();
		}

		if (control && input.WasKeyPressed(Key.O))
		{
			LoadLayout();
		}

		if (input.WasKeyPressed(Key.F5))
		{
			session.Evaluate();
		}

		if (input.WasMouseButtonPressed(MouseButton.Right))
		{
			menu.Open(mouse, world, window.Width, window.Height);
			selected = null;
		}

		if (input.WasMouseButtonPressed(MouseButton.Left))
		{
			HandleLeftPress(world);
		}

		if (draggedNode != null && input.IsMouseButtonDown(MouseButton.Left))
		{
			draggedNode.Position = world - nodeDragOffset;
		}

		if (input.WasMouseButtonReleased(MouseButton.Left))
		{
			HandleLeftRelease(world);
		}
	}

	private void HandleLeftPress(Vector2 world)
	{
		Vector2 screen = input.MousePosition;

		if (
			screen.X >= 220
			&& screen.X <= 352
			&& screen.Y >= window.Height - 58
			&& screen.Y <= window.Height - 20
		)
		{
			session.Evaluate();
			return;
		}

		FunctionNode nodeAt = NodeHitTester.FindNode(session.Graph, world);

		if (nodeAt != null && NodeGeometry.CloseOf(nodeAt).Contains(world))
		{
			session.RemoveNode(nodeAt);

			if (selected == nodeAt)
			{
				selected = null;
			}

			return;
		}

		NodePort port = NodeHitTester.FindPort(session.Graph, canvas, world);

		if (port != null)
		{
			if (port.Direction == NodePortDirection.Input)
			{
				if (session.Graph.TryGetInputConnection(port, out NodeConnection existing))
				{
					session.Disconnect(existing);
					draggedPort = existing.Output;
					selected = null;
					return;
				}
			}

			draggedPort = port;
			selected = null;
			return;
		}

		if (nodeAt != null)
		{
			for (int index = 0; index < nodeAt.InlineValues.Count; index++)
			{
				if (NodeGeometry.ValueBounds(nodeAt, index).Contains(world))
				{
					editor.Begin(nodeAt.InlineValues[index]);
					return;
				}
			}

			selected = nodeAt;

			if (NodeGeometry.HeaderOf(nodeAt).Contains(world))
			{
				draggedNode = nodeAt;
				nodeDragOffset = world - nodeAt.Position;
			}

			return;
		}

		NodeConnection connection = NodeHitTester.FindConnection(session.Graph, canvas, world);
		selected = connection;
	}

	private void HandleLeftRelease(Vector2 world)
	{
		draggedNode = null;

		if (draggedPort == null)
		{
			return;
		}

		NodePort target = NodeHitTester.FindPort(session.Graph, canvas, world);

		if (target != null && target != draggedPort)
		{
			NodePort output = draggedPort.Direction == NodePortDirection.Output ? draggedPort : target;
			NodePort inputPort = draggedPort.Direction == NodePortDirection.Input ? draggedPort : target;

			if (session.TryConnect(output, inputPort, out NodeConnection connection))
			{
				selected = connection;
			}
		}

		draggedPort = null;
	}

	private void CreateFromMenu(NodeFunctionDescriptor descriptor)
	{
		if (descriptor == null)
		{
			return;
		}

		selected = session.AddNode(descriptor, menu.InsertionWorld);
		menu.Close();
	}

	private void DeleteSelected()
	{
		if (selected is FunctionNode node)
		{
			session.RemoveNode(node);
		}
		else if (selected is NodeConnection connection)
		{
			session.Disconnect(connection);
		}

		selected = null;
	}

	private void SaveLayout()
	{
		try
		{
			session.Save(layoutPath, canvas.Capture());
			fileStatus = "Saved " + Path.GetFileName(layoutPath);
			fileStatusError = false;
		}
		catch (Exception ex)
		{
			fileStatus = "Save failed: " + ex.Message;
			fileStatusError = true;
		}
	}

	private bool LoadLayout()
	{
		if (!session.TryLoad(layoutPath, out NodeGraphViewState view, out IReadOnlyList<string> errors))
		{
			fileStatus = "Load failed: " + string.Join(" | ", errors);
			fileStatusError = true;
			return false;
		}

		canvas.Apply(view);
		selected = null;
		draggedNode = null;
		draggedPort = null;
		fileStatus = "Loaded " + Path.GetFileName(layoutPath);
		fileStatusError = false;
		return true;
	}

	private void ConfigureProjection()
	{
		renderCamera.SetOrthogonal(0, 0, window.Width, window.Height);
	}

	private void OnResize(object sender, WindowResizeEventArgs args) => ConfigureProjection();

	private void OnScroll(object sender, ScrollEventArgs args) => scrollDelta += args.Offset.Y;

	private void OnTextInput(object sender, TextInputEventArgs args) => typedCharacters.Add(args.Text);
}
