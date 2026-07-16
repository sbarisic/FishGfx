using System;
using System.Collections.Generic;
using System.Numerics;
using FishGfx;
using FishGfx.Graphics;
using FishGfx.NodeGraph;

namespace FishGfx.NodeEditor;

internal sealed partial class VisualNodeRenderer
{
	internal static readonly Color CanvasColor = new Color(24, 25, 28);

	private static readonly Color GridColor = new Color(39, 42, 47);
	private static readonly Color PanelColor = new Color(31, 33, 37);
	private static readonly Color PanelBorder = new Color(57, 61, 68);
	private static readonly Color NodeColor = new Color(58, 61, 67);
	private static readonly Color HeaderColor = new Color(70, 74, 82);
	private static readonly Color TextColor = new Color(226, 228, 232);
	private static readonly Color MutedText = new Color(139, 145, 155);
	private static readonly Color ExecutionColor = new Color(235, 237, 241);
	private readonly GraphicsFont font;
	private readonly GraphicsFont interfaceFont;
	private RenderPass pass;

	internal VisualNodeRenderer(GraphicsFont font, GraphicsFont interfaceFont)
	{
		this.font = font;
		this.interfaceFont = interfaceFont;
	}

	internal void Draw(
		RenderPass renderPass,
		VisualEditorSession session,
		NodeCanvas canvas,
		IReadOnlySet<Guid> selectedNodes,
		VisualConnection selectedConnection,
		VisualPort hoverPort,
		VisualPort draggedPort,
		Vector2 mouseWorld,
		VisualContextMenu menu,
		VisualInlineEditor editor,
		bool showSource,
		string fileStatus,
		bool fileStatusError,
		int width,
		int height
	)
	{
		pass = renderPass ?? throw new ArgumentNullException(nameof(renderPass));

		DrawGrid(canvas, width, height);
		DrawGraph(
			session.CurrentFunction.Graph,
			canvas,
			selectedNodes,
			selectedConnection,
			hoverPort,
			draggedPort,
			mouseWorld,
			editor
		);
		DrawPanels(session, selectedNodes, showSource, fileStatus, fileStatusError, width, height);

		if (menu.IsOpen)
		{
			DrawMenu(menu);
		}
	}

	internal void Dispose()
	{
		font.Dispose();
		interfaceFont.Dispose();
	}

	private static Vector2 Screen(NodeCanvas canvas, Vector2 world)
	{
		return canvas.WorldToScreen(world);
	}

	private static Color PortColor(VisualPort port)
	{
		if (port.Kind == VisualPortKind.Execution)
		{
			return ExecutionColor;
		}

		uint hash = 2166136261;

		foreach (char character in port.Type.ToString())
		{
			hash = (hash ^ character) * 16777619;
		}

		return new Color(
			(byte)(80 + hash % 140),
			(byte)(80 + (hash >> 8) % 140),
			(byte)(80 + (hash >> 16) % 140)
		);
	}
}
