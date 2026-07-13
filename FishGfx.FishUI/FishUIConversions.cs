using System;
using System.Numerics;

namespace FishGfx.FishUI
{
	/// <summary>Coordinate, atlas, and color conversions shared by the FishUI backend.</summary>
	public static class FishUIConversions
	{
		public static Vector2 ToFishGfxPoint(Vector2 point, float viewportHeight)
		{
			ValidateViewportHeight(viewportHeight);
			ValidateFinite(point, nameof(point));
			return new Vector2(point.X, viewportHeight - point.Y);
		}

		public static Vector2 ToFishGfxRectanglePosition(Vector2 position, Vector2 size, float viewportHeight)
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
				throw new ArgumentOutOfRangeException(nameof(textureWidth));
			if (textureHeight <= 0)
				throw new ArgumentOutOfRangeException(nameof(textureHeight));
			if (
				sourcePosition.X < 0
				|| sourcePosition.Y < 0
				|| sourcePosition.X + sourceSize.X > textureWidth
				|| sourcePosition.Y + sourceSize.Y > textureHeight
			)
				throw new ArgumentOutOfRangeException(nameof(sourcePosition), "The source region exceeds the texture bounds.");

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
				throw new ArgumentOutOfRangeException(nameof(viewportHeight));
		}

		private static void ValidateFinite(Vector2 value, string name)
		{
			if (!float.IsFinite(value.X) || !float.IsFinite(value.Y))
				throw new ArgumentOutOfRangeException(name);
		}

		private static void ValidateSize(Vector2 value, string name)
		{
			ValidateFinite(value, name);
			if (value.X < 0 || value.Y < 0)
				throw new ArgumentOutOfRangeException(name);
		}
	}
}
