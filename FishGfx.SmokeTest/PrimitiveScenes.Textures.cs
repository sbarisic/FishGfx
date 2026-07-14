using System;
using System.Numerics;
using FishGfx;
using FishGfx.Graphics;

namespace FishGfx.SmokeTest;

internal static partial class PrimitiveScenes
{
	private static void DrawTexturedRectangles(RenderPass pass, float time, Texture texture)
	{
		for (int index = 0; index < 8; index++)
		{
			float x = 135 + index % 4 * 440;
			float y = 190 + index / 4 * 390;
			float scale = 0.9f + MathF.Sin(time * 1.5f + index) * 0.08f;
			float width = 350 * scale;
			float height = 280 * scale;

			pass.DrawTexturedRectangle(
				x + (350 - width) / 2,
				y + (280 - height) / 2,
				width,
				height,
				color: new Color(
					(byte)(255 - index * 12),
					(byte)(180 + index * 8),
					255
				),
				texture: texture
			);
		}
	}

	private static void DrawTexturedRoundedRectangles(
		RenderPass pass,
		float time,
		Texture texture
	)
	{
		for (int row = 0; row < 3; row++)
		{
			for (int column = 0; column < 4; column++)
			{
				float x = 420 + column * 360;
				float y = 190 + row * 285;
				float height = 210 + MathF.Sin(time * 1.7f + row + column) * 25;
				CornerRadii radii = column == 3
					? new CornerRadii(180)
					: new CornerRadii(
						20 + row * 20,
						80 - column * 10,
						25 + column * 18,
						row * 12
					);

				pass.DrawTexturedRoundedRectangle(
					x,
					y,
					300,
					height,
					radii,
					texture,
					color: new Color(
						(byte)(255 - row * 18),
						(byte)(205 + column * 10),
						255,
						225
					),
					cornerSegments: row == 0 ? 2 : 0
				);
			}
		}
	}

	private static void DrawTexturedCircles(RenderPass pass, float time, Texture texture)
	{
		for (int ring = 0; ring < 4; ring++)
		{
			int count = 6 + ring * 3;

			for (int index = 0; index < count; index++)
			{
				float angle = time * (0.2f + ring * 0.08f) + index * MathF.Tau / count;
				Vector2 center = new Vector2(Width / 2f + 120, Height / 2f)
					+ new Vector2(MathF.Cos(angle), MathF.Sin(angle))
					* (100 + ring * 105);

				pass.DrawTexturedCircle(
					center,
					35 + ring * 10,
					texture,
					color: new Color(
						(byte)(220 - ring * 15),
						255,
						(byte)(185 + ring * 15),
						225
					),
					segments: ring == 0 ? 12 : 0
				);
			}
		}
	}

	private static void DrawTexturedEllipses(RenderPass pass, float time, Texture texture)
	{
		for (int row = 0; row < 3; row++)
		{
			for (int column = 0; column < 4; column++)
			{
				Vector2 center = new(500 + column * 375, 250 + row * 290);
				Vector2 radii = new(
					135 + MathF.Sin(time * 1.4f + column) * 25,
					55 + row * 25
				);

				pass.DrawTexturedEllipse(
					center,
					radii,
					texture,
					color: new Color(
						(byte)(205 + row * 15),
						(byte)(235 - column * 12),
						255,
						220
					),
					segments: column == 0 ? 16 : 0
				);
			}
		}
	}

	private static void DrawNinePatches(RenderPass pass, float time, Texture texture)
	{
		NinePatchInsets insets = new(64);

		pass.DrawTexturedRectangle(90, 650, 256, 256, texture: texture);
		pass.DrawRectangle(90, 650, 256, 256, 3, new Color(120, 135, 170));
		pass.DrawNinePatch(410, 690, 430, 190, texture, insets);
		pass.DrawNinePatch(
			900,
			700,
			850,
			170,
			texture,
			insets,
			new Color(210, 245, 255)
		);
		pass.DrawNinePatch(
			130,
			180,
			210,
			390,
			texture,
			insets,
			new Color(255, 220, 190)
		);
		pass.DrawNinePatch(
			410,
			190,
			380,
			380,
			texture,
			insets,
			new Color(210, 255, 215, 225)
		);
		pass.DrawNinePatch(
			860,
			230,
			96,
			84,
			texture,
			insets,
			new Color(255, 190, 210)
		);

		float animatedWidth = 520 + MathF.Sin(time * 1.8f) * 180;
		float animatedHeight = 220 + MathF.Cos(time * 1.4f) * 70;

		pass.DrawNinePatch(
			1080,
			190,
			animatedWidth,
			animatedHeight,
			texture,
			insets,
			new Color(190, 220, 255, 215)
		);
	}
}
