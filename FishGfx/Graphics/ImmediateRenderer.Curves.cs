using System;
using System.Numerics;

namespace FishGfx.Graphics;

internal sealed partial class ImmediateRenderer
{
	internal void DrawEllipse(
		RenderPass pass,
		Vector2 center,
		Vector2 radii,
		float thickness,
		Color color,
		int segments
	)
	{
		PrimitiveTessellator.ValidateThickness(thickness);

		Vector2[] positions = PrimitiveTessellator.EllipseOutline(
			center,
			radii,
			segments
		);

		if (positions.Length == 0)
		{
			return;
		}

		DrawLineStrip(pass, ColorVertices(positions, color), thickness);
	}

	internal void FillEllipse(
		RenderPass pass,
		Vector2 center,
		Vector2 radii,
		Color color,
		int segments
	)
	{
		Vector2[] positions = PrimitiveTessellator.FilledEllipse(
			center,
			radii,
			segments
		);
		FillTriangles(pass, positions, color);
	}

	internal void DrawTexturedEllipse(
		RenderPass pass,
		Vector2 center,
		Vector2 radii,
		Texture texture,
		Vector2 uvMinimum,
		Vector2 uvMaximum,
		Color color,
		ShaderProgram shader,
		int segments
	)
	{
		ArgumentNullException.ThrowIfNull(texture);

		Vector2[] positions = PrimitiveTessellator.FilledEllipse(
			center,
			radii,
			segments
		);
		Vector2 boundsMinimum = center - radii;
		Vector2 boundsSize = radii * 2;
		Vertex2[] vertices = PrimitiveTessellator.TextureVertices(
			positions,
			boundsMinimum,
			boundsSize,
			uvMinimum,
			uvMaximum,
			color
		);

		DrawTexturedTriangles(pass, vertices, texture, shader);
	}

	internal void FillRing(
		RenderPass pass,
		Vector2 center,
		float innerRadius,
		float outerRadius,
		float startAngle,
		float endAngle,
		Color color,
		int segments
	)
	{
		Vector2[] positions = RingTessellator.Filled(
			center,
			innerRadius,
			outerRadius,
			startAngle,
			endAngle,
			segments
		);

		FillTriangles(pass, positions, color);
	}

	internal void DrawRing(
		RenderPass pass,
		Vector2 center,
		float innerRadius,
		float outerRadius,
		float startAngle,
		float endAngle,
		float thickness,
		Color color,
		int segments
	)
	{
		PrimitiveTessellator.ValidateThickness(thickness);

		Vector2[][] paths = RingTessellator.Lines(
			center,
			innerRadius,
			outerRadius,
			startAngle,
			endAngle,
			segments
		);

		foreach (Vector2[] path in paths)
		{
			DrawLineStrip(pass, ColorVertices(path, color), thickness);
		}
	}

	internal void DrawQuadraticBezier(
		RenderPass pass,
		Vector2 start,
		Vector2 control,
		Vector2 end,
		float thickness,
		Color color,
		int segments
	)
	{
		PrimitiveTessellator.ValidateThickness(thickness);

		Vector2[] positions = PrimitiveTessellator.QuadraticBezier(
			start,
			control,
			end,
			segments
		);

		DrawLineStrip(pass, ColorVertices(positions, color), thickness);
	}

	internal void DrawCubicBezier(
		RenderPass pass,
		Vector2 start,
		Vector2 control1,
		Vector2 control2,
		Vector2 end,
		float thickness,
		Color color,
		int segments
	)
	{
		PrimitiveTessellator.ValidateThickness(thickness);

		Vector2[] positions = PrimitiveTessellator.CubicBezier(
			start,
			control1,
			control2,
			end,
			segments
		);

		DrawLineStrip(pass, ColorVertices(positions, color), thickness);
	}
}
