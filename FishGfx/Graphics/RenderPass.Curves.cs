using System;
using System.Numerics;

namespace FishGfx.Graphics;

public sealed partial class RenderPass
{
	public void DrawCircle(
		Vector2 center,
		float radius,
		float thickness = 1,
		Color? color = null,
		int segments = 0
	)
	{
		DrawEllipse(center, new Vector2(radius), thickness, color, segments);
	}

	public void FillCircle(
		Vector2 center,
		float radius,
		Color? color = null,
		int segments = 0
	)
	{
		FillEllipse(center, new Vector2(radius), color, segments);
	}

	public void DrawEllipse(
		Vector2 center,
		Vector2 radii,
		float thickness = 1,
		Color? color = null,
		int segments = 0
	)
	{
		EnsureActive();
		context.Renderer.DrawEllipse(
			this,
			center,
			radii,
			thickness,
			color ?? Color.White,
			segments
		);
	}

	public void FillEllipse(
		Vector2 center,
		Vector2 radii,
		Color? color = null,
		int segments = 0
	)
	{
		EnsureActive();
		context.Renderer.FillEllipse(
			this,
			center,
			radii,
			color ?? Color.White,
			segments
		);
	}

	public void DrawTexturedCircle(
		Vector2 center,
		float radius,
		Texture texture,
		float u0 = 0,
		float v0 = 0,
		float u1 = 1,
		float v1 = 1,
		Color? color = null,
		ShaderProgram shader = null,
		int segments = 0
	)
	{
		DrawTexturedEllipse(
			center,
			new Vector2(radius),
			texture,
			u0,
			v0,
			u1,
			v1,
			color,
			shader,
			segments
		);
	}

	public void DrawTexturedEllipse(
		Vector2 center,
		Vector2 radii,
		Texture texture,
		float u0 = 0,
		float v0 = 0,
		float u1 = 1,
		float v1 = 1,
		Color? color = null,
		ShaderProgram shader = null,
		int segments = 0
	)
	{
		EnsureActive();
		context.Renderer.DrawTexturedEllipse(
			this,
			center,
			radii,
			texture,
			new Vector2(u0, v0),
			new Vector2(u1, v1),
			color ?? Color.White,
			shader,
			segments
		);
	}

	public void FillRing(
		Vector2 center,
		float innerRadius,
		float outerRadius,
		Color? color = null,
		int segments = 0
	)
	{
		FillRing(center, innerRadius, outerRadius, 0, MathF.Tau, color, segments);
	}

	public void FillRing(
		Vector2 center,
		float innerRadius,
		float outerRadius,
		float startAngle,
		float endAngle,
		Color? color = null,
		int segments = 0
	)
	{
		EnsureActive();
		context.Renderer.FillRing(
			this,
			center,
			innerRadius,
			outerRadius,
			startAngle,
			endAngle,
			color ?? Color.White,
			segments
		);
	}

	public void DrawRing(
		Vector2 center,
		float innerRadius,
		float outerRadius,
		float thickness = 1,
		Color? color = null,
		int segments = 0
	)
	{
		DrawRing(
			center,
			innerRadius,
			outerRadius,
			0,
			MathF.Tau,
			thickness,
			color,
			segments
		);
	}

	public void DrawRing(
		Vector2 center,
		float innerRadius,
		float outerRadius,
		float startAngle,
		float endAngle,
		float thickness = 1,
		Color? color = null,
		int segments = 0
	)
	{
		EnsureActive();
		context.Renderer.DrawRing(
			this,
			center,
			innerRadius,
			outerRadius,
			startAngle,
			endAngle,
			thickness,
			color ?? Color.White,
			segments
		);
	}

	public void DrawQuadraticBezier(
		Vector2 start,
		Vector2 control,
		Vector2 end,
		float thickness = 1,
		Color? color = null,
		int segments = 0
	)
	{
		EnsureActive();
		context.Renderer.DrawQuadraticBezier(
			this,
			start,
			control,
			end,
			thickness,
			color ?? Color.White,
			segments
		);
	}

	public void DrawCubicBezier(
		Vector2 start,
		Vector2 control1,
		Vector2 control2,
		Vector2 end,
		float thickness = 1,
		Color? color = null,
		int segments = 0
	)
	{
		EnsureActive();
		context.Renderer.DrawCubicBezier(
			this,
			start,
			control1,
			control2,
			end,
			thickness,
			color ?? Color.White,
			segments
		);
	}
}
