using FishGfx.Graphics;
using System;

namespace FishGfx.SmokeTest {
	internal sealed class GalleryScene {
		public string Title { get; }
		public Action<float, Texture> Draw { get; }

		public GalleryScene(string title, Action<float, Texture> draw) {
			Title = title;
			Draw = draw;
		}
	}
}
