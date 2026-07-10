using FishGfx;
using FishGfx.Formats;
using FishGfx.Graphics;
using System.Numerics;

namespace FishGfx.SmokeTest {
	internal static class SceneMenu {
		internal static void Draw(BMFont font, GalleryScene[] scenes, int selectedIndex) {
			const float x = 30;
			const float firstItemY = 55;
			const float itemSpacing = 36;

			Gfx.DrawText(font, new Vector2(x, firstItemY), "SCENES", new Color(120, 190, 255), 30);
			for (int i = 0; i < scenes.Length; i++) {
				bool selected = i == selectedIndex;
				string prefix = selected ? "> " : "  ";
				Color color = selected ? new Color(255, 220, 110) : new Color(190, 200, 220);
				string label = $"{prefix}{i + 1,2}. {scenes[i].Title}";
				Gfx.DrawText(font, new Vector2(x, firstItemY + 46 + i * itemSpacing), label, color, 26);
			}

			float controlsY = firstItemY + 66 + scenes.Length * itemSpacing;
			Gfx.DrawText(font, new Vector2(x, controlsY), "SPACE       next", Color.White, 23);
			Gfx.DrawText(font, new Vector2(x, controlsY + 31), "BACKSPACE   previous", Color.White, 23);
			Gfx.DrawText(font, new Vector2(x, controlsY + 62), "ESC         quit", Color.White, 23);
		}
	}
}
