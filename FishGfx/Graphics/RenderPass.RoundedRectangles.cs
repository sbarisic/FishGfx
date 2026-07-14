using System.Numerics;

namespace FishGfx.Graphics;

public sealed partial class RenderPass
{
	public void DrawRoundedRectangle(
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
		DrawRoundedRectangle(
			new Vector2(x, y),
			new Vector2(width, height),
			radii,
			thickness,
			color,
			cornerSegments
		);
	}

	public void DrawRoundedRectangle(
		Vector2 position,
		Vector2 size,
		CornerRadii radii,
		float thickness = 1,
		Color? color = null,
		int cornerSegments = 0
	)
	{
		EnsureActive();
		context.Renderer.DrawRoundedRectangle(
			this,
			position,
			size,
			radii,
			thickness,
			color ?? Color.White,
			cornerSegments
		);
	}

	public void FillRoundedRectangle(
		float x,
		float y,
		float width,
		float height,
		CornerRadii radii,
		Color? color = null,
		int cornerSegments = 0
	)
	{
		FillRoundedRectangle(
			new Vector2(x, y),
			new Vector2(width, height),
			radii,
			color,
			cornerSegments
		);
	}

	public void FillRoundedRectangle(
		Vector2 position,
		Vector2 size,
		CornerRadii radii,
		Color? color = null,
		int cornerSegments = 0
	)
	{
		EnsureActive();
		context.Renderer.FillRoundedRectangle(
			this,
			position,
			size,
			radii,
			color ?? Color.White,
			cornerSegments
		);
	}

	public void DrawTexturedRoundedRectangle(
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
		DrawTexturedRoundedRectangle(
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

	public void DrawTexturedRoundedRectangle(
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
		EnsureActive();
		context.Renderer.DrawTexturedRoundedRectangle(
			this,
			position,
			size,
			radii,
			texture,
			uvMinimum,
			uvMaximum,
			color ?? Color.White,
			shader,
			cornerSegments
		);
	}
}
