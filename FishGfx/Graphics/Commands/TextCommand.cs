using System;
using System.Numerics;

namespace FishGfx.Graphics
{
	public sealed class DrawTextCommand : GraphicsCommand
	{
		public DrawTextCommand(
			GfxFont font,
			Vector2 position,
			string text,
			Color color,
			float fontSize = -1,
			bool debugDraw = false
		)
		{
			Font = font ?? throw new ArgumentNullException(nameof(font));
			Position = position;
			Text = text;
			Color = color;
			FontSize = fontSize;
			DebugDraw = debugDraw;
		}

		public GfxFont Font { get; }
		public Vector2 Position { get; }
		public string Text { get; }
		public Color Color { get; }
		public float FontSize { get; }
		public bool DebugDraw { get; }

		public override void Execute()
		{
			Gfx.DrawText(Font, Position, Text, Color, FontSize, DebugDraw);
		}
	}
}
