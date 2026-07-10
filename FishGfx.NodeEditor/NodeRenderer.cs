using FishGfx;
using FishGfx.Formats;
using FishGfx.Graphics;
using System;
using System.Numerics;

namespace FishGfx.NodeEditor {
	internal sealed class NodeRenderer {
		private static readonly Color CanvasColor = new Color(24, 25, 27);
		private static readonly Color GridColor = new Color(39, 41, 44);
		private static readonly Color NodeColor = new Color(63, 64, 66);
		private static readonly Color HeaderColor = new Color(72, 73, 76);
		private static readonly Color TextColor = new Color(224, 224, 226);
		private static readonly Color ScalarColor = new Color(43, 120, 225);
		private static readonly Color VectorColor = new Color(250, 207, 81);
		private readonly BMFont font;

		internal NodeRenderer(BMFont font) { this.font = font; }
		private static Color PortColor(PortType type) => type == PortType.Scalar ? ScalarColor : VectorColor;
		private Vector2 S(NodeCanvas canvas, Vector2 world) => canvas.WorldToScreen(world);

		internal void Draw(NodeGraph graph, NodeCanvas canvas, object selected, NodePort hoverPort, NodePort dragPort, Vector2 mouseWorld,
			ContextMenu menu, InlineValueEditor editor, int width, int height) {
			Gfx.Clear(CanvasColor);
			DrawGrid(canvas, width, height);
			foreach (NodeConnection connection in graph.Connections) DrawConnection(connection, canvas, selected == connection);
			if (dragPort != null) {
				Vector2 fixedPoint = S(canvas, NodeGeometry.PortPosition(dragPort));
				Vector2 cursor = S(canvas, mouseWorld);
				DrawBezier(dragPort.Direction == PortDirection.Output ? fixedPoint : cursor,
					dragPort.Direction == PortDirection.Output ? cursor : fixedPoint, PortColor(dragPort.Type), 5 * canvas.Zoom);
			}
			foreach (Node node in graph.Nodes) DrawNode(node, canvas, selected == node, hoverPort, editor);
			if (menu.IsOpen) DrawMenu(menu, canvas);
			Gfx.DrawText(font, new Vector2(22, height - 42), "NODE EDITOR", new Color(145, 151, 160), 23);
			Gfx.DrawText(font, new Vector2(22, 22), "Right click: add node   Middle drag: pan   Wheel: zoom   Delete: remove", new Color(115, 120, 128), 18);
		}

		private void DrawGrid(NodeCanvas canvas, int width, int height) {
			float spacing = 48 * canvas.Zoom;
			float startX = canvas.Pan.X % spacing, startY = canvas.Pan.Y % spacing;
			for (float x = startX; x < width; x += spacing) Gfx.Line(new Vertex2(new Vector2(x, 0), GridColor), new Vertex2(new Vector2(x, height), GridColor), 1);
			for (float y = startY; y < height; y += spacing) Gfx.Line(new Vertex2(new Vector2(0, y), GridColor), new Vertex2(new Vector2(width, y), GridColor), 1);
		}

		private void DrawConnection(NodeConnection connection, NodeCanvas canvas, bool selected) {
			DrawBezier(S(canvas, NodeGeometry.PortPosition(connection.Output)), S(canvas, NodeGeometry.PortPosition(connection.Input)),
				selected ? Color.White : PortColor(connection.Output.Type), (selected ? 8 : 6) * canvas.Zoom);
		}

		private static void DrawBezier(Vector2 start, Vector2 end, Color color, float thickness) {
			float reach = Math.Max(60, Math.Abs(end.X - start.X) * .45f);
			Gfx.CubicBezier(start, start + new Vector2(reach, 0), end - new Vector2(reach, 0), end, Math.Max(1, thickness), color, 28);
		}

		private void DrawNode(Node node, NodeCanvas canvas, bool selected, NodePort hoverPort, InlineValueEditor editor) {
			Vector2 p = S(canvas, node.Position); float z = canvas.Zoom, w = node.Width * z, h = node.Height * z;
			Gfx.FilledRoundedRectangle(p, new Vector2(w, h), new CornerRadii(7 * z), selected ? new Color(84, 87, 92) : NodeColor, 4);
			Gfx.FilledRoundedRectangle(new Vector2(p.X, p.Y + h - NodeGeometry.HeaderHeight * z), new Vector2(w, NodeGeometry.HeaderHeight * z), new CornerRadii(7 * z), HeaderColor, 4);
			if (selected) Gfx.RoundedRectangle(p, new Vector2(w, h), new CornerRadii(7 * z), Math.Max(2, 2 * z), new Color(118, 179, 255), 4);
			Gfx.DrawText(font, p + new Vector2(15 * z, h - 31 * z), node.Title, TextColor, Math.Max(11, 20 * z));
			Vector2 close = p + new Vector2(w - 24 * z, h - 21 * z);
			Color closeColor = new Color(190, 190, 192);
			Gfx.Line(new Vertex2(close + new Vector2(-7, -7) * z, closeColor), new Vertex2(close + new Vector2(7, 7) * z, closeColor), Math.Max(1, 2 * z));
			Gfx.Line(new Vertex2(close + new Vector2(-7, 7) * z, closeColor), new Vertex2(close + new Vector2(7, -7) * z, closeColor), Math.Max(1, 2 * z));

			foreach (NodePort port in node.Inputs) DrawPort(port, canvas, hoverPort == port);
			foreach (NodePort port in node.Outputs) DrawPort(port, canvas, hoverPort == port);
			for (int i = 0; i < node.Values.Count; i++) {
				NodeValue value = node.Values[i]; Bounds bounds = NodeGeometry.ValueBounds(node, i);
				Vector2 bp = S(canvas, new Vector2(bounds.X, bounds.Y));
				Gfx.DrawText(font, S(canvas, new Vector2(node.Position.X + 14, bounds.Y + 4)), value.Name, TextColor, Math.Max(10, 17 * z));
				Gfx.FilledRoundedRectangle(bp, new Vector2(bounds.Width * z, bounds.Height * z), new CornerRadii(3 * z), editor.Target == value ? new Color(43, 45, 49) : new Color(57, 58, 61), 2);
				string text = editor.Target == value ? editor.Text + "|" : value.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
				Gfx.DrawText(font, bp + new Vector2(8 * z, 4 * z), text, TextColor, Math.Max(10, 17 * z));
			}
		}

		private void DrawPort(NodePort port, NodeCanvas canvas, bool hover) {
			Vector2 p = S(canvas, NodeGeometry.PortPosition(port)); float z = canvas.Zoom;
			Gfx.FilledCircle(p, (hover ? 11 : NodeGeometry.PortRadius) * z, hover ? Color.White : PortColor(port.Type), 20);
			float x = port.Direction == PortDirection.Input ? 14 : -14;
			float approxWidth = port.Name.Length * 9 * z;
			Vector2 text = p + new Vector2(port.Direction == PortDirection.Input ? x : x - approxWidth, -8 * z);
			Gfx.DrawText(font, text, port.Name, TextColor, Math.Max(10, 17 * z));
		}

		private void DrawMenu(ContextMenu menu, NodeCanvas canvas) {
			Vector2 p = S(canvas, menu.Position); float z = canvas.Zoom, width = 220 * z, itemHeight = 34 * z;
			Gfx.FilledRoundedRectangle(p, new Vector2(width, NodeTemplates.Names.Length * itemHeight + 12 * z), new CornerRadii(6 * z), new Color(51, 52, 55), 3);
			for (int i = 0; i < NodeTemplates.Names.Length; i++) {
				if (i == menu.HoverIndex) Gfx.FilledRectangle(p.X + 6 * z, p.Y + 6 * z + i * itemHeight, width - 12 * z, itemHeight, new Color(73, 104, 146));
				Gfx.DrawText(font, p + new Vector2(14 * z, 12 * z + i * itemHeight), NodeTemplates.Names[i], TextColor, Math.Max(10, 17 * z));
			}
		}

		internal void Dispose() { foreach (Texture texture in font.PageNames.Values) texture.Dispose(); }
	}
}
