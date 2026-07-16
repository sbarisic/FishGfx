using System.Numerics;
using FishGfx;
using FishGfx.Graphics;

namespace FishGfx.NodeEditor;

internal sealed partial class VisualNodeRenderer
{
	private void DrawMenu(VisualContextMenu menu)
	{
		Vector2 position = menu.Position;

		pass.FillRoundedRectangle(position, new Vector2(VisualContextMenu.Width, VisualContextMenu.Height), new CornerRadii(9), new Color(37, 40, 45, 252), 4);
		pass.DrawRoundedRectangle(position, new Vector2(VisualContextMenu.Width, VisualContextMenu.Height), new CornerRadii(9), 1, new Color(85, 91, 101), 4);
		DrawMenuSearch(menu);
		DrawMenuCategories(menu);
		DrawMenuNodes(menu);
	}

	private void DrawMenuSearch(VisualContextMenu menu)
	{
		float y = menu.Position.Y + VisualContextMenu.Height - 42;
		string text = menu.SearchText.Length == 0 ? "Search C# nodes..." : menu.SearchText + "|";

		pass.FillRoundedRectangle(menu.Position.X + 12, y, VisualContextMenu.Width - 24, 30, new CornerRadii(4), new Color(24, 27, 31), 3);
		pass.DrawText(interfaceFont, new Vector2(menu.Position.X + 22, y + 7), text, menu.SearchText.Length == 0 ? MutedText : TextColor, 16);
	}

	private void DrawMenuCategories(VisualContextMenu menu)
	{
		float top = menu.Position.Y + VisualContextMenu.Height - VisualContextMenu.SearchHeight;

		for (int visible = 0; visible < menu.VisibleRows; visible++)
		{
			int index = menu.CategoryScroll + visible;

			if (index >= menu.Categories.Count)
			{
				break;
			}

			float y = top - (visible + 1) * VisualContextMenu.RowHeight;
			Color background = index == menu.SelectedCategory
				? new Color(65, 70, 78)
				: index == menu.HoverCategory ? new Color(50, 54, 61) : new Color(0, 0, 0, 0);

			if (background.A > 0)
			{
				pass.FillRoundedRectangle(menu.Position.X + 7, y + 3, VisualContextMenu.CategoryWidth - 14, VisualContextMenu.RowHeight - 6, new CornerRadii(4), background, 2);
			}

			pass.FillCircle(new Vector2(menu.Position.X + 19, y + 18), 5, CategoryColor(menu.Categories[index].Name), 16);
			pass.DrawText(interfaceFont, new Vector2(menu.Position.X + 32, y + 9), menu.Categories[index].Name, TextColor, 16);
		}
	}

	private void DrawMenuNodes(VisualContextMenu menu)
	{
		float top = menu.Position.Y + VisualContextMenu.Height - VisualContextMenu.SearchHeight;
		float x = menu.Position.X + VisualContextMenu.CategoryWidth;

		for (int visible = 0; visible < menu.VisibleRows; visible++)
		{
			int index = menu.NodeScroll + visible;

			if (index >= menu.CurrentNodes.Count)
			{
				break;
			}

			float y = top - (visible + 1) * VisualContextMenu.RowHeight;
			Color background = index == menu.SelectedNode
				? CategoryColor(menu.CurrentCategory.Name)
				: index == menu.HoverNode ? new Color(50, 54, 61) : new Color(0, 0, 0, 0);

			if (background.A > 0)
			{
				pass.FillRoundedRectangle(x + 7, y + 3, VisualContextMenu.Width - VisualContextMenu.CategoryWidth - 14, VisualContextMenu.RowHeight - 6, new CornerRadii(4), background, 2);
			}

			pass.DrawText(interfaceFont, new Vector2(x + 16, y + 9), menu.CurrentNodes[index].Title, TextColor, 16);
		}
	}
}
