using System;
using System.Numerics;

namespace FishGfx.Voxels;

public readonly struct VoxelTextureRegion
{
	public VoxelTextureRegion(int x, int y, int width, int height, int atlasWidth, int atlasHeight)
	{
		if (x < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(x));
		}

		if (y < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(y));
		}

		if (width <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(width));
		}

		if (height <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(height));
		}

		if (atlasWidth <= 0 || x + width > atlasWidth)
		{
			throw new ArgumentOutOfRangeException(nameof(atlasWidth));
		}

		if (atlasHeight <= 0 || y + height > atlasHeight)
		{
			throw new ArgumentOutOfRangeException(nameof(atlasHeight));
		}

		X = x;
		Y = y;
		Width = width;
		Height = height;
		AtlasWidth = atlasWidth;
		AtlasHeight = atlasHeight;
	}

	public int X { get; }
	public int Y { get; }
	public int Width { get; }
	public int Height { get; }
	public int AtlasWidth { get; }
	public int AtlasHeight { get; }

	public Vector2 Map(Vector2 sourceUv)
	{
		if (!IsFinite(sourceUv))
		{
			throw new ArgumentOutOfRangeException(nameof(sourceUv));
		}

		return new Vector2(
			(X + sourceUv.X * Width) / AtlasWidth,
			1 - (Y + sourceUv.Y * Height) / AtlasHeight
		);
	}

	private static bool IsFinite(Vector2 value)
	{
		return float.IsFinite(value.X) && float.IsFinite(value.Y);
	}
}
