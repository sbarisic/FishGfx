using System;
using System.Numerics;
using FishGfx;
using FishGfx.Graphics;

namespace FishGfx.SmokeTest;

internal static partial class PrimitiveScenes
{
	private static void DrawCircles(RenderPass pass, float time, Texture _)
	{
		for (int row = 0; row < 3; row++)
		{
			for (int column = 0; column < 6; column++)
			{
				float radius = 45 + row * 35 + MathF.Sin(time * 2 + column) * 8;
				Vector2 center = new(220 + column * 290, 260 + row * 280);

				pass.DrawCircle(
					center,
					radius,
					3 + column * 2,
					new Color(
						(byte)(50 + column * 30),
						(byte)(225 - row * 35),
						(byte)(110 + row * 45)
					),
					column == 0 ? 12 : 0
				);
			}
		}
	}

	private static void FillCircles(RenderPass pass, float time, Texture _)
	{
		for (int ring = 0; ring < 4; ring++)
		{
			int count = 7 + ring * 3;

			for (int index = 0; index < count; index++)
			{
				float angle = time * (0.25f + ring * 0.08f) + index * MathF.Tau / count;
				Vector2 center = new Vector2(Width / 2f, Height / 2f + 40)
					+ new Vector2(MathF.Cos(angle), MathF.Sin(angle))
					* (100 + ring * 115);

				pass.FillCircle(
					center,
					24 + ring * 11,
					new Color(
						(byte)(70 + ring * 42),
						(byte)(210 - ring * 24),
						(byte)(120 + index * 7),
						215
					)
				);
			}
		}
	}

	private static void DrawEllipses(RenderPass pass, float time, Texture _)
	{
		for (int row = 0; row < 3; row++)
		{
			for (int column = 0; column < 5; column++)
			{
				Vector2 center = new(260 + column * 350, 270 + row * 285);
				Vector2 radii = new(
					120 + MathF.Sin(time + column) * 25,
					45 + row * 28
				);

				pass.DrawEllipse(
					center,
					radii,
					4 + row * 4,
					new Color(
						(byte)(80 + column * 30),
						(byte)(215 - row * 30),
						(byte)(230 - column * 20)
					),
					column == 0 ? 16 : 0
				);
			}
		}
	}

	private static void FillEllipses(RenderPass pass, float time, Texture _)
	{
		for (int index = 0; index < 12; index++)
		{
			float angle = index * MathF.Tau / 12 + time * 0.2f;
			Vector2 center = new Vector2(Width / 2f, Height / 2f + 30)
				+ new Vector2(MathF.Cos(angle) * 570, MathF.Sin(angle) * 330);
			Vector2 radii = new(125 + index * 4, 35 + index * 2);

			pass.FillEllipse(
				center,
				radii,
				new Color(
					(byte)(60 + index * 15),
					(byte)(220 - index * 9),
					(byte)(115 + index * 10),
					210
				)
			);
		}
	}
}
