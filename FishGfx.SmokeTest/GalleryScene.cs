using System;
using FishGfx.Graphics;

namespace FishGfx.SmokeTest;

internal sealed class GalleryScene
{
	public string Title { get; }

	public Action<RenderPass, float, Texture> Draw { get; }

	public GalleryScene(string title, Action<RenderPass, float, Texture> draw)
	{
		Title = title;
		Draw = draw;
	}
}
