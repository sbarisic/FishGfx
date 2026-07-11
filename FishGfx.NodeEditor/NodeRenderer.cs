using FishGfx;
using FishGfx.Formats;
using FishGfx.Graphics;
using System;
using System.Numerics;
using FishGfx.NodeGraph;
using System.Linq;

namespace FishGfx.NodeEditor {
	internal sealed class NodeRenderer {
		private static readonly Color CanvasColor = new Color(24, 25, 27);
		private static readonly Color GridColor = new Color(39, 41, 44);
		private static readonly Color NodeColor = new Color(63, 64, 66);
		private static readonly Color HeaderColor = new Color(72, 73, 76);
		private static readonly Color TextColor = new Color(224, 224, 226);
		private readonly BMFont font;
		private readonly BMFont menuFont;

		internal NodeRenderer(BMFont font, BMFont menuFont) { this.font = font; this.menuFont = menuFont; }
		private static Color PortColor(Type type) {
			uint hash = 2166136261;
			foreach (char c in type.FullName ?? type.Name) hash = (hash ^ c) * 16777619;
			return new Color((byte)(70 + hash % 150), (byte)(70 + (hash >> 8) % 150), (byte)(70 + (hash >> 16) % 150));
		}
		private Vector2 S(NodeCanvas canvas, Vector2 world) => canvas.WorldToScreen(world);

		internal void Draw(FunctionNodeGraph graph, NodeCanvas canvas, object selected, NodePort hoverPort, NodePort dragPort, Vector2 mouseWorld,
			ContextMenu menu, InlineValueEditor editor, NodeEvaluationResult result, string fileStatus, bool fileStatusError, int width, int height) {
			Gfx.Clear(CanvasColor);
			DrawGrid(canvas, width, height);
			foreach (NodeConnection connection in graph.Connections) DrawConnection(connection, canvas, selected == connection);
			if (dragPort != null) {
				Vector2 fixedPoint = S(canvas, NodeGeometry.PortPosition(dragPort));
				Vector2 cursor = S(canvas, mouseWorld);
				DrawBezier(dragPort.Direction == NodePortDirection.Output ? fixedPoint : cursor,
					dragPort.Direction == NodePortDirection.Output ? cursor : fixedPoint, PortColor(dragPort.Type), 5 * canvas.Zoom);
			}
			foreach (FunctionNode node in graph.Nodes) DrawNode(node, canvas, selected == node, hoverPort, editor);
			Gfx.DrawText(font, new Vector2(22, height - 42), "NODE EDITOR", new Color(145, 151, 160), 23);
			Color evaluateColor = result == null || result.Success ? new Color(55, 132, 86) : new Color(172, 68, 68);
			Gfx.FilledRoundedRectangle(220, height - 58, 132, 38, new CornerRadii(5), evaluateColor, 3);
			Gfx.DrawText(font, new Vector2(240, height - 49), "Evaluate  F5", Color.White, 17);
			if (result != null) Gfx.DrawText(font, new Vector2(370, height - 48), result.Summary, evaluateColor, 18);
			if (!string.IsNullOrEmpty(fileStatus)) Gfx.DrawText(font, new Vector2(700, height - 48), fileStatus, fileStatusError ? new Color(235, 110, 110) : new Color(115, 205, 145), 18);
			Gfx.DrawText(font, new Vector2(22, 22), "Right click: add node   Middle drag: pan   Wheel: zoom   Delete: remove", new Color(115, 120, 128), 18);
			if (menu.IsOpen) DrawMenu(menu);
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

		private void DrawNode(FunctionNode node, NodeCanvas canvas, bool selected, NodePort hoverPort, InlineValueEditor editor) {
			Vector2 p = S(canvas, node.Position); float z = canvas.Zoom, w = node.Width * z, h = NodeGeometry.HeightOf(node) * z;
			Color bodyColor = node.EvaluationState == NodeEvaluationState.Error || node.EvaluationState == NodeEvaluationState.Skipped ? new Color(105, 53, 55) : selected ? new Color(84, 87, 92) : NodeColor;
			Gfx.FilledRoundedRectangle(p, new Vector2(w, h), new CornerRadii(7 * z), bodyColor, 4);
			Gfx.FilledRoundedRectangle(new Vector2(p.X, p.Y + h - NodeGeometry.HeaderHeight * z), new Vector2(w, NodeGeometry.HeaderHeight * z), new CornerRadii(7 * z), HeaderColor, 4);
			if (selected) Gfx.RoundedRectangle(p, new Vector2(w, h), new CornerRadii(7 * z), Math.Max(2, 2 * z), new Color(118, 179, 255), 4);
			Gfx.DrawText(font, p + new Vector2(15 * z, h - 31 * z), node.Title, TextColor, Math.Max(11, 20 * z));
			Vector2 close = p + new Vector2(w - 24 * z, h - 21 * z);
			Color closeColor = new Color(190, 190, 192);
			Gfx.Line(new Vertex2(close + new Vector2(-7, -7) * z, closeColor), new Vertex2(close + new Vector2(7, 7) * z, closeColor), Math.Max(1, 2 * z));
			Gfx.Line(new Vertex2(close + new Vector2(-7, 7) * z, closeColor), new Vertex2(close + new Vector2(7, -7) * z, closeColor), Math.Max(1, 2 * z));

			foreach (NodePort port in node.Inputs) DrawPort(port, canvas, hoverPort == port);
			foreach (NodePort port in node.Outputs) DrawPort(port, canvas, hoverPort == port);
			for (int i = 0; i < node.BodyValues.Count; i++) {
				NodeBodyValue value = node.BodyValues[i]; Bounds bounds = NodeGeometry.ValueBounds(node, i);
				Vector2 bp = S(canvas, new Vector2(bounds.X, bounds.Y));
				Gfx.DrawText(font, S(canvas, new Vector2(node.Position.X + 14, bounds.Y + 4)), value.Name, TextColor, Math.Max(10, 17 * z));
				Gfx.FilledRoundedRectangle(bp, new Vector2(bounds.Width * z, bounds.Height * z), new CornerRadii(3 * z), editor.Target == value ? new Color(43, 45, 49) : new Color(57, 58, 61), 2);
				string text = editor.Target == value ? editor.Text + "|" : value.Text;
				Gfx.DrawText(font, bp + new Vector2(8 * z, 4 * z), text, TextColor, Math.Max(10, 17 * z));
			}
			if (node.Outputs.Count > 0 && node.EvaluationState == NodeEvaluationState.Success) {
				string preview = string.Join("  ", node.Outputs.Select(o => $"{o.Name}={NodeValueConverter.Format(o.Value, o.Type)}"));
				Gfx.DrawText(font, p + new Vector2(14 * z, 9 * z), preview, new Color(160, 220, 170), Math.Max(9, 14 * z));
			} else if (!string.IsNullOrEmpty(node.EvaluationMessage) && node.EvaluationState != NodeEvaluationState.Success)
				Gfx.DrawText(font, p + new Vector2(14 * z, 9 * z), node.EvaluationMessage, new Color(255, 190, 190), Math.Max(9, 14 * z));
		}

		private void DrawPort(NodePort port, NodeCanvas canvas, bool hover) {
			Vector2 p = S(canvas, NodeGeometry.PortPosition(port)); float z = canvas.Zoom;
			Gfx.FilledCircle(p, (hover ? 11 : NodeGeometry.PortRadius) * z, hover ? Color.White : PortColor(port.Type), 20);
			float x = port.Direction == NodePortDirection.Input ? 14 : -14;
			float approxWidth = port.Name.Length * 9 * z;
			Vector2 text = p + new Vector2(port.Direction == NodePortDirection.Input ? x : x - approxWidth, -8 * z);
			Gfx.DrawText(font, text, port.Name, TextColor, Math.Max(10, 17 * z));
			if (hover) Gfx.DrawText(font, p + new Vector2(12 * z, 12 * z), NodeValueConverter.TypeName(port.Type), Color.White, Math.Max(9, 14 * z));
		}

		private void DrawMenu(ContextMenu menu) {
			Vector2 p = menu.Position;
			Gfx.FilledRoundedRectangle(p, new Vector2(ContextMenu.Width, ContextMenu.Height), new CornerRadii(10), new Color(38, 40, 44, 250), 5);
			Gfx.RoundedRectangle(p, new Vector2(ContextMenu.Width, ContextMenu.Height), new CornerRadii(10), 1, new Color(86, 89, 96), 5);
			float searchY = p.Y + ContextMenu.Height - 43;
			Gfx.FilledRoundedRectangle(p.X + 12, searchY, ContextMenu.Width - 24, 31, new CornerRadii(5), new Color(28, 30, 34), 3);
			Gfx.DrawText(menuFont, new Vector2(p.X + 23, searchY + 7), menu.SearchText.Length == 0 ? "Search functions..." : menu.SearchText + "|", menu.SearchText.Length == 0 ? new Color(120, 124, 132) : TextColor, 17);
			Gfx.FilledRectangle(p.X + ContextMenu.CategoryWidth - 1, p.Y + 10, 1, ContextMenu.Height - ContextMenu.SearchHeight - 10, new Color(73, 76, 82));

			float top = p.Y + ContextMenu.Height - ContextMenu.SearchHeight;
			for (int visible = 0; visible < menu.VisibleRows; visible++) {
				int index = menu.CategoryScroll + visible; if (index >= menu.Categories.Count) break;
				MenuCategory category = menu.Categories[index]; float y = top - (visible + 1) * ContextMenu.RowHeight;
				if (index == menu.SelectedCategory) Gfx.FilledRoundedRectangle(p.X + 7, y + 3, ContextMenu.CategoryWidth - 14, ContextMenu.RowHeight - 6, new CornerRadii(4), new Color(64, 67, 73), 2);
				else if (index == menu.HoverCategory) Gfx.FilledRoundedRectangle(p.X + 7, y + 3, ContextMenu.CategoryWidth - 14, ContextMenu.RowHeight - 6, new CornerRadii(4), new Color(52, 55, 60), 2);
				Gfx.FilledRoundedRectangle(p.X + 14, y + 12, 10, 10, new CornerRadii(5), category.Color, 3);
				Gfx.DrawText(menuFont, new Vector2(p.X + 33, y + 9), category.Name, index == menu.SelectedCategory ? Color.White : new Color(190, 193, 199), 17);
			}

			for (int visible = 0; visible < menu.VisibleRows; visible++) {
				int index = menu.FunctionScroll + visible; if (index >= menu.CurrentFunctions.Count) break;
				NodeFunctionDescriptor function = menu.CurrentFunctions[index]; float x = p.X + ContextMenu.CategoryWidth, y = top - (visible + 1) * ContextMenu.RowHeight;
				if (index == menu.SelectedFunction) Gfx.FilledRoundedRectangle(x + 7, y + 3, ContextMenu.Width - ContextMenu.CategoryWidth - 14, ContextMenu.RowHeight - 6, new CornerRadii(4), menu.CurrentCategory.Color, 2);
				else if (index == menu.HoverFunction) Gfx.FilledRoundedRectangle(x + 7, y + 3, ContextMenu.Width - ContextMenu.CategoryWidth - 14, ContextMenu.RowHeight - 6, new CornerRadii(4), new Color(52, 55, 60), 2);
				Gfx.DrawText(menuFont, new Vector2(x + 16, y + 9), function.MenuLabel, index == menu.SelectedFunction ? Color.White : new Color(215, 217, 221), 17);
			}
			if (menu.Categories.Count == 0) Gfx.DrawText(menuFont, new Vector2(p.X + ContextMenu.CategoryWidth + 18, top - 34), "No matching functions", new Color(145, 149, 157), 17);
		}

		internal void Dispose() { foreach (Texture texture in font.PageNames.Values) texture.Dispose(); foreach (Texture texture in menuFont.PageNames.Values) texture.Dispose(); }
	}
}
