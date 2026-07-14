using System.Collections.Generic;
using System.Numerics;

namespace FishGfx;

public struct Vertex2
{
	public Vertex2(Vector2 position, Vector2 uv, Color color)
	{
		Position = position;
		UV = uv;
		Color = color;
	}

	public Vertex2(Vector2 position, Vector2 uv)
		: this(position, uv, Color.White)
	{
	}

	public Vertex2(Vector2 position, Color color)
		: this(position, Vector2.Zero, color)
	{
	}

	public Vertex2(Vector2 position)
		: this(position, Vector2.Zero)
	{
	}

	public Vertex2(float x, float y)
		: this(new Vector2(x, y))
	{
	}

	public Vector2 Position;

	public Vector2 UV;

	public Color Color;

	public static IReadOnlyList<Vertex2> CreateQuad(
		Vector2 position,
		Vector2 size,
		Vector2 uv,
		Vector2 uvSize,
		Color color
	)
	{
		Vector2 bottomLeft = position;
		Vector2 topLeft = position + new Vector2(0, size.Y);
		Vector2 topRight = position + size;
		Vector2 bottomRight = position + new Vector2(size.X, 0);
		Vector2 uvBottomLeft = uv;
		Vector2 uvTopLeft = uv + new Vector2(0, uvSize.Y);
		Vector2 uvTopRight = uv + uvSize;
		Vector2 uvBottomRight = uv + new Vector2(uvSize.X, 0);

		return new[]
		{
			new Vertex2(bottomLeft, uvBottomLeft, color),
			new Vertex2(topLeft, uvTopLeft, color),
			new Vertex2(topRight, uvTopRight, color),
			new Vertex2(bottomLeft, uvBottomLeft, color),
			new Vertex2(topRight, uvTopRight, color),
			new Vertex2(bottomRight, uvBottomRight, color),
		};
	}

	public static IReadOnlyList<Vertex2> CreateQuad(
		Vector2 position,
		Vector2 size,
		Vector2 uv,
		Vector2 uvSize
	)
	{
		return CreateQuad(position, size, uv, uvSize, Color.White);
	}

	public static implicit operator Vertex2(Vector2 position)
	{
		return new Vertex2(position);
	}
}
