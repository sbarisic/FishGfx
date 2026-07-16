using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using FishGfx;
using FishGfx.Graphics;
using FishGfx.NodeGraph;

namespace FishGfx.NodeEditor;

internal sealed partial class VisualNodeRenderer
{
	private void DrawGrid(NodeCanvas canvas, int width, int height)
	{
		float spacing = 48 * canvas.Zoom;
		float startX = canvas.Pan.X % spacing;
		float startY = canvas.Pan.Y % spacing;

		for (float x = startX; x < width; x += spacing)
		{
			pass.DrawLine(
				new Vertex2(new Vector2(x, VisualEditorLayout.BottomHeight), GridColor),
				new Vertex2(new Vector2(x, height - VisualEditorLayout.TopHeight), GridColor),
				1
			);
		}

		for (float y = startY; y < height; y += spacing)
		{
			if (y >= VisualEditorLayout.BottomHeight && y <= height - VisualEditorLayout.TopHeight)
			{
				pass.DrawLine(
					new Vertex2(new Vector2(VisualEditorLayout.LeftWidth, y), GridColor),
					new Vertex2(new Vector2(width - VisualEditorLayout.RightWidth, y), GridColor),
					1
				);
			}
		}
	}

	private void DrawGraph(
		VisualGraph graph,
		NodeCanvas canvas,
		IReadOnlySet<Guid> selectedNodes,
		VisualConnection selectedConnection,
		VisualPort hoverPort,
		VisualPort draggedPort,
		Vector2 mouseWorld,
		VisualInlineEditor editor
	)
	{
		foreach (VisualConnection connection in graph.Connections)
		{
			DrawConnection(connection, canvas, connection == selectedConnection);
		}

		if (draggedPort != null)
		{
			DrawDraggedConnection(draggedPort, canvas, mouseWorld);
		}

		foreach (VisualNode node in graph.Nodes)
		{
			DrawNode(node, canvas, selectedNodes.Contains(node.Id), hoverPort, editor);
		}
	}

	private void DrawConnection(VisualConnection connection, NodeCanvas canvas, bool selected)
	{
		DrawBezier(
			Screen(canvas, VisualNodeGeometry.PortPosition(connection.Output)),
			Screen(canvas, VisualNodeGeometry.PortPosition(connection.Input)),
			selected ? new Color(120, 190, 255) : PortColor(connection.Output),
			(selected ? 7 : connection.Kind == VisualPortKind.Execution ? 5 : 4) * canvas.Zoom
		);
	}

	private void DrawDraggedConnection(VisualPort port, NodeCanvas canvas, Vector2 mouseWorld)
	{
		Vector2 fixedPoint = Screen(canvas, VisualNodeGeometry.PortPosition(port));
		Vector2 cursor = Screen(canvas, mouseWorld);
		Vector2 start = port.Direction == VisualPortDirection.Output ? fixedPoint : cursor;
		Vector2 end = port.Direction == VisualPortDirection.Output ? cursor : fixedPoint;

		DrawBezier(start, end, PortColor(port), 5 * canvas.Zoom);
	}

	private void DrawBezier(Vector2 start, Vector2 end, Color color, float thickness)
	{
		float reach = Math.Max(55, Math.Abs(end.X - start.X) * .4f);

		pass.DrawCubicBezier(
			start,
			start + new Vector2(reach, 0),
			end - new Vector2(reach, 0),
			end,
			Math.Max(1, thickness),
			color,
			28
		);
	}

	private void DrawNode(
		VisualNode node,
		NodeCanvas canvas,
		bool selected,
		VisualPort hoverPort,
		VisualInlineEditor editor
	)
	{
		Vector2 position = Screen(canvas, node.Position);
		float zoom = canvas.Zoom;
		float width = node.Width * zoom;
		float height = VisualNodeGeometry.HeightOf(node) * zoom;
		Color body = node.IsMissingDefinition
			? new Color(104, 52, 58)
			: selected ? new Color(77, 83, 93) : NodeColor;
		Color header = RoleColor(node.Role);

		pass.FillRoundedRectangle(position, new Vector2(width, height), new CornerRadii(7 * zoom), body, 4);
		pass.FillRoundedRectangle(
			new Vector2(position.X, position.Y + height - VisualNodeGeometry.HeaderHeight * zoom),
			new Vector2(width, VisualNodeGeometry.HeaderHeight * zoom),
			new CornerRadii(7 * zoom),
			header,
			4
		);

		if (selected)
		{
			pass.DrawRoundedRectangle(
				position,
				new Vector2(width, height),
				new CornerRadii(7 * zoom),
				Math.Max(2, 2 * zoom),
				new Color(110, 179, 255),
				4
			);
		}

		pass.DrawText(
			font,
			position + new Vector2(14 * zoom, height - 31 * zoom),
			node.Title,
			TextColor,
			Math.Max(11, 19 * zoom)
		);

		foreach (VisualPort port in node.Inputs.Concat(node.Outputs))
		{
			DrawPort(port, canvas, hoverPort == port);
		}

		IReadOnlyList<VisualEditableField> fields = VisualNodeGeometry.Fields(node);

		for (int index = 0; index < fields.Count; index++)
		{
			DrawField(node, fields[index], index, canvas, editor);
		}
	}

	private void DrawPort(VisualPort port, NodeCanvas canvas, bool hover)
	{
		Vector2 position = Screen(canvas, VisualNodeGeometry.PortPosition(port));
		float zoom = canvas.Zoom;
		Color color = hover ? Color.White : PortColor(port);

		if (port.Kind == VisualPortKind.Execution)
		{
			float size = (hover ? 18 : 14) * zoom;
			pass.FillRoundedRectangle(
				position - new Vector2(size / 2),
				new Vector2(size),
				new CornerRadii(2 * zoom),
				color,
				2
			);
		}
		else
		{
			pass.FillCircle(position, (hover ? 10 : VisualNodeGeometry.PortRadius) * zoom, color, 20);
		}

		float offset = port.Direction == VisualPortDirection.Input ? 14 : -14;
		float textSize = Math.Max(10, 16 * zoom);
		float textWidth = font.Measure(port.Label, textSize).X;
		float x = port.Direction == VisualPortDirection.Input ? offset : offset - textWidth;

		pass.DrawText(font, position + new Vector2(x, -8 * zoom), port.Label, TextColor, textSize);

		if (hover)
		{
			string type = port.Kind == VisualPortKind.Execution
				? "execution"
				: VisualValueTypes.DisplayName(port.Type);

			pass.DrawText(font, position + new Vector2(12 * zoom, 12 * zoom), type, Color.White, Math.Max(9, 13 * zoom));
		}
	}

	private void DrawField(
		VisualNode node,
		VisualEditableField field,
		int index,
		NodeCanvas canvas,
		VisualInlineEditor editor
	)
	{
		Bounds bounds = VisualNodeGeometry.FieldBounds(node, index);
		Vector2 position = Screen(canvas, new Vector2(bounds.X, bounds.Y));
		float zoom = canvas.Zoom;
		string value = editor.IsEditing(field) ? editor.Text + "|" : field.Value;

		pass.DrawText(
			font,
			Screen(canvas, new Vector2(node.Position.X + 14, bounds.Y + 4)),
			field.Label,
			MutedText,
			Math.Max(10, 15 * zoom)
		);
		pass.FillRoundedRectangle(
			position,
			new Vector2(bounds.Width * zoom, bounds.Height * zoom),
			new CornerRadii(3 * zoom),
			new Color(42, 45, 50),
			2
		);
		pass.DrawText(
			font,
			position + new Vector2(7 * zoom, 4 * zoom),
			value,
			TextColor,
			Math.Max(10, 15 * zoom)
		);
	}

	private static Color RoleColor(VisualNodeRole role)
	{
		return role switch
		{
			VisualNodeRole.Entry => new Color(52, 126, 88),
			VisualNodeRole.Branch => new Color(128, 91, 45),
			VisualNodeRole.Loop => new Color(119, 72, 139),
			VisualNodeRole.Merge => new Color(89, 76, 54),
			VisualNodeRole.LoopEnd => new Color(82, 60, 94),
			VisualNodeRole.Expression => new Color(53, 91, 132),
			_ => HeaderColor,
		};
	}
}
