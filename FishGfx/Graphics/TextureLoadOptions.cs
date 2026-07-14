using System;

namespace FishGfx.Graphics;

public sealed class TextureLoadOptions
{
	public bool FlipY { get; init; } = true;

	public TextureFormat Format { get; init; } = TextureFormat.RGBA8Unorm;

	public int MipLevels { get; init; } = 1;

	public TextureSamplingState Sampling { get; init; } = TextureSamplingState.Default;
}

public readonly record struct CubemapPaths
{
	public CubemapPaths(
		string left,
		string front,
		string right,
		string back,
		string bottom,
		string top
	)
	{
		Left = Required(left, nameof(left));
		Front = Required(front, nameof(front));
		Right = Required(right, nameof(right));
		Back = Required(back, nameof(back));
		Bottom = Required(bottom, nameof(bottom));
		Top = Required(top, nameof(top));
	}

	public string Left { get; }

	public string Front { get; }

	public string Right { get; }

	public string Back { get; }

	public string Bottom { get; }

	public string Top { get; }

	private static string Required(string value, string parameterName)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			throw new ArgumentException(
				"A cubemap path is required.",
				parameterName
			);
		}

		return value;
	}
}
