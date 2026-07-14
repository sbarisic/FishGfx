using System;
using System.Numerics;
using FishGfx;
using FishGfx.Graphics;

namespace FishGfx.SmokeTest;

internal static partial class PrimitiveScenes
{
	private static void DrawLines(RenderPass pass, float time, Texture _)
	{
		for (int index = 0; index < 14; index++)
		{
			float y = 170 + index * 58;
			float wave = MathF.Sin(time * 1.5f + index * 0.55f) * 100;
			Color start = new(
				(byte)(40 + index * 14),
				(byte)(220 - index * 8),
				255
			);
			Color end = new(
				255,
				(byte)(70 + index * 11),
				(byte)(190 - index * 7)
			);

			pass.DrawLine(
				new Vertex2(new Vector2(130, y), start),
				new Vertex2(new Vector2(1790, y + wave), end),
				2 + index * 1.5f
			);
		}
	}

	private static void DrawRectangles(RenderPass pass, float time, Texture _)
	{
		for (int index = 0; index < 9; index++)
		{
			float inset = index * 45;
			float pulse = MathF.Sin(time * 2 + index * 0.4f) * 10;

			pass.DrawRectangle(
				160 + inset - pulse,
				160 + inset - pulse,
				1600 - inset * 2 + pulse * 2,
				790 - inset * 2 + pulse * 2,
				2 + index * 2,
				new Color(
					(byte)(60 + index * 20),
					(byte)(210 - index * 12),
					(byte)(120 + index * 13)
				)
			);
		}
	}

	private static void FillRectangles(RenderPass pass, float time, Texture _)
	{
		for (int row = 0; row < 4; row++)
		{
			for (int column = 0; column < 7; column++)
			{
				float pulse = 0.85f + MathF.Sin(time * 2 + row + column * 0.4f) * 0.12f;
				float width = 190 * pulse;
				float height = 145 * pulse;
				float x = 155 + column * 245 + (190 - width) / 2;
				float y = 190 + row * 205 + (145 - height) / 2;

				pass.FillRectangle(
					x,
					y,
					width,
					height,
					new Color(
						(byte)(45 + column * 28),
						(byte)(65 + row * 45),
						(byte)(210 - column * 16),
						220
					)
				);
			}
		}
	}

	private static void DrawRoundedRectangles(RenderPass pass, float time, Texture _)
	{
		for (int row = 0; row < 4; row++)
		{
			for (int column = 0; column < 4; column++)
			{
				float x = 430 + column * 350;
				float y = 170 + row * 220;
				float pulse = 15 * MathF.Sin(time * 1.8f + row + column);
				CornerRadii radii = column switch
				{
					0 => new CornerRadii(15 + row * 18),
					1 => new CornerRadii(10, 70 + pulse, 20, 50),
					2 => new CornerRadii(160),
					_ => new CornerRadii(0, 75, 0, 75),
				};

				pass.DrawRoundedRectangle(
					x,
					y,
					280,
					150,
					radii,
					3 + row * 2,
					new Color(
						(byte)(80 + column * 35),
						(byte)(220 - row * 28),
						(byte)(125 + row * 25)
					),
					row == 0 ? 2 : 0
				);
			}
		}
	}

	private static void FillRoundedRectangles(RenderPass pass, float time, Texture _)
	{
		for (int row = 0; row < 3; row++)
		{
			for (int column = 0; column < 5; column++)
			{
				float x = 410 + column * 290;
				float y = 210 + row * 275;
				float width = 235 + MathF.Sin(time * 1.5f + column) * 22;
				CornerRadii radii = row == 0
					? new CornerRadii(20 + column * 18)
					: new CornerRadii(
						15 + column * 8,
						90,
						35 + row * 15,
						column * 12
					);

				pass.FillRoundedRectangle(
					x,
					y,
					width,
					190,
					radii,
					new Color(
						(byte)(55 + column * 35),
						(byte)(90 + row * 55),
						(byte)(220 - column * 20),
						220
					),
					column == 0 ? 2 : 0
				);
			}
		}
	}

	private static void DrawLineStrips(RenderPass pass, float time, Texture _)
	{
		for (int strip = 0; strip < 6; strip++)
		{
			Vertex2[] points = new Vertex2[18];

			for (int index = 0; index < points.Length; index++)
			{
				float x = 100 + index * 100;
				float y = 220
					+ strip * 135
					+ MathF.Sin(time * 2 + index * 0.55f + strip) * 55;
				points[index] = new Vertex2(
					new Vector2(x, y),
					new Color(
						(byte)(60 + index * 9),
						(byte)(230 - strip * 22),
						(byte)(100 + strip * 24)
					)
				);
			}

			pass.DrawLineStrip(points, 4 + strip * 2);
		}
	}

	private static void DrawPoints(RenderPass pass, float time, Texture _)
	{
		Vector2 center = new(Width / 2f, Height / 2f + 40);

		for (int ring = 0; ring < 5; ring++)
		{
			int count = 10 + ring * 6;
			float radius = 100 + ring * 85;

			for (int index = 0; index < count; index++)
			{
				float angle = time * (0.35f + ring * 0.12f) + index * MathF.Tau / count;
				Vector2 position = center
					+ new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;

				pass.DrawPoint(
					new Vertex2(
						position,
						new Color(
							(byte)(70 + ring * 38),
							(byte)(230 - ring * 25),
							(byte)(110 + index * 5)
						)
					),
					9 + ring * 5
				);
			}
		}
	}
}
