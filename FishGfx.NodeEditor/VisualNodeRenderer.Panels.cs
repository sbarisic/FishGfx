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
	private void DrawPanels(
		VisualEditorSession session,
		IReadOnlySet<Guid> selectedNodes,
		bool showSource,
		string fileStatus,
		bool fileStatusError,
		int width,
		int height
	)
	{
		DrawPanel(0, height - VisualEditorLayout.TopHeight, width, VisualEditorLayout.TopHeight);
		DrawPanel(0, VisualEditorLayout.BottomHeight, VisualEditorLayout.LeftWidth, height - VisualEditorLayout.TopHeight - VisualEditorLayout.BottomHeight);
		DrawPanel(width - VisualEditorLayout.RightWidth, VisualEditorLayout.BottomHeight, VisualEditorLayout.RightWidth, height - VisualEditorLayout.TopHeight - VisualEditorLayout.BottomHeight);
		DrawPanel(0, 0, width, VisualEditorLayout.BottomHeight);
		DrawToolbar(session, fileStatus, fileStatusError, height);
		DrawToolbox(session, height);
		DrawInspector(session, selectedNodes, width, height);
		DrawOutput(session, width);

		if (showSource)
		{
			DrawSource(session, width, height);
		}
	}

	private void DrawPanel(float x, float y, float width, float height)
	{
		pass.FillRectangle(x, y, width, height, PanelColor);
		pass.DrawRectangle(x, y, width, height, 1, PanelBorder);
	}

	private void DrawToolbar(
		VisualEditorSession session,
		string fileStatus,
		bool fileStatusError,
		int height
	)
	{
		pass.DrawText(interfaceFont, new Vector2(18, height - 43), "FISHGFX  VISUAL C#", TextColor, 21);
		DrawButton(VisualEditorLayout.RunButton(height), session.IsRunning ? "Running" : "Run  F5", new Color(50, 130, 83));
		DrawButton(VisualEditorLayout.StopButton(height), "Stop", new Color(147, 67, 68));
		DrawButton(VisualEditorLayout.CodeButton(height), "C# Preview  F6", new Color(59, 91, 137));

		float tabX = 570;

		foreach (VisualFunction function in session.Program.Functions)
		{
			Color color = function == session.CurrentFunction ? new Color(75, 85, 101) : new Color(45, 48, 54);
			pass.FillRoundedRectangle(tabX, height - 52, 130, 36, new CornerRadii(4), color, 3);
			pass.DrawText(interfaceFont, new Vector2(tabX + 12, height - 43), function.Name, TextColor, 16);
			tabX += 138;
		}

		if (!string.IsNullOrWhiteSpace(fileStatus))
		{
			pass.DrawText(
				interfaceFont,
				new Vector2(Math.Max(tabX + 8, 900), height - 41),
				fileStatus,
				fileStatusError ? new Color(235, 105, 110) : new Color(110, 205, 140),
				15
			);
		}
	}

	private void DrawButton(Bounds bounds, string label, Color color)
	{
		pass.FillRoundedRectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height, new CornerRadii(4), color, 3);
		pass.DrawText(interfaceFont, new Vector2(bounds.X + 11, bounds.Y + 9), label, Color.White, 15);
	}

	private void DrawToolbox(VisualEditorSession session, int height)
	{
		pass.DrawText(interfaceFont, new Vector2(16, height - VisualEditorLayout.TopHeight - 34), "TOOLBOX", MutedText, 17);
		IReadOnlyList<VisualNodeDefinition> definitions = VisualEditorLayout.ToolboxDefinitions(session.Catalog, height);

		for (int index = 0; index < definitions.Count; index++)
		{
			VisualNodeDefinition definition = definitions[index];
			Bounds row = VisualEditorLayout.ToolboxRow(index, height);

			pass.FillRoundedRectangle(row.X, row.Y, row.Width, row.Height, new CornerRadii(3), new Color(42, 45, 50), 2);
			pass.FillRoundedRectangle(row.X + 7, row.Y + 10, 9, 9, new CornerRadii(5), CategoryColor(definition.Category), 2);
			pass.DrawText(interfaceFont, new Vector2(row.X + 24, row.Y + 8), definition.Title, TextColor, 15);
		}

		DrawButton(VisualEditorLayout.AddFunctionButton(), "+ Function", new Color(62, 91, 128));
		DrawButton(
			VisualEditorLayout.AddParameterButton(),
			"+ Parameter",
			session.CurrentFunction.IsEntryPoint ? new Color(58, 60, 64) : new Color(83, 76, 126)
		);
		pass.DrawText(interfaceFont, new Vector2(16, VisualEditorLayout.BottomHeight + 57), "Right click for all nodes", MutedText, 13);
	}

	private void DrawInspector(
		VisualEditorSession session,
		IReadOnlySet<Guid> selectedNodes,
		int width,
		int height
	)
	{
		float x = width - VisualEditorLayout.RightWidth + 16;

		pass.DrawText(interfaceFont, new Vector2(x, height - VisualEditorLayout.TopHeight - 34), "INSPECTOR", MutedText, 17);

		if (selectedNodes.Count != 1)
		{
			pass.DrawText(interfaceFont, new Vector2(x, height - VisualEditorLayout.TopHeight - 72), selectedNodes.Count == 0 ? "Select a node" : $"{selectedNodes.Count} nodes selected", TextColor, 16);
			float symbolY = height - VisualEditorLayout.TopHeight - 112;

			foreach (VisualVariableSymbol symbol in session.CurrentFunction.Symbols.Take(10))
			{
				pass.DrawText(font, new Vector2(x, symbolY), $"{(symbol.IsParameter ? "parameter" : "variable")}  {symbol.Name}: {VisualValueTypes.DisplayName(symbol.Type)}", new Color(155, 181, 218), 14);
				symbolY -= 23;
			}

			return;
		}

		VisualNode node = session.CurrentFunction.Graph.Nodes.FirstOrDefault(candidate => selectedNodes.Contains(candidate.Id));

		if (node == null)
		{
			return;
		}

		float y = height - VisualEditorLayout.TopHeight - 72;
		pass.DrawText(interfaceFont, new Vector2(x, y), node.Title, TextColor, 20);
		y -= 30;
		pass.DrawText(font, new Vector2(x, y), node.DefinitionId, MutedText, 13);
		y -= 38;

		foreach (VisualPort port in node.Inputs.Concat(node.Outputs).Take(10))
		{
			string direction = port.Direction == VisualPortDirection.Input ? "in" : "out";
			string type = port.Kind == VisualPortKind.Execution ? "flow" : VisualValueTypes.DisplayName(port.Type);

			pass.DrawText(font, new Vector2(x, y), $"{direction}  {port.Label}: {type}", PortColor(port), 14);
			y -= 23;
		}

		IEnumerable<VisualProgramDiagnostic> nodeDiagnostics = session.Validation.Diagnostics.Where(diagnostic => diagnostic.NodeId == node.Id);

		foreach (VisualProgramDiagnostic diagnostic in nodeDiagnostics.Take(4))
		{
			y -= 12;
			pass.DrawText(font, new Vector2(x, y), diagnostic.Code + "  " + Trim(diagnostic.Message, 36), SeverityColor(diagnostic.Severity), 13);
			y -= 22;
		}
	}

	private void DrawOutput(VisualEditorSession session, int width)
	{
		pass.DrawText(interfaceFont, new Vector2(16, VisualEditorLayout.BottomHeight - 30), "DIAGNOSTICS / OUTPUT", MutedText, 16);
		float y = VisualEditorLayout.BottomHeight - 58;

		foreach (VisualProgramDiagnostic diagnostic in session.Validation.Diagnostics.Take(4))
		{
			pass.DrawText(font, new Vector2(18, y), $"{diagnostic.Code}  {Trim(diagnostic.Message, 88)}", SeverityColor(diagnostic.Severity), 14);
			y -= 22;
		}

		float outputX = Math.Max(620, width * .48f);
		pass.DrawText(interfaceFont, new Vector2(outputX, VisualEditorLayout.BottomHeight - 58), session.IsRunning ? "RUNNING" : "PROGRAM", session.IsRunning ? new Color(105, 205, 140) : MutedText, 15);
		y = VisualEditorLayout.BottomHeight - 82;

		foreach (string line in session.Output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).Take(5))
		{
			pass.DrawText(font, new Vector2(outputX, y), Trim(line, 90), TextColor, 14);
			y -= 22;
		}
	}

	private void DrawSource(VisualEditorSession session, int width, int height)
	{
		float panelWidth = 700;
		float x = width - panelWidth;
		float panelHeight = height - VisualEditorLayout.TopHeight - VisualEditorLayout.BottomHeight;

		pass.FillRectangle(x, VisualEditorLayout.BottomHeight, panelWidth, panelHeight, new Color(22, 24, 28, 250));
		pass.DrawRectangle(x, VisualEditorLayout.BottomHeight, panelWidth, panelHeight, 1, new Color(79, 91, 111));
		pass.DrawText(interfaceFont, new Vector2(x + 18, height - VisualEditorLayout.TopHeight - 30), "GENERATED C#  ·  READ ONLY", new Color(129, 174, 231), 16);

		float y = height - VisualEditorLayout.TopHeight - 50;
		string source = session.Generation.Success ? session.Generation.Source : "// Resolve diagnostics to generate C#.";
		int maxLines = Math.Max(1, (int)((panelHeight - 60) / 19));
		string[] lines = source.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

		for (int index = 0; index < Math.Min(maxLines, lines.Length); index++)
		{
			pass.DrawText(font, new Vector2(x + 12, y - index * 19), (index + 1).ToString().PadLeft(3) + "  " + lines[index], CodeColor(lines[index]), 13);
		}
	}

	private static Color SeverityColor(VisualDiagnosticSeverity severity)
	{
		return severity switch
		{
			VisualDiagnosticSeverity.Error => new Color(240, 105, 110),
			VisualDiagnosticSeverity.Warning => new Color(230, 184, 92),
			_ => new Color(115, 172, 226),
		};
	}

	private static Color CodeColor(string line)
	{
		string trimmed = line.TrimStart();

		return trimmed.StartsWith("//", StringComparison.Ordinal)
			? new Color(107, 153, 104)
			: trimmed.StartsWith("using ", StringComparison.Ordinal)
				|| trimmed.StartsWith("private ", StringComparison.Ordinal)
				|| trimmed.StartsWith("internal ", StringComparison.Ordinal)
				? new Color(120, 167, 226)
				: new Color(215, 218, 224);
	}

	private static string Trim(string value, int length)
	{
		return value != null && value.Length > length ? value.Substring(0, length - 1) + "…" : value ?? "";
	}

	private static Color CategoryColor(string category)
	{
		uint hash = 2166136261;

		foreach (char character in category ?? "")
		{
			hash = (hash ^ character) * 16777619;
		}

		return new Color((byte)(75 + hash % 145), (byte)(75 + (hash >> 8) % 145), (byte)(75 + (hash >> 16) % 145));
	}
}
