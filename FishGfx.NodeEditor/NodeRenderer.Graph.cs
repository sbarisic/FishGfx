using System;
using System.Linq;
using System.Numerics;
using FishGfx;
using FishGfx.Graphics;
using FishGfx.NodeGraph;

namespace FishGfx.NodeEditor;

internal sealed partial class NodeRenderer
{
	private void DrawGrid(NodeCanvas canvas, int width, int height)
	{
		float spacing = 48 * canvas.Zoom;
		float startX = canvas.Pan.X % spacing;
		float startY = canvas.Pan.Y % spacing;

		for (float x = startX; x < width; x += spacing)
		{
			pass.DrawLine(
				new Vertex2(new Vector2(x, 0), GridColor),
				new Vertex2(new Vector2(x, height), GridColor),
				1
			);
		}

		for (float y = startY; y < height; y += spacing)
		{
			pass.DrawLine(
				new Vertex2(new Vector2(0, y), GridColor),
				new Vertex2(new Vector2(width, y), GridColor),
				1
			);
		}
	}

	private void DrawConnection(NodeConnection connection, NodeCanvas canvas, bool selected)
	{
		DrawBezier(
			Screen(canvas, NodeGeometry.PortPosition(connection.Output)),
			Screen(canvas, NodeGeometry.PortPosition(connection.Input)),
			selected ? Color.White : PortColor(connection.Output.Type),
			(selected ? 8 : 6) * canvas.Zoom
		);
	}

	private void DrawBezier(Vector2 start, Vector2 end, Color color, float thickness)
	{
		float reach = Math.Max(60, Math.Abs(end.X - start.X) * 0.45f);

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
		FunctionNode node,
		NodeCanvas canvas,
		bool selected,
		NodePort hoverPort,
		InlineValueEditor editor
	)
	{
		Vector2 position = Screen(canvas, node.Position);
		float zoom = canvas.Zoom;
		float width = node.Width * zoom;
		float height = NodeGeometry.HeightOf(node) * zoom;
		Color bodyColor = BodyColor(node, selected);

		pass.FillRoundedRectangle(
			position,
			new Vector2(width, height),
			new CornerRadii(7 * zoom),
			bodyColor,
			4
		);
		pass.FillRoundedRectangle(
			new Vector2(position.X, position.Y + height - NodeGeometry.HeaderHeight * zoom),
			new Vector2(width, NodeGeometry.HeaderHeight * zoom),
			new CornerRadii(7 * zoom),
			HeaderColor,
			4
		);

		if (selected)
		{
			pass.DrawRoundedRectangle(
				position,
				new Vector2(width, height),
				new CornerRadii(7 * zoom),
				Math.Max(2, 2 * zoom),
				new Color(118, 179, 255),
				4
			);
		}

		pass.DrawText(
			font,
			position + new Vector2(15 * zoom, height - 31 * zoom),
			node.Title,
			TextColor,
			Math.Max(11, 20 * zoom)
		);

		DrawCloseButton(position, width, height, zoom);

		foreach (NodePort port in node.Inputs)
		{
			DrawPort(port, canvas, hoverPort == port);
		}

		foreach (NodePort port in node.Outputs)
		{
			DrawPort(port, canvas, hoverPort == port);
		}

		for (int index = 0; index < node.InlineValues.Count; index++)
		{
			DrawInlineValue(node, node.InlineValues[index], index, canvas, editor);
		}

		DrawEvaluation(node, position, zoom);
	}

	private void DrawCloseButton(Vector2 nodePosition, float width, float height, float zoom)
	{
		Vector2 close = nodePosition + new Vector2(width - 24 * zoom, height - 21 * zoom);
		Color closeColor = new Color(190, 190, 192);
		float thickness = Math.Max(1, 2 * zoom);

		pass.DrawLine(
			new Vertex2(close + new Vector2(-7, -7) * zoom, closeColor),
			new Vertex2(close + new Vector2(7, 7) * zoom, closeColor),
			thickness
		);
		pass.DrawLine(
			new Vertex2(close + new Vector2(-7, 7) * zoom, closeColor),
			new Vertex2(close + new Vector2(7, -7) * zoom, closeColor),
			thickness
		);
	}

	private void DrawInlineValue(
		FunctionNode node,
		NodeInlineValue value,
		int index,
		NodeCanvas canvas,
		InlineValueEditor editor
	)
	{
		float zoom = canvas.Zoom;
		Bounds bounds = NodeGeometry.ValueBounds(node, index);
		Vector2 position = Screen(canvas, new Vector2(bounds.X, bounds.Y));
		Vector2 labelPosition = Screen(canvas, new Vector2(node.Position.X + 14, bounds.Y + 4));
		Color background = editor.Target == value
			? new Color(43, 45, 49)
			: new Color(57, 58, 61);
		string text = editor.Target == value ? editor.Text + "|" : value.Text;

		pass.DrawText(font, labelPosition, value.Name, TextColor, Math.Max(10, 17 * zoom));
		pass.FillRoundedRectangle(
			position,
			new Vector2(bounds.Width * zoom, bounds.Height * zoom),
			new CornerRadii(3 * zoom),
			background,
			2
		);
		pass.DrawText(
			font,
			position + new Vector2(8 * zoom, 4 * zoom),
			text,
			TextColor,
			Math.Max(10, 17 * zoom)
		);
	}

	private void DrawEvaluation(FunctionNode node, Vector2 position, float zoom)
	{
		if (node.Outputs.Count > 0 && node.EvaluationState == NodeEvaluationState.Success)
		{
			string preview = string.Join(
				"  ",
				node.Outputs.Select(output =>
					$"{output.Name}={NodeValueConverter.Format(output.Value, output.Type)}"
				)
			);

			pass.DrawText(
				font,
				position + new Vector2(14 * zoom, 9 * zoom),
				preview,
				new Color(160, 220, 170),
				Math.Max(9, 14 * zoom)
			);

			return;
		}

		if (!string.IsNullOrEmpty(node.EvaluationMessage)
			&& node.EvaluationState != NodeEvaluationState.Success)
		{
			pass.DrawText(
				font,
				position + new Vector2(14 * zoom, 9 * zoom),
				node.EvaluationMessage,
				new Color(255, 190, 190),
				Math.Max(9, 14 * zoom)
			);
		}
	}

	private void DrawPort(NodePort port, NodeCanvas canvas, bool hover)
	{
		Vector2 position = Screen(canvas, NodeGeometry.PortPosition(port));
		float zoom = canvas.Zoom;
		Color color = hover ? Color.White : PortColor(port.Type);

		pass.FillCircle(position, (hover ? 11 : NodeGeometry.PortRadius) * zoom, color, 20);

		float offset = port.Direction == NodePortDirection.Input ? 14 : -14;
		float textSize = Math.Max(10, 17 * zoom);
		float textWidth = Measure(font, port.Name, textSize).X;
		float textX = port.Direction == NodePortDirection.Input ? offset : offset - textWidth;
		Vector2 textPosition = position + new Vector2(textX, -8 * zoom);

		pass.DrawText(font, textPosition, port.Name, TextColor, textSize);

		if (hover)
		{
			pass.DrawText(
				font,
				position + new Vector2(12 * zoom, 12 * zoom),
				NodeValueConverter.TypeName(port.Type),
				Color.White,
				Math.Max(9, 14 * zoom)
			);
		}
	}

	private static Color BodyColor(FunctionNode node, bool selected)
	{
		if (node.EvaluationState == NodeEvaluationState.Error
			|| node.EvaluationState == NodeEvaluationState.Skipped)
		{
			return new Color(105, 53, 55);
		}

		return selected ? new Color(84, 87, 92) : NodeColor;
	}

	private static Vector2 Measure(GraphicsFont measuredFont, string text, float size)
	{
		return measuredFont.Measure(text, size);
	}
}
