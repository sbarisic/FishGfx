using System;
using System.Numerics;

namespace FishGfx.Graphics;

internal sealed partial class ImmediateRenderer
{
	internal void DrawRectangle(
		RenderPass pass,
		float x,
		float y,
		float width,
		float height,
		float thickness,
		Color color
	)
	{
		DrawLineStrip(
			pass,
			new[]
			{
				new Vertex2(new Vector2(x, y), color),
				new Vertex2(new Vector2(x + width, y), color),
				new Vertex2(new Vector2(x + width, y + height), color),
				new Vertex2(new Vector2(x, y + height), color),
				new Vertex2(new Vector2(x, y), color),
			},
			thickness
		);
	}

	internal void FillRectangle(
		RenderPass pass,
		float x,
		float y,
		float width,
		float height,
		Color color
	)
	{
		Ensure2DResources(PrimitiveType.Triangles);
		DrawTexturedRectangle(
			pass,
			x,
			y,
			width,
			height,
			0,
			0,
			1,
			1,
			color,
			whiteTexture,
			null
		);
	}

	internal void DrawTexturedRectangle(
		RenderPass pass,
		float x,
		float y,
		float width,
		float height,
		float u0,
		float v0,
		float u1,
		float v1,
		Color color,
		Texture texture,
		ShaderProgram shader
	)
	{
		Vertex2[] vertices = EmitRectangleTriangles(
			new Vertex2[6],
			0,
			x,
			y,
			width,
			height,
			u0,
			v0,
			u1,
			v1,
			color
		);

		DrawTexturedTriangles(pass, vertices, texture, shader);
	}

	internal void DrawNinePatch(
		RenderPass pass,
		Vector2 position,
		Vector2 size,
		Texture texture,
		NinePatchInsets insets,
		Color color,
		ShaderProgram shader
	)
	{
		ArgumentNullException.ThrowIfNull(texture);

		Vertex2[] vertices = NinePatchTessellator.Create(
			position,
			size,
			texture.Size,
			insets,
			color
		);

		DrawTexturedTriangles(pass, vertices, texture, shader);
	}

	internal void DrawRoundedRectangle(
		RenderPass pass,
		Vector2 position,
		Vector2 size,
		CornerRadii radii,
		float thickness,
		Color color,
		int cornerSegments
	)
	{
		PrimitiveTessellator.ValidateThickness(thickness);

		Vector2[] positions = RoundedRectangleTessellator.Outline(
			position,
			size,
			radii,
			cornerSegments
		);

		if (positions.Length == 0)
		{
			return;
		}

		DrawLineStrip(pass, ColorVertices(positions, color), thickness);
	}

	internal void FillRoundedRectangle(
		RenderPass pass,
		Vector2 position,
		Vector2 size,
		CornerRadii radii,
		Color color,
		int cornerSegments
	)
	{
		Vector2[] positions = RoundedRectangleTessellator.Filled(
			position,
			size,
			radii,
			cornerSegments
		);

		FillTriangles(pass, positions, color);
	}

	internal void DrawTexturedRoundedRectangle(
		RenderPass pass,
		Vector2 position,
		Vector2 size,
		CornerRadii radii,
		Texture texture,
		Vector2 uvMinimum,
		Vector2 uvMaximum,
		Color color,
		ShaderProgram shader,
		int cornerSegments
	)
	{
		ArgumentNullException.ThrowIfNull(texture);

		Vector2[] positions = RoundedRectangleTessellator.Filled(
			position,
			size,
			radii,
			cornerSegments
		);
		Vertex2[] vertices = PrimitiveTessellator.TextureVertices(
			positions,
			position,
			size,
			uvMinimum,
			uvMaximum,
			color
		);

		DrawTexturedTriangles(pass, vertices, texture, shader);
	}
}
