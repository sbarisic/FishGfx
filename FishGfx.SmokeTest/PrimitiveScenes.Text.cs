using System;
using System.Numerics;
using FishGfx;
using FishGfx.Graphics;

namespace FishGfx.SmokeTest;

internal static partial class PrimitiveScenes
{
	private static void DrawTrueTypeText(RenderPass pass, float time, Texture _)
	{
		float pulse = 0.85f + MathF.Sin(time * 2) * 0.12f;

		pass.DrawText(
			proportionalFont,
			new Vector2(500, 850),
			"TrueType SDF",
			new Color(110, 205, 255),
			120 * pulse
		);
		pass.DrawText(
			proportionalFont,
			new Vector2(510, 735),
			"Sharp from 18 px through 180 px",
			Color.White,
			32
		);
		pass.DrawText(
			proportionalFont,
			new Vector2(510, 665),
			"AVATAR  To Wa Yo  kerning pairs",
			new Color(255, 205, 95),
			48
		);
		pass.DrawText(
			monoFont,
			new Vector2(510, 500),
			"Monospace (40% alpha)\n\tTabs and punctuation:  !? #42",
			new Color(145, 255, 170, 102),
			36
		);
		pass.DrawText(
			monoFont,
			new Vector2(510, 390),
			"Unicode BMP: café  Ω  Ж  Ł  ñ",
			new Color(225, 165, 255, 225),
			42
		);
		pass.DrawText(
			proportionalFont,
			new Vector2(510, 245),
			"Aa",
			new Color(255, 105, 135, 210),
			180
		);
		pass.DrawText(
			proportionalFont,
			new Vector2(830, 275),
			"small SDF text remains smooth",
			new Color(190, 200, 215),
			18
		);
		pass.DrawText(
			monoFont,
			new Vector2(830, 220),
			"fallback: \uD800",
			new Color(255, 150, 105),
			28
		);
	}
}
