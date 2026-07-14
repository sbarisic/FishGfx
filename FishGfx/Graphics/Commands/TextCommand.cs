using System;
using System.Numerics;

namespace FishGfx.Graphics;

public sealed class DrawTextCommand : RenderCommand
{
	public DrawTextCommand(
		GraphicsFont font,
		Vector2 position,
		string text,
		Color color,
		float size,
		bool debugDraw = false
	) : this(font, position, text, color, size, 0, debugDraw)
	{
	}

	public DrawTextCommand(
		GraphicsFont font,
		Vector2 position,
		string text,
		Color color,
		float size,
		float characterSpacing,
		bool debugDraw = false
	)
	{
		Font = font ?? throw new ArgumentNullException(nameof(font));
		Text = text ?? throw new ArgumentNullException(nameof(text));
		Position = position;
		Color = color;
		Size = size;
		CharacterSpacing = characterSpacing;
		DebugDraw = debugDraw;
	}

	public GraphicsFont Font { get; }

	public Vector2 Position { get; }

	public string Text { get; }

	public Color Color { get; }

	public float Size { get; }

	public float CharacterSpacing { get; }

	public bool DebugDraw { get; }

	public override void Execute(RenderPass pass)
	{
		ArgumentNullException.ThrowIfNull(pass);
		pass.DrawText(
			Font,
			Position,
			Text,
			Color,
			Size,
			CharacterSpacing,
			DebugDraw
		);
	}
}
