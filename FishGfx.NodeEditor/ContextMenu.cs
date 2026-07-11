using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using FishGfx;
using FishGfx.NodeGraph;

namespace FishGfx.NodeEditor
{
	internal sealed class MenuCategory
	{
		internal string Name { get; }
		internal Color Color { get; }
		internal IReadOnlyList<NodeFunctionDescriptor> Functions { get; }

		internal MenuCategory(string name, IEnumerable<NodeFunctionDescriptor> functions)
		{
			Name = name;
			Color = ContextMenu.ColorFor(name);
			Functions = functions.OrderBy(f => f.MenuLabel, StringComparer.OrdinalIgnoreCase).ToArray();
		}
	}

	internal sealed class ContextMenu
	{
		internal const float Width = 520,
			Height = 420,
			SearchHeight = 52,
			CategoryWidth = 174,
			RowHeight = 38,
			Padding = 10;
		private readonly IReadOnlyList<NodeFunctionDescriptor> allEntries;
		private List<MenuCategory> categories = new List<MenuCategory>();
		private int categoryScroll,
			functionScroll;
		internal bool IsOpen { get; private set; }
		internal Vector2 Position { get; private set; }
		internal Vector2 InsertionWorld { get; private set; }
		internal string SearchText { get; private set; } = "";
		internal int SelectedCategory { get; private set; }
		internal int SelectedFunction { get; private set; }
		internal int HoverCategory { get; private set; } = -1;
		internal int HoverFunction { get; private set; } = -1;
		internal IReadOnlyList<MenuCategory> Categories => categories;
		internal MenuCategory CurrentCategory => categories.Count == 0 ? null : categories[SelectedCategory];
		internal IReadOnlyList<NodeFunctionDescriptor> CurrentFunctions =>
			CurrentCategory?.Functions ?? Array.Empty<NodeFunctionDescriptor>();
		internal int CategoryScroll => categoryScroll;
		internal int FunctionScroll => functionScroll;
		internal Bounds PanelBounds => new Bounds(Position.X, Position.Y, Width, Height);

		internal ContextMenu(IReadOnlyList<NodeFunctionDescriptor> entries)
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
			categoryScroll = functionScroll = 0;
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
			if (!IsOpen || string.IsNullOrEmpty(text))
				return;
			SearchText += text;
			Refilter();
		}

		internal void Backspace()
		{
			if (!IsOpen || SearchText.Length == 0)
				return;
			SearchText = SearchText.Substring(0, SearchText.Length - 1);
			Refilter();
		}

		internal bool Escape()
		{
			if (SearchText.Length > 0)
			{
				SearchText = "";
				Refilter();
				return false;
			}
			Close();
			return true;
		}

		internal void MoveCategory(int delta)
		{
			if (categories.Count == 0)
				return;
			SelectedCategory = (SelectedCategory + delta % categories.Count + categories.Count) % categories.Count;
			SelectedFunction = 0;
			functionScroll = 0;
			EnsureVisible();
		}

		internal void MoveFunction(int delta)
		{
			if (CurrentFunctions.Count == 0)
				return;
			SelectedFunction =
				(SelectedFunction + delta % CurrentFunctions.Count + CurrentFunctions.Count) % CurrentFunctions.Count;
			EnsureVisible();
		}

		internal NodeFunctionDescriptor Activate() =>
			CurrentFunctions.Count == 0 ? null : CurrentFunctions[SelectedFunction];

		internal NodeFunctionDescriptor Click(Vector2 screen)
		{
			int category = CategoryAt(screen);
			if (category >= 0)
			{
				SelectedCategory = category;
				SelectedFunction = 0;
				functionScroll = 0;
				EnsureVisible();
				return null;
			}
			int function = FunctionAt(screen);
			if (function >= 0)
			{
				if (function == SelectedFunction)
					return Activate();
				SelectedFunction = function;
				EnsureVisible();
				return null;
			}
			if (!PanelBounds.Contains(screen))
				Close();
			return null;
		}

		internal void UpdateHover(Vector2 screen)
		{
			HoverCategory = CategoryAt(screen);
			HoverFunction = FunctionAt(screen);
		}

		internal void Scroll(Vector2 screen, float delta)
		{
			if (!IsOpen || delta == 0)
				return;
			int direction = delta > 0 ? -1 : 1;
			if (screen.X < Position.X + CategoryWidth)
				categoryScroll = Math.Clamp(categoryScroll + direction, 0, Math.Max(0, categories.Count - VisibleRows));
			else
				functionScroll = Math.Clamp(
					functionScroll + direction,
					0,
					Math.Max(0, CurrentFunctions.Count - VisibleRows)
				);
		}

		internal int VisibleRows => Math.Max(1, (int)((Height - SearchHeight - Padding) / RowHeight));

		private int CategoryAt(Vector2 p) => RowAt(p, true, categories.Count, categoryScroll);

		private int FunctionAt(Vector2 p) => RowAt(p, false, CurrentFunctions.Count, functionScroll);

		private int RowAt(Vector2 p, bool category, int count, int scroll)
		{
			float left = category ? Position.X : Position.X + CategoryWidth;
			float right = category ? Position.X + CategoryWidth : Position.X + Width;
			float top = Position.Y + Height - SearchHeight;
			if (p.X < left || p.X >= right || p.Y < Position.Y + Padding || p.Y >= top)
				return -1;
			int visibleIndex = (int)((top - p.Y) / RowHeight);
			int index = visibleIndex + scroll;
			return index >= 0 && index < count ? index : -1;
		}

		private void Refilter()
		{
			string query = SearchText.Trim();
			IEnumerable<NodeFunctionDescriptor> matches = allEntries.Where(f => Matches(f, query));
			categories = matches
				.GroupBy(f => f.Group, StringComparer.OrdinalIgnoreCase)
				.OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
				.Select(g => new MenuCategory(g.Key, g))
				.ToList();
			SelectedCategory = Math.Clamp(SelectedCategory, 0, Math.Max(0, categories.Count - 1));
			SelectedFunction = 0;
			categoryScroll = functionScroll = 0;
			EnsureVisible();
		}

		private static bool Matches(NodeFunctionDescriptor f, string query)
		{
			if (query.Length == 0)
				return true;
			return f.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
				|| f.MenuLabel.Contains(query, StringComparison.OrdinalIgnoreCase)
				|| f.Method.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
				|| f.Group.Contains(query, StringComparison.OrdinalIgnoreCase)
				|| f.Parameters.Any(p =>
					NodeValueConverter.TypeName(p.Type).Contains(query, StringComparison.OrdinalIgnoreCase)
				);
		}

		private void EnsureVisible()
		{
			categoryScroll = KeepVisible(SelectedCategory, categoryScroll, categories.Count);
			functionScroll = KeepVisible(SelectedFunction, functionScroll, CurrentFunctions.Count);
		}

		private int KeepVisible(int selection, int scroll, int count)
		{
			int max = Math.Max(0, count - VisibleRows);
			if (selection < scroll)
				scroll = selection;
			if (selection >= scroll + VisibleRows)
				scroll = selection - VisibleRows + 1;
			return Math.Clamp(scroll, 0, max);
		}

		private void ClearHover()
		{
			HoverCategory = HoverFunction = -1;
		}

		internal static Color ColorFor(string category)
		{
			uint hash = 2166136261;
			foreach (char c in category ?? "")
				hash = (hash ^ c) * 16777619;
			return new Color((byte)(75 + hash % 145), (byte)(75 + (hash >> 8) % 145), (byte)(75 + (hash >> 16) % 145));
		}
	}
}
