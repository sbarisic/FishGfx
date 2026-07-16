using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using FishGfx.NodeGraph;

namespace FishGfx.NodeEditor;

internal sealed class VisualMenuCategory
{
	internal string Name { get; }
	internal IReadOnlyList<VisualNodeDefinition> Nodes { get; }

	internal VisualMenuCategory(string name, IEnumerable<VisualNodeDefinition> nodes)
	{
		Name = name;
		Nodes = nodes.OrderBy(node => node.Title, StringComparer.OrdinalIgnoreCase).ToArray();
	}
}

internal sealed class VisualContextMenu
{
	internal const float Width = 520;
	internal const float Height = 440;
	internal const float SearchHeight = 52;
	internal const float CategoryWidth = 174;
	internal const float RowHeight = 38;
	internal const float Padding = 10;

	private readonly IReadOnlyList<VisualNodeDefinition> allEntries;
	private List<VisualMenuCategory> categories = new List<VisualMenuCategory>();
	private int categoryScroll;
	private int nodeScroll;

	internal bool IsOpen { get; private set; }
	internal Vector2 Position { get; private set; }
	internal Vector2 InsertionWorld { get; private set; }
	internal string SearchText { get; private set; } = "";
	internal int SelectedCategory { get; private set; }
	internal int SelectedNode { get; private set; }
	internal int HoverCategory { get; private set; } = -1;
	internal int HoverNode { get; private set; } = -1;
	internal IReadOnlyList<VisualMenuCategory> Categories => categories;
	internal VisualMenuCategory CurrentCategory => categories.Count == 0 ? null : categories[SelectedCategory];
	internal IReadOnlyList<VisualNodeDefinition> CurrentNodes => CurrentCategory?.Nodes ?? Array.Empty<VisualNodeDefinition>();
	internal int CategoryScroll => categoryScroll;
	internal int NodeScroll => nodeScroll;
	internal int VisibleRows => Math.Max(1, (int)((Height - SearchHeight - Padding) / RowHeight));
	internal Bounds PanelBounds => new Bounds(Position.X, Position.Y, Width, Height);

	internal VisualContextMenu(IReadOnlyList<VisualNodeDefinition> entries)
	{
		allEntries = entries ?? throw new ArgumentNullException(nameof(entries));
		Refilter();
	}

	internal void Open(Vector2 screen, Vector2 world, int windowWidth, int windowHeight)
	{
		Position = new Vector2(
			Math.Clamp(screen.X, Padding, Math.Max(Padding, windowWidth - Width - Padding)),
			Math.Clamp(screen.Y - Height, Padding, Math.Max(Padding, windowHeight - Height - Padding))
		);
		InsertionWorld = world;
		IsOpen = true;
		SearchText = "";
		categoryScroll = 0;
		nodeScroll = 0;
		Refilter();
		ClearHover();
	}

	internal void Close()
	{
		IsOpen = false;
		ClearHover();
	}

	internal void Append(string text)
	{
		if (IsOpen && !string.IsNullOrEmpty(text))
		{
			SearchText += text;
			Refilter();
		}
	}

	internal void Backspace()
	{
		if (IsOpen && SearchText.Length > 0)
		{
			SearchText = SearchText.Substring(0, SearchText.Length - 1);
			Refilter();
		}
	}

	internal void Escape()
	{
		if (SearchText.Length > 0)
		{
			SearchText = "";
			Refilter();
		}
		else
		{
			Close();
		}
	}

	internal void MoveCategory(int delta)
	{
		if (categories.Count == 0)
		{
			return;
		}

		SelectedCategory = Wrap(SelectedCategory + delta, categories.Count);
		SelectedNode = 0;
		nodeScroll = 0;
		EnsureVisible();
	}

	internal void MoveNode(int delta)
	{
		if (CurrentNodes.Count == 0)
		{
			return;
		}

		SelectedNode = Wrap(SelectedNode + delta, CurrentNodes.Count);
		EnsureVisible();
	}

	internal VisualNodeDefinition Activate()
	{
		return CurrentNodes.Count == 0 ? null : CurrentNodes[SelectedNode];
	}

	internal VisualNodeDefinition Click(Vector2 screen)
	{
		int category = CategoryAt(screen);

		if (category >= 0)
		{
			SelectedCategory = category;
			SelectedNode = 0;
			nodeScroll = 0;
			EnsureVisible();
			return null;
		}

		int node = NodeAt(screen);

		if (node >= 0)
		{
			if (node == SelectedNode)
			{
				return Activate();
			}

			SelectedNode = node;
			EnsureVisible();
			return null;
		}

		if (!PanelBounds.Contains(screen))
		{
			Close();
		}

		return null;
	}

	internal void UpdateHover(Vector2 screen)
	{
		HoverCategory = CategoryAt(screen);
		HoverNode = NodeAt(screen);
	}

	internal void Scroll(Vector2 screen, float delta)
	{
		if (!IsOpen || delta == 0)
		{
			return;
		}

		int direction = delta > 0 ? -1 : 1;

		if (screen.X < Position.X + CategoryWidth)
		{
			categoryScroll = Math.Clamp(categoryScroll + direction, 0, Math.Max(0, categories.Count - VisibleRows));
		}
		else
		{
			nodeScroll = Math.Clamp(nodeScroll + direction, 0, Math.Max(0, CurrentNodes.Count - VisibleRows));
		}
	}

	private int CategoryAt(Vector2 point)
	{
		return RowAt(point, true, categories.Count, categoryScroll);
	}

	private int NodeAt(Vector2 point)
	{
		return RowAt(point, false, CurrentNodes.Count, nodeScroll);
	}

	private int RowAt(Vector2 point, bool category, int count, int scroll)
	{
		float left = category ? Position.X : Position.X + CategoryWidth;
		float right = category ? Position.X + CategoryWidth : Position.X + Width;
		float top = Position.Y + Height - SearchHeight;

		if (point.X < left || point.X >= right || point.Y < Position.Y + Padding || point.Y >= top)
		{
			return -1;
		}

		int index = (int)((top - point.Y) / RowHeight) + scroll;

		return index >= 0 && index < count ? index : -1;
	}

	private void Refilter()
	{
		string query = SearchText.Trim();
		categories = allEntries
			.Where(node => Matches(node, query))
			.GroupBy(node => node.Category, StringComparer.OrdinalIgnoreCase)
			.OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
			.Select(group => new VisualMenuCategory(group.Key, group))
			.ToList();
		SelectedCategory = Math.Clamp(SelectedCategory, 0, Math.Max(0, categories.Count - 1));
		SelectedNode = 0;
		categoryScroll = 0;
		nodeScroll = 0;
		EnsureVisible();
	}

	private static bool Matches(VisualNodeDefinition node, string query)
	{
		return query.Length == 0
			|| node.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
			|| node.Category.Contains(query, StringComparison.OrdinalIgnoreCase)
			|| node.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
			|| node.Ports.Any(port => VisualValueTypes.DisplayName(port.Type).Contains(query, StringComparison.OrdinalIgnoreCase));
	}

	private void EnsureVisible()
	{
		categoryScroll = KeepVisible(SelectedCategory, categoryScroll, categories.Count);
		nodeScroll = KeepVisible(SelectedNode, nodeScroll, CurrentNodes.Count);
	}

	private int KeepVisible(int selection, int scroll, int count)
	{
		if (selection < scroll)
		{
			scroll = selection;
		}

		if (selection >= scroll + VisibleRows)
		{
			scroll = selection - VisibleRows + 1;
		}

		return Math.Clamp(scroll, 0, Math.Max(0, count - VisibleRows));
	}

	private static int Wrap(int value, int count)
	{
		return (value % count + count) % count;
	}

	private void ClearHover()
	{
		HoverCategory = -1;
		HoverNode = -1;
	}
}
