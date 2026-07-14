using System.Numerics;

namespace FishGfx.Graphics;

public sealed partial class RenderPass
{
	public Vector2 DrawText(
		GraphicsFont font,
		Vector2 position,
		string text,
		Color color,
		float size,
		bool debugDraw = false
	)
	{
		return DrawText(font, position, text, color, size, 0, debugDraw);
	}

	public Vector2 DrawText(
		GraphicsFont font,
		Vector2 position,
		string text,
		Color color,
		float size,
		float characterSpacing,
		bool debugDraw = false
	)
	{
		EnsureActive();

		return context.Renderer.DrawText(
			this,
			font,
			position,
			text,
			color,
			size,
			characterSpacing,
			debugDraw
		);
	}
}
