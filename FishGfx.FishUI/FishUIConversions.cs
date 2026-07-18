using System;
using System.Numerics;

namespace FishGfx.FishUI;

/// <summary>Coordinate, atlas, and color conversions shared by the FishUI backend.</summary>
public static class FishUIConversions
{
	public static Vector2 CalculateFramebufferScale(
		Vector2 logicalSize,
		Vector2 framebufferSize
	)
	{
		ValidateSize(logicalSize, nameof(logicalSize));
		ValidateSize(framebufferSize, nameof(framebufferSize));

		if (logicalSize.X <= 0 || logicalSize.Y <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(logicalSize));
		}

		if (framebufferSize.X <= 0 || framebufferSize.Y <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(framebufferSize));
		}

		return framebufferSize / logicalSize;
	}

	public static (Vector2 Position, Vector2 Size) ToFramebufferRectangle(
		Vector2 position,
		Vector2 size,
		float logicalViewportHeight,
		Vector2 contentScale
	)
	{
		ValidateFinite(contentScale, nameof(contentScale));
		if (contentScale.X <= 0 || contentScale.Y <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(contentScale));
		}

		Vector2 logicalPosition = ToFishGfxRectanglePosition(
			position,
			size,
			logicalViewportHeight
		);
		Vector2 minimum = new(
			MathF.Floor(logicalPosition.X * contentScale.X),
			MathF.Floor(logicalPosition.Y * contentScale.Y)
		);
		Vector2 maximum = new(
			MathF.Ceiling((logicalPosition.X + size.X) * contentScale.X),
			MathF.Ceiling((logicalPosition.Y + size.Y) * contentScale.Y)
		);
		return (minimum, maximum - minimum);
	}

	public static Vector2 ToFishGfxPoint(Vector2 point, float viewportHeight)
	{
		ValidateViewportHeight(viewportHeight);
		ValidateFinite(point, nameof(point));

		return new Vector2(point.X, viewportHeight - point.Y);
	}

	public static Vector2 ToFishGfxRectanglePosition(
		Vector2 position,
		Vector2 size,
		float viewportHeight
	)
	{
		ValidateViewportHeight(viewportHeight);
		ValidateFinite(position, nameof(position));
		ValidateSize(size, nameof(size));

		return new Vector2(position.X, viewportHeight - position.Y - size.Y);
	}

	public static (Vector2 Minimum, Vector2 Maximum) ToAtlasUv(
		Vector2 sourcePosition,
		Vector2 sourceSize,
		int textureWidth,
		int textureHeight
	)
	{
		ValidateFinite(sourcePosition, nameof(sourcePosition));
		ValidateSize(sourceSize, nameof(sourceSize));

		if (textureWidth <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(textureWidth));
		}

		if (textureHeight <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(textureHeight));
		}

		if (sourcePosition.X < 0
			|| sourcePosition.Y < 0
			|| sourcePosition.X + sourceSize.X > textureWidth
			|| sourcePosition.Y + sourceSize.Y > textureHeight)
		{
			throw new ArgumentOutOfRangeException(
				nameof(sourcePosition),
				"The source region exceeds the texture bounds."
			);
		}

		return (
			new Vector2(
				sourcePosition.X / textureWidth,
				1 - (sourcePosition.Y + sourceSize.Y) / textureHeight
			),
			new Vector2(
				(sourcePosition.X + sourceSize.X) / textureWidth,
				1 - sourcePosition.Y / textureHeight
			)
		);
	}

	public static Color ToFishGfxColor(global::FishUI.FishColor color)
	{
		return new Color(color.R, color.G, color.B, color.A);
	}

	private static void ValidateViewportHeight(float viewportHeight)
	{
		if (!float.IsFinite(viewportHeight) || viewportHeight < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(viewportHeight));
		}
	}

	private static void ValidateFinite(Vector2 value, string name)
	{
		if (!float.IsFinite(value.X) || !float.IsFinite(value.Y))
		{
			throw new ArgumentOutOfRangeException(name);
		}
	}

	private static void ValidateSize(Vector2 value, string name)
	{
		ValidateFinite(value, name);

		if (value.X < 0 || value.Y < 0)
		{
			throw new ArgumentOutOfRangeException(name);
		}
	}
}
