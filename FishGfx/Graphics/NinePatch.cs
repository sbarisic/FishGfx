using System;
using System.Numerics;

namespace FishGfx.Graphics;

public readonly record struct NinePatchInsets
{
	public NinePatchInsets(float uniformInset)
		: this(uniformInset, uniformInset, uniformInset, uniformInset)
	{
	}

	public NinePatchInsets(
		float left,
		float top,
		float right,
		float bottom
	)
	{
		Validate(left, nameof(left));
		Validate(top, nameof(top));
		Validate(right, nameof(right));
		Validate(bottom, nameof(bottom));
		Left = left;
		Top = top;
		Right = right;
		Bottom = bottom;
	}

	public float Left { get; }

	public float Top { get; }

	public float Right { get; }

	public float Bottom { get; }

	private static void Validate(float value, string parameterName)
	{
		if (!float.IsFinite(value) || value < 0)
		{
			throw new ArgumentOutOfRangeException(
				parameterName,
				"Nine-patch insets must be finite and non-negative."
			);
		}
	}
}

internal static class NinePatchTessellator
{
	internal static Vertex2[] Create(
		Vector2 position,
		Vector2 size,
		Vector2 textureSize,
		NinePatchInsets insets,
		Color color
	)
	{
		ValidateFinite(position, nameof(position));
		ValidateFinite(size, nameof(size));
		ValidateFinite(textureSize, nameof(textureSize));

		if (size.X < 0 || size.Y < 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(size),
				"Nine-patch destination size cannot be negative."
			);
		}

		if (textureSize.X <= 0 || textureSize.Y <= 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(textureSize),
				"Nine-patch texture dimensions must be positive."
			);
		}

		if (insets.Left + insets.Right > textureSize.X)
		{
			throw new ArgumentOutOfRangeException(
				nameof(insets),
				"Horizontal insets exceed the texture width."
			);
		}

		if (insets.Top + insets.Bottom > textureSize.Y)
		{
			throw new ArgumentOutOfRangeException(
				nameof(insets),
				"Vertical insets exceed the texture height."
			);
		}

		if (size.X == 0 || size.Y == 0)
		{
			return Array.Empty<Vertex2>();
		}

		(float left, float right) = FitBorders(
			insets.Left,
			insets.Right,
			size.X
		);
		(float bottom, float top) = FitBorders(
			insets.Bottom,
			insets.Top,
			size.Y
		);
		float[] x =
		{
			position.X,
			position.X + left,
			position.X + size.X - right,
			position.X + size.X,
		};
		float[] y =
		{
			position.Y,
			position.Y + bottom,
			position.Y + size.Y - top,
			position.Y + size.Y,
		};
		float[] u =
		{
			0,
			insets.Left / textureSize.X,
			1 - insets.Right / textureSize.X,
			1,
		};
		float[] v =
		{
			0,
			insets.Bottom / textureSize.Y,
			1 - insets.Top / textureSize.Y,
			1,
		};
		Vertex2[] vertices = new Vertex2[54];
		int offset = 0;

		for (int row = 0; row < 3; row++)
		{
			for (int column = 0; column < 3; column++)
			{
				EmitQuad(
					vertices,
					offset,
					x[column],
					y[row],
					x[column + 1],
					y[row + 1],
					u[column],
					v[row],
					u[column + 1],
					v[row + 1],
					color
				);
				offset += 6;
			}
		}

		return vertices;
	}

	private static (float First, float Second) FitBorders(
		float first,
		float second,
		float available
	)
	{
		float total = first + second;

		if (total <= available || total == 0)
		{
			return (first, second);
		}

		float scale = available / total;

		return (first * scale, second * scale);
	}

	private static void EmitQuad(
		Vertex2[] vertices,
		int offset,
		float x0,
		float y0,
		float x1,
		float y1,
		float u0,
		float v0,
		float u1,
		float v1,
		Color color
	)
	{
		vertices[offset] = new Vertex2(
			new Vector2(x0, y0),
			new Vector2(u0, v0),
			color
		);
		vertices[offset + 1] = new Vertex2(
			new Vector2(x1, y1),
			new Vector2(u1, v1),
			color
		);
		vertices[offset + 2] = new Vertex2(
			new Vector2(x0, y1),
			new Vector2(u0, v1),
			color
		);
		vertices[offset + 3] = vertices[offset];
		vertices[offset + 4] = new Vertex2(
			new Vector2(x1, y0),
			new Vector2(u1, v0),
			color
		);
		vertices[offset + 5] = vertices[offset + 1];
	}

	private static void ValidateFinite(Vector2 value, string parameterName)
	{
		if (!float.IsFinite(value.X) || !float.IsFinite(value.Y))
		{
			throw new ArgumentOutOfRangeException(
				parameterName,
				"Nine-patch values must be finite."
			);
		}
	}
}
