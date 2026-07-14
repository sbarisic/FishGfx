using System;
using System.Numerics;
using FishGfx;
using FishGfx.Graphics;

namespace FishGfx.SmokeTest;

internal static partial class PrimitiveScenes
{
	private static void FillRings(RenderPass pass, float time, Texture _)
	{
		Vector2 fullCenter = new(650, 560);

		for (int index = 0; index < 5; index++)
		{
			pass.FillRing(
				fullCenter,
				45 + index * 38,
				70 + index * 38,
				new Color(
					(byte)(70 + index * 34),
					(byte)(225 - index * 18),
					(byte)(115 + index * 24),
					215
				)
			);
		}

		float animatedSweep = 0.2f
			+ (MathF.Sin(time * 1.4f) * 0.5f + 0.5f) * (MathF.Tau - 0.2f);

		pass.FillRing(
			new Vector2(1250, 650),
			85,
			220,
			-MathF.PI / 4,
			-MathF.PI / 4 + animatedSweep,
			new Color(80, 175, 255, 220)
		);
		pass.FillRing(
			new Vector2(1180, 260),
			0,
			150,
			MathF.PI * 1.5f,
			MathF.PI * 2.5f,
			new Color(255, 145, 80, 215),
			6
		);
		pass.FillRing(
			new Vector2(1600, 300),
			125,
			145,
			0,
			MathF.PI * 1.35f,
			new Color(215, 95, 235, 225)
		);
	}

	private static void DrawRings(RenderPass pass, float time, Texture _)
	{
		Vector2 fullCenter = new(680, 560);

		for (int index = 0; index < 5; index++)
		{
			pass.DrawRing(
				fullCenter,
				35 + index * 42,
				70 + index * 42,
				3 + index * 2,
				new Color(
					(byte)(75 + index * 32),
					(byte)(220 - index * 16),
					(byte)(125 + index * 23)
				)
			);
		}

		float start = time * 0.45f;
		float sweep = MathF.PI * 1.45f;

		pass.DrawRing(
			new Vector2(1280, 660),
			90,
			230,
			start,
			start + sweep,
			12,
			new Color(90, 190, 255),
			0
		);
		pass.DrawRing(
			new Vector2(1210, 250),
			0,
			145,
			MathF.PI,
			MathF.PI * 1.8f,
			7,
			new Color(255, 170, 75),
			5
		);
		pass.DrawRing(
			new Vector2(1620, 290),
			130,
			130,
			0,
			MathF.Tau,
			10,
			new Color(225, 105, 240)
		);
	}

	private static void DrawQuadraticBeziers(RenderPass pass, float time, Texture _)
	{
		for (int index = 0; index < 10; index++)
		{
			float y = 180 + index * 85;
			Vector2 start = new(120, y);
			Vector2 control = new(
				Width / 2f,
				y + MathF.Sin(time * 1.8f + index * 0.5f) * 260
			);
			Vector2 end = new(1800, y);

			pass.DrawQuadraticBezier(
				start,
				control,
				end,
				3 + index,
				new Color(
					(byte)(55 + index * 19),
					(byte)(225 - index * 10),
					(byte)(120 + index * 12)
				),
				index == 0 ? 8 : 0
			);
		}
	}

	private static void DrawCubicBeziers(RenderPass pass, float time, Texture _)
	{
		for (int index = 0; index < 9; index++)
		{
			float y = 190 + index * 95;
			float motion = MathF.Sin(time * 1.5f + index * 0.4f) * 120;

			pass.DrawCubicBezier(
				new Vector2(110, y),
				new Vector2(620, y + 260 + motion),
				new Vector2(1300, y - 260 - motion),
				new Vector2(1810, y),
				4 + index,
				new Color(
					(byte)(75 + index * 18),
					(byte)(115 + index * 12),
					(byte)(245 - index * 12)
				),
				index == 0 ? 12 : 0
			);
		}
	}
}
