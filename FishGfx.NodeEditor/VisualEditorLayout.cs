using System;
using System.Collections.Generic;
using System.Linq;
using FishGfx.NodeGraph;

namespace FishGfx.NodeEditor;

internal static class VisualEditorLayout
{
	internal const float TopHeight = 66;
	internal const float LeftWidth = 250;
	internal const float RightWidth = 330;
	internal const float BottomHeight = 190;
	internal const float ToolboxRowHeight = 34;

	internal static Bounds RunButton(int height)
	{
		return new Bounds(260, height - 52, 76, 36);
	}

	internal static Bounds StopButton(int height)
	{
		return new Bounds(346, height - 52, 76, 36);
	}

	internal static Bounds CodeButton(int height)
	{
		return new Bounds(432, height - 52, 110, 36);
	}

	internal static bool IsCanvasPoint(System.Numerics.Vector2 point, int width, int height)
	{
		return point.X >= LeftWidth
			&& point.X <= width - RightWidth
			&& point.Y >= BottomHeight
			&& point.Y <= height - TopHeight;
	}

	internal static IReadOnlyList<VisualNodeDefinition> ToolboxDefinitions(
		VisualNodeCatalog catalog,
		int height
	)
	{
		int visible = Math.Max(1, (int)((height - TopHeight - BottomHeight - 155) / ToolboxRowHeight));

		return catalog.Definitions
			.OrderBy(definition => definition.Category, StringComparer.OrdinalIgnoreCase)
			.ThenBy(definition => definition.Title, StringComparer.OrdinalIgnoreCase)
			.Take(visible)
			.ToArray();
	}

	internal static Bounds AddFunctionButton()
	{
		return new Bounds(12, BottomHeight + 18, 108, 31);
	}

	internal static Bounds AddParameterButton()
	{
		return new Bounds(130, BottomHeight + 18, 108, 31);
	}

	internal static Bounds ToolboxRow(int index, int height)
	{
		return new Bounds(
			10,
			height - TopHeight - 58 - (index + 1) * ToolboxRowHeight,
			LeftWidth - 20,
			ToolboxRowHeight - 3
		);
	}

	internal static int SourceLineAt(System.Numerics.Vector2 point, int width, int height)
	{
		float left = width - 700;
		float top = height - TopHeight - 50;

		if (point.X < left || point.X > width - 8 || point.Y < BottomHeight || point.Y > top)
		{
			return -1;
		}

		return (int)((top - point.Y) / 19) + 1;
	}
}
