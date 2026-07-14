using System.Numerics;

namespace FishGfx.Graphics;

public sealed partial class RenderPass
{
	public void DrawPoint(Vertex2 point, float thickness = 1)
	{
		DrawPoint(new[] { point }, thickness);
	}

	public void DrawPoint(Vertex2[] points, float thickness = 1)
	{
		EnsureActive();
		context.Renderer.DrawPoint(this, points, thickness);
	}

	public void DrawPoint(Vertex3 point, float thickness = 1)
	{
		DrawPoint(new[] { point }, thickness);
	}

	public void DrawPoint(Vertex3[] points, float thickness = 1)
	{
		EnsureActive();
		context.Renderer.DrawPoint(this, points, thickness);
	}

	public void DrawLine(Vertex2 start, Vertex2 end, float thickness = 1)
	{
		EnsureActive();
		context.Renderer.DrawLine(this, start, end, thickness);
	}

	public void DrawLine(Vertex3 start, Vertex3 end, float thickness = 1)
	{
		EnsureActive();
		context.Renderer.DrawLine(this, start, end, thickness);
	}

	public void DrawLineStrip(Vertex2[] points, float thickness = 1)
	{
		EnsureActive();
		context.Renderer.DrawLineStrip(this, points, thickness);
	}

	public void DrawRectangle(
		float x,
		float y,
		float width,
		float height,
		float thickness = 1,
		Color? color = null
	)
	{
		EnsureActive();
		context.Renderer.DrawRectangle(
			this,
			x,
			y,
			width,
			height,
			thickness,
			color ?? Color.White
		);
	}

	public void FillRectangle(
		float x,
		float y,
		float width,
		float height,
		Color? color = null
	)
	{
		EnsureActive();
		context.Renderer.FillRectangle(
			this,
			x,
			y,
			width,
			height,
			color ?? Color.White
		);
	}

	public void DrawTexturedRectangle(
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
		EnsureActive();
		context.Renderer.DrawTexturedRectangle(
			this,
			x,
			y,
			width,
			height,
			u0,
			v0,
			u1,
			v1,
			color ?? Color.White,
			texture,
			shader
		);
	}

	public void DrawNinePatch(
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
		DrawNinePatch(
			new Vector2(x, y),
			new Vector2(width, height),
			texture,
			insets,
			color,
			shader
		);
	}

	public void DrawNinePatch(
		Vector2 position,
		Vector2 size,
		Texture texture,
		NinePatchInsets insets,
		Color? color = null,
		ShaderProgram shader = null
	)
	{
		EnsureActive();
		context.Renderer.DrawNinePatch(
			this,
			position,
			size,
			texture,
			insets,
			color ?? Color.White,
			shader
		);
	}
}
