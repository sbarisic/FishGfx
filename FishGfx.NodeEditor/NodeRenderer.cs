using System;
using System.Numerics;
using FishGfx;
using FishGfx.Graphics;
using FishGfx.NodeGraph;

namespace FishGfx.NodeEditor;

internal sealed partial class NodeRenderer
{
	internal static readonly Color CanvasColor = new Color(24, 25, 27);

	private static readonly Color GridColor = new Color(39, 41, 44);
	private static readonly Color NodeColor = new Color(63, 64, 66);
	private static readonly Color HeaderColor = new Color(72, 73, 76);
	private static readonly Color TextColor = new Color(224, 224, 226);

	private readonly GraphicsFont font;
	private readonly GraphicsFont menuFont;
	private RenderPass pass;

	internal NodeRenderer(GraphicsFont font, GraphicsFont menuFont)
	{
		this.font = font;
		this.menuFont = menuFont;
	}

	internal void Draw(
		RenderPass renderPass,
		FunctionGraph graph,
		NodeCanvas canvas,
		object selected,
		NodePort hoverPort,
		NodePort dragPort,
		Vector2 mouseWorld,
		ContextMenu menu,
		InlineValueEditor editor,
		NodeEvaluationResult result,
		string fileStatus,
		bool fileStatusError,
		int width,
		int height
	)
	{
		pass = renderPass ?? throw new ArgumentNullException(nameof(renderPass));

		DrawGrid(canvas, width, height);

		foreach (NodeConnection connection in graph.Connections)
		{
			DrawConnection(connection, canvas, selected == connection);
		}

		if (dragPort != null)
		{
			DrawDraggedConnection(dragPort, canvas, mouseWorld);
		}

		foreach (FunctionNode node in graph.Nodes)
		{
			DrawNode(node, canvas, selected == node, hoverPort, editor);
		}

		DrawStatus(result, fileStatus, fileStatusError, height);

		if (menu.IsOpen)
		{
			DrawMenu(menu);
		}
	}

	internal void Dispose()
	{
		font.Dispose();
		menuFont.Dispose();
	}

	private void DrawDraggedConnection(NodePort dragPort, NodeCanvas canvas, Vector2 mouseWorld)
	{
		Vector2 fixedPoint = Screen(canvas, NodeGeometry.PortPosition(dragPort));
		Vector2 cursor = Screen(canvas, mouseWorld);
		Vector2 start = dragPort.Direction == NodePortDirection.Output ? fixedPoint : cursor;
		Vector2 end = dragPort.Direction == NodePortDirection.Output ? cursor : fixedPoint;

		DrawBezier(start, end, PortColor(dragPort.Type), 5 * canvas.Zoom);
	}

	private void DrawStatus(
		NodeEvaluationResult result,
		string fileStatus,
		bool fileStatusError,
		int height
	)
	{
		pass.DrawText(font, new Vector2(22, height - 42), "NODE EDITOR", new Color(145, 151, 160), 23);

		Color evaluateColor = result == null || result.Success
			? new Color(55, 132, 86)
			: new Color(172, 68, 68);

		pass.FillRoundedRectangle(220, height - 58, 132, 38, new CornerRadii(5), evaluateColor, 3);
		pass.DrawText(font, new Vector2(240, height - 49), "Evaluate  F5", Color.White, 17);

		if (result != null)
		{
			pass.DrawText(font, new Vector2(370, height - 48), result.Summary, evaluateColor, 18);
		}

		if (!string.IsNullOrEmpty(fileStatus))
		{
			Color statusColor = fileStatusError
				? new Color(235, 110, 110)
				: new Color(115, 205, 145);

			pass.DrawText(font, new Vector2(700, height - 48), fileStatus, statusColor, 18);
		}

		pass.DrawText(
			font,
			new Vector2(22, 22),
			"Right click: add node   Middle drag: pan   Wheel: zoom   Delete: remove",
			new Color(115, 120, 128),
			18
		);
	}

	private static Color PortColor(Type type)
	{
		uint hash = 2166136261;

		foreach (char character in type.FullName ?? type.Name)
		{
			hash = (hash ^ character) * 16777619;
		}

		return new Color(
			(byte)(70 + hash % 150),
			(byte)(70 + (hash >> 8) % 150),
			(byte)(70 + (hash >> 16) % 150)
		);
	}

	private static Vector2 Screen(NodeCanvas canvas, Vector2 world)
	{
		return canvas.WorldToScreen(world);
	}
}
