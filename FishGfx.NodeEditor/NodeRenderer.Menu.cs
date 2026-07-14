using System.Numerics;
using FishGfx;
using FishGfx.Graphics;
using FishGfx.NodeGraph;

namespace FishGfx.NodeEditor;

internal sealed partial class NodeRenderer
{
	private void DrawMenu(ContextMenu menu)
	{
		Vector2 position = menu.Position;

		DrawMenuPanel(position);
		DrawMenuSearch(menu, position);
		DrawMenuCategories(menu, position);
		DrawMenuFunctions(menu, position);
	}

	private void DrawMenuPanel(Vector2 position)
	{
		pass.FillRoundedRectangle(
			position,
			new Vector2(ContextMenu.Width, ContextMenu.Height),
			new CornerRadii(10),
			new Color(38, 40, 44, 250),
			5
		);
		pass.DrawRoundedRectangle(
			position,
			new Vector2(ContextMenu.Width, ContextMenu.Height),
			new CornerRadii(10),
			1,
			new Color(86, 89, 96),
			5
		);
		pass.FillRectangle(
			position.X + ContextMenu.CategoryWidth - 1,
			position.Y + 10,
			1,
			ContextMenu.Height - ContextMenu.SearchHeight - 10,
			new Color(73, 76, 82)
		);
	}

	private void DrawMenuSearch(ContextMenu menu, Vector2 position)
	{
		float searchY = position.Y + ContextMenu.Height - 43;
		string searchText = menu.SearchText.Length == 0
			? "Search functions..."
			: menu.SearchText + "|";
		Color searchColor = menu.SearchText.Length == 0
			? new Color(120, 124, 132)
			: TextColor;

		pass.FillRoundedRectangle(
			position.X + 12,
			searchY,
			ContextMenu.Width - 24,
			31,
			new CornerRadii(5),
			new Color(28, 30, 34),
			3
		);
		pass.DrawText(
			menuFont,
			new Vector2(position.X + 23, searchY + 7),
			searchText,
			searchColor,
			17
		);
	}

	private void DrawMenuCategories(ContextMenu menu, Vector2 position)
	{
		float top = position.Y + ContextMenu.Height - ContextMenu.SearchHeight;

		for (int visibleIndex = 0; visibleIndex < menu.VisibleRows; visibleIndex++)
		{
			int categoryIndex = menu.CategoryScroll + visibleIndex;

			if (categoryIndex >= menu.Categories.Count)
			{
				break;
			}

			MenuCategory category = menu.Categories[categoryIndex];
			float y = top - (visibleIndex + 1) * ContextMenu.RowHeight;

			DrawCategoryBackground(menu, position.X, y, categoryIndex);
			pass.FillRoundedRectangle(
				position.X + 14,
				y + 12,
				10,
				10,
				new CornerRadii(5),
				category.Color,
				3
			);
			pass.DrawText(
				menuFont,
				new Vector2(position.X + 33, y + 9),
				category.Name,
				categoryIndex == menu.SelectedCategory ? Color.White : new Color(190, 193, 199),
				17
			);
		}
	}

	private void DrawCategoryBackground(ContextMenu menu, float x, float y, int categoryIndex)
	{
		Color color;

		if (categoryIndex == menu.SelectedCategory)
		{
			color = new Color(64, 67, 73);
		}
		else if (categoryIndex == menu.HoverCategory)
		{
			color = new Color(52, 55, 60);
		}
		else
		{
			return;
		}

		pass.FillRoundedRectangle(
			x + 7,
			y + 3,
			ContextMenu.CategoryWidth - 14,
			ContextMenu.RowHeight - 6,
			new CornerRadii(4),
			color,
			2
		);
	}

	private void DrawMenuFunctions(ContextMenu menu, Vector2 position)
	{
		float top = position.Y + ContextMenu.Height - ContextMenu.SearchHeight;
		float x = position.X + ContextMenu.CategoryWidth;

		for (int visibleIndex = 0; visibleIndex < menu.VisibleRows; visibleIndex++)
		{
			int functionIndex = menu.FunctionScroll + visibleIndex;

			if (functionIndex >= menu.CurrentFunctions.Count)
			{
				break;
			}

			NodeFunctionDescriptor function = menu.CurrentFunctions[functionIndex];
			float y = top - (visibleIndex + 1) * ContextMenu.RowHeight;

			DrawFunctionBackground(menu, x, y, functionIndex);
			pass.DrawText(
				menuFont,
				new Vector2(x + 16, y + 9),
				function.MenuLabel,
				functionIndex == menu.SelectedFunction ? Color.White : new Color(215, 217, 221),
				17
			);
		}

		if (menu.Categories.Count == 0)
		{
			pass.DrawText(
				menuFont,
				new Vector2(position.X + ContextMenu.CategoryWidth + 18, top - 34),
				"No matching functions",
				new Color(145, 149, 157),
				17
			);
		}
	}

	private void DrawFunctionBackground(ContextMenu menu, float x, float y, int functionIndex)
	{
		Color color;

		if (functionIndex == menu.SelectedFunction)
		{
			color = menu.CurrentCategory.Color;
		}
		else if (functionIndex == menu.HoverFunction)
		{
			color = new Color(52, 55, 60);
		}
		else
		{
			return;
		}

		pass.FillRoundedRectangle(
			x + 7,
			y + 3,
			ContextMenu.Width - ContextMenu.CategoryWidth - 14,
			ContextMenu.RowHeight - 6,
			new CornerRadii(4),
			color,
			2
		);
	}
}
