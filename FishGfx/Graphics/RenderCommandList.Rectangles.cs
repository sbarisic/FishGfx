using System.Numerics;

namespace FishGfx.Graphics;

public sealed partial class RenderCommandList
{
	public RectangleCommand RecordDrawRectangle(
		float x,
		float y,
		float width,
		float height,
		float thickness = 1,
		Color? color = null
	)
	{
		return Add(new RectangleCommand(x, y, width, height, thickness, color));
	}

	public RectangleCommand RecordDrawRectangle(
		Vector2 position,
		Vector2 size,
		float thickness = 1,
		Color? color = null
	)
	{
		return RecordDrawRectangle(
			position.X,
			position.Y,
			size.X,
			size.Y,
			thickness,
			color
		);
	}

	public FillRectangleCommand RecordFillRectangle(
		float x,
		float y,
		float width,
		float height,
		Color? color = null
	)
	{
		return Add(new FillRectangleCommand(x, y, width, height, color));
	}

	public FillRectangleCommand RecordFillRectangle(
		Vector2 position,
		Vector2 size,
		Color? color = null
	)
	{
		return RecordFillRectangle(position.X, position.Y, size.X, size.Y, color);
	}

	public TexturedRectangleCommand RecordDrawTexturedRectangle(
		float x,
		float y,
		float width,
		float height,
		float u0 = 0,
		float v0 = 0,
		float u1 = 1,
		float v1 = 1,
		Color? color = null,
		Texture texture = null,
		ShaderProgram shader = null
	)
	{
		return Add(
			new TexturedRectangleCommand(
				x,
				y,
				width,
				height,
				u0,
				v0,
				u1,
				v1,
				color,
				texture,
				shader
			)
		);
	}

	public TexturedRectangleCommand RecordDrawTexturedRectangle(
		Vector2 position,
		Vector2 size,
		Vector2 uvMinimum,
		Vector2 uvMaximum,
		Color? color = null,
		Texture texture = null,
		ShaderProgram shader = null
	)
	{
		return RecordDrawTexturedRectangle(
			position.X,
			position.Y,
			size.X,
			size.Y,
			uvMinimum.X,
			uvMinimum.Y,
			uvMaximum.X,
			uvMaximum.Y,
			color,
			texture,
			shader
		);
	}

	public NinePatchCommand RecordDrawNinePatch(
		Vector2 position,
		Vector2 size,
		Texture texture,
		NinePatchInsets insets,
		Color? color = null,
		ShaderProgram shader = null
	)
	{
		return Add(new NinePatchCommand(position, size, texture, insets, color, shader));
	}

	public NinePatchCommand RecordDrawNinePatch(
		float x,
		float y,
		float width,
		float height,
		Texture texture,
		NinePatchInsets insets,
		Color? color = null,
		ShaderProgram shader = null
	)
	{
		return RecordDrawNinePatch(
			new Vector2(x, y),
			new Vector2(width, height),
			texture,
			insets,
			color,
			shader
		);
	}

	public RoundedRectangleCommand RecordDrawRoundedRectangle(
		Vector2 position,
		Vector2 size,
		CornerRadii radii,
		float thickness = 1,
		Color? color = null,
		int cornerSegments = 0
	)
	{
		return Add(
			new RoundedRectangleCommand(
				position,
				size,
				radii,
				thickness,
				color,
				cornerSegments
			)
		);
	}

	public RoundedRectangleCommand RecordDrawRoundedRectangle(
		float x,
		float y,
		float width,
		float height,
		CornerRadii radii,
		float thickness = 1,
		Color? color = null,
		int cornerSegments = 0
	)
	{
		return RecordDrawRoundedRectangle(
			new Vector2(x, y),
			new Vector2(width, height),
			radii,
			thickness,
			color,
			cornerSegments
		);
	}

	public FillRoundedRectangleCommand RecordFillRoundedRectangle(
		Vector2 position,
		Vector2 size,
		CornerRadii radii,
		Color? color = null,
		int cornerSegments = 0
	)
	{
		return Add(
			new FillRoundedRectangleCommand(
				position,
				size,
				radii,
				color,
				cornerSegments
			)
		);
	}

	public FillRoundedRectangleCommand RecordFillRoundedRectangle(
		float x,
		float y,
		float width,
		float height,
		CornerRadii radii,
		Color? color = null,
		int cornerSegments = 0
	)
	{
		return RecordFillRoundedRectangle(
			new Vector2(x, y),
			new Vector2(width, height),
			radii,
			color,
			cornerSegments
		);
	}

	public TexturedRoundedRectangleCommand RecordDrawTexturedRoundedRectangle(
		Vector2 position,
		Vector2 size,
		CornerRadii radii,
		Texture texture,
		Vector2 uvMinimum,
		Vector2 uvMaximum,
		Color? color = null,
		ShaderProgram shader = null,
		int cornerSegments = 0
	)
	{
		return Add(
			new TexturedRoundedRectangleCommand(
				position,
				size,
				radii,
				texture,
				uvMinimum,
				uvMaximum,
				color,
				shader,
				cornerSegments
			)
		);
	}

	public TexturedRoundedRectangleCommand RecordDrawTexturedRoundedRectangle(
		float x,
		float y,
		float width,
		float height,
		CornerRadii radii,
		Texture texture,
		float u0 = 0,
		float v0 = 0,
		float u1 = 1,
		float v1 = 1,
		Color? color = null,
		ShaderProgram shader = null,
		int cornerSegments = 0
	)
	{
		return RecordDrawTexturedRoundedRectangle(
			new Vector2(x, y),
			new Vector2(width, height),
			radii,
			texture,
			new Vector2(u0, v0),
			new Vector2(u1, v1),
			color,
			shader,
			cornerSegments
		);
	}
}
