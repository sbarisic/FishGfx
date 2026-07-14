using System;
using System.Numerics;

namespace FishGfx.Graphics;

internal sealed partial class ImmediateRenderer
{
	private void FillTriangles(
		RenderPass pass,
		Vector2[] positions,
		Color color
	)
	{
		if (positions.Length == 0)
		{
			return;
		}

		Ensure2DResources(PrimitiveType.Triangles);
		scratchMesh2D.SetVertices(ColorVertices(positions, color));
		DrawMesh2D(pass, scratchMesh2D, whiteTexture, default2D);
	}

	private void DrawTexturedTriangles(
		RenderPass pass,
		Vertex2[] vertices,
		Texture texture,
		ShaderProgram shader
	)
	{
		ArgumentNullException.ThrowIfNull(vertices);

		if (vertices.Length == 0)
		{
			return;
		}

		Ensure2DResources(PrimitiveType.Triangles);
		scratchMesh2D.SetVertices(vertices);
		DrawMesh2D(
			pass,
			scratchMesh2D,
			texture ?? whiteTexture,
			shader ?? default2D
		);
	}

	private static Vertex2[] EmitRectangleTriangles(
		Vertex2[] vertices,
		int offset,
		float x,
		float y,
		float width,
		float height,
		float u0,
		float v0,
		float u1,
		float v1,
		Color color
	)
	{
		vertices[offset] = new Vertex2(
			new Vector2(x, y),
			new Vector2(u0, v0),
			color
		);
		vertices[offset + 1] = new Vertex2(
			new Vector2(x + width, y + height),
			new Vector2(u1, v1),
			color
		);
		vertices[offset + 2] = new Vertex2(
			new Vector2(x, y + height),
			new Vector2(u0, v1),
			color
		);
		vertices[offset + 3] = new Vertex2(
			new Vector2(x, y),
			new Vector2(u0, v0),
			color
		);
		vertices[offset + 4] = new Vertex2(
			new Vector2(x + width, y),
			new Vector2(u1, v0),
			color
		);
		vertices[offset + 5] = new Vertex2(
			new Vector2(x + width, y + height),
			new Vector2(u1, v1),
			color
		);

		return vertices;
	}

	private static Vertex2[] ColorVertices(
		Vector2[] positions,
		Color color
	)
	{
		Vertex2[] vertices = new Vertex2[positions.Length];

		for (int index = 0; index < positions.Length; index++)
		{
			vertices[index] = new Vertex2(positions[index], color);
		}

		return vertices;
	}
}
