using System;
using System.Numerics;

namespace FishGfx.FishUI;

internal readonly record struct FishUITextLayout(
	float FontSize,
	float CharacterSpacing,
	Vector2 Size
)
{
	internal static FishUITextLayout Create(
		GraphicsFont font,
		string text,
		float fontSize,
		float characterSpacing,
		float scale = 1
	)
	{
		ArgumentNullException.ThrowIfNull(font);
		ArgumentNullException.ThrowIfNull(text);

		if (!float.IsFinite(scale) || scale <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(scale));
		}

		float scaledFontSize = fontSize * scale;
		float scaledCharacterSpacing = characterSpacing * scale;
		Vector2 measuredSize = font.Measure(
			text,
			scaledFontSize,
			scaledCharacterSpacing
		);

		return new FishUITextLayout(
			scaledFontSize,
			scaledCharacterSpacing,
			measuredSize
		);
	}
}
