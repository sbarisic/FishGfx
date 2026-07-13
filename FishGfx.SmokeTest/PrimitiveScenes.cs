using System;
using System.Numerics;
using FishGfx;
using FishGfx.Formats;
using FishGfx.Graphics;

namespace FishGfx.SmokeTest
{
	internal static class PrimitiveScenes
	{
		private const int Width = PrimitiveGallery.Width;
		private const int Height = PrimitiveGallery.Height;
		private static TTFFont proportionalFont;
		private static TTFFont monoFont;
		private static CommandList commandListScene;
		private static DeferredRenderQueue deferredQueueScene;
		private static CommandList[] deferredOpaqueCommands;
		private static CommandList[] deferredTransparentCommands;
		private static CommandList deferredOverlayCommands;

		internal static void InitializeFonts()
		{
			proportionalFont = new TTFFont(PrimitiveGallery.AssetPath("fonts", "Aaargh.ttf"));
			monoFont = new TTFFont(PrimitiveGallery.AssetPath("fonts", "Consolas-Regular.ttf"));
		}

		internal static void DisposeFonts()
		{
			commandListScene?.Clear();
			commandListScene = null;
			deferredQueueScene?.Clear();
			deferredQueueScene = null;
			deferredOpaqueCommands = null;
			deferredTransparentCommands = null;
			deferredOverlayCommands = null;
			proportionalFont?.Dispose();
			monoFont?.Dispose();
			proportionalFont = monoFont = null;
		}

		internal static GalleryScene[] Create()
		{
			return new[]
			{
				new GalleryScene("Gfx.Line", DrawLines),
				new GalleryScene("Gfx.Rectangle", DrawRectangles),
				new GalleryScene("Gfx.FilledRectangle", DrawFilledRectangles),
				new GalleryScene("Gfx.RoundedRectangle", DrawRoundedRectangles),
				new GalleryScene("Gfx.FilledRoundedRectangle", DrawFilledRoundedRectangles),
				new GalleryScene("Gfx.LineStrip", DrawLineStrips),
				new GalleryScene("Gfx.Point", DrawPoints),
				new GalleryScene("Gfx.TexturedRectangle", DrawTexturedRectangles),
				new GalleryScene("Gfx.TexturedRoundedRectangle", DrawTexturedRoundedRectangles),
				new GalleryScene("Gfx.TexturedCircle", DrawTexturedCircles),
				new GalleryScene("Gfx.TexturedEllipse", DrawTexturedEllipses),
				new GalleryScene("Gfx.NinePatch", DrawNinePatches),
				new GalleryScene("Gfx.Circle", DrawCircles),
				new GalleryScene("Gfx.FilledCircle", DrawFilledCircles),
				new GalleryScene("Gfx.Ring", DrawRings),
				new GalleryScene("Gfx.RingLines", DrawRingLines),
				new GalleryScene("Gfx.Ellipse", DrawEllipses),
				new GalleryScene("Gfx.FilledEllipse", DrawFilledEllipses),
				new GalleryScene("Gfx.QuadraticBezier", DrawQuadraticBeziers),
				new GalleryScene("Gfx.CubicBezier", DrawCubicBeziers),
				new GalleryScene("Gfx.DrawText (TTF/SDF)", DrawTrueTypeText),
				new GalleryScene("CommandList", DrawCommandList),
				new GalleryScene("DeferredRenderQueue", DrawDeferredRenderQueue),
			};
		}

		private static void DrawCommandList(RenderPass pass, float _, Texture texture)
		{
			if (commandListScene == null)
				commandListScene = CreateCommandListScene(texture);

			pass.Execute(commandListScene);
		}

		private static CommandList CreateCommandListScene(Texture texture)
		{
			CommandList commands = new CommandList();
			RenderState state = Gfx.CreateDefaultRenderState();

			commands.RecordPushRenderState(state);
			commands.RecordDrawText(
				proportionalFont,
				new Vector2(500, 875),
				"Typed commands, recorded once and replayed every frame",
				new Color(110, 205, 255),
				42
			);
			commands.RecordFilledRoundedRectangle(
				new Vector2(500, 575),
				new Vector2(420, 220),
				new CornerRadii(55, 18, 55, 18),
				new Color(60, 115, 205, 210)
			);
			commands.RecordTexturedRoundedRectangle(
				new Vector2(1030, 575),
				new Vector2(420, 220),
				new CornerRadii(48),
				texture,
				Vector2.Zero,
				Vector2.One,
				new Color(235, 245, 255, 225)
			);
			commands.RecordRing(
				new Vector2(610, 330),
				55,
				110,
				-MathF.PI / 5,
				MathF.PI * 1.45f,
				new Color(255, 170, 80, 225)
			);
			commands.RecordCircle(new Vector2(940, 330), 105, 9, new Color(90, 225, 160));
			commands.RecordCubicBezier(
				new Vector2(1120, 260),
				new Vector2(1260, 500),
				new Vector2(1480, 120),
				new Vector2(1660, 365),
				12,
				new Color(225, 125, 245)
			);
			commands.RecordLine(
				new Vertex2(new Vector2(490, 145), new Color(90, 205, 255)),
				new Vertex2(new Vector2(1680, 145), new Color(255, 110, 155)),
				7
			);
			commands.RecordDrawText(
				monoFont,
				new Vector2(500, 95),
				$"{commands.Count + 2} immutable commands | caller-owned resources | execution-time camera",
				new Color(195, 205, 220),
				25
			);
			commands.RecordPopRenderState();

			return commands;
		}

		private static void DrawDeferredRenderQueue(RenderPass pass, float _, Texture __)
		{
			if (deferredQueueScene == null)
				CreateDeferredRenderQueueScene();

			deferredQueueScene.BeginFrame();

			deferredQueueScene.SubmitOpaque(
				deferredOpaqueCommands[0],
				Matrix4x4.CreateTranslation(480, 610, -24),
				sortKey: 2,
				tag: "far"
			);
			deferredQueueScene.SubmitOpaque(
				deferredOpaqueCommands[2],
				Matrix4x4.CreateTranslation(1260, 610, -3),
				sortKey: 1,
				tag: "near"
			);
			deferredQueueScene.SubmitOpaque(
				deferredOpaqueCommands[1],
				Matrix4x4.CreateTranslation(870, 610, -12),
				sortKey: 2,
				tag: "middle"
			);

			deferredQueueScene.SubmitTransparent(
				deferredTransparentCommands[1],
				Matrix4x4.CreateTranslation(1030, 300, -10),
				tag: "middle"
			);
			deferredQueueScene.SubmitTransparent(
				deferredTransparentCommands[2],
				Matrix4x4.CreateTranslation(1150, 300, -3),
				tag: "near"
			);
			deferredQueueScene.SubmitTransparent(
				deferredTransparentCommands[0],
				Matrix4x4.CreateTranslation(910, 300, -20),
				tag: "far"
			);

			RenderBucket overlay = new RenderBucket("Overlay");
			deferredQueueScene.Submit(overlay, deferredOverlayCommands, Matrix4x4.Identity);

			RenderView view = pass.View;
			deferredQueueScene.Execute(
				RenderBucket.Opaque,
				RenderSubmissionComparers.OpaqueFrontToBack(view)
			);
			deferredQueueScene.Execute(
				RenderBucket.Transparent,
				RenderSubmissionComparers.TransparentBackToFront(view)
			);

			foreach (RenderSubmission submission in deferredQueueScene.Query(overlay))
				submission.Execute(pass);
		}

		private static void CreateDeferredRenderQueueScene()
		{
			deferredQueueScene = new DeferredRenderQueue();
			deferredOpaqueCommands = new[]
			{
				CreateDeferredBox(new Color(70, 135, 235), "FAR  depth 24"),
				CreateDeferredBox(new Color(75, 195, 145), "MIDDLE  depth 12"),
				CreateDeferredBox(new Color(235, 145, 75), "NEAR  depth 3"),
			};
			deferredTransparentCommands = new[]
			{
				CreateDeferredCircle(new Color(80, 150, 255, 145)),
				CreateDeferredCircle(new Color(95, 235, 155, 145)),
				CreateDeferredCircle(new Color(255, 105, 145, 145)),
			};
			deferredOverlayCommands = new CommandList();
			deferredOverlayCommands.RecordDrawText(
				proportionalFont,
				new Vector2(480, 900),
				"Deferred entity submissions",
				new Color(110, 205, 255),
				48
			);
			deferredOverlayCommands.RecordDrawText(
				monoFont,
				new Vector2(480, 840),
				"OPAQUE: submitted far / near / middle, sorted front-to-back",
				new Color(205, 215, 230),
				25
			);
			deferredOverlayCommands.RecordDrawText(
				monoFont,
				new Vector2(480, 535),
				"TRANSPARENT: submitted middle / near / far, sorted back-to-front",
				new Color(205, 215, 230),
				25
			);
			deferredOverlayCommands.RecordDrawText(
				monoFont,
				new Vector2(480, 120),
				"Overlay is a custom queried bucket | transforms captured at submission",
				new Color(255, 210, 105),
				24
			);
		}

		private static CommandList CreateDeferredBox(Color color, string label)
		{
			CommandList commands = new CommandList();
			commands.RecordFilledRoundedRectangle(
				Vector2.Zero,
				new Vector2(330, 150),
				new CornerRadii(24),
				color
			);
			commands.RecordRoundedRectangle(
				Vector2.Zero,
				new Vector2(330, 150),
				new CornerRadii(24),
				4,
				new Color(235, 240, 250)
			);
			commands.RecordDrawText(monoFont, new Vector2(22, 58), label, Color.White, 23);

			return commands;
		}

		private static CommandList CreateDeferredCircle(Color color)
		{
			CommandList commands = new CommandList();
			commands.RecordFilledCircle(Vector2.Zero, 145, color);
			commands.RecordCircle(Vector2.Zero, 145, 5, new Color(235, 240, 250, 210));

			return commands;
		}

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
			pass.DrawText(proportionalFont, new Vector2(510, 735), "Sharp from 18 px through 180 px", Color.White, 32);
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
			pass.DrawText(proportionalFont, new Vector2(510, 245), "Aa", new Color(255, 105, 135, 210), 180);
			pass.DrawText(
				proportionalFont,
				new Vector2(830, 275),
				"small SDF text remains smooth",
				new Color(190, 200, 215),
				18
			);
			pass.DrawText(monoFont, new Vector2(830, 220), "fallback: \uD800", new Color(255, 150, 105), 28);
		}

		private static void DrawLines(RenderPass pass, float time, Texture _)
		{
			for (int i = 0; i < 14; i++)
			{
				float y = 170 + i * 58;
				float wave = MathF.Sin(time * 1.5f + i * 0.55f) * 100;
				Color start = new Color((byte)(40 + i * 14), (byte)(220 - i * 8), 255);
				Color end = new Color(255, (byte)(70 + i * 11), (byte)(190 - i * 7));
				pass.Line(
					new Vertex2(new Vector2(130, y), start),
					new Vertex2(new Vector2(1790, y + wave), end),
					2 + i * 1.5f
				);
			}
		}

		private static void DrawRectangles(RenderPass pass, float time, Texture _)
		{
			for (int i = 0; i < 9; i++)
			{
				float inset = i * 45;
				float pulse = MathF.Sin(time * 2 + i * 0.4f) * 10;
				pass.Rectangle(
					160 + inset - pulse,
					160 + inset - pulse,
					1600 - inset * 2 + pulse * 2,
					790 - inset * 2 + pulse * 2,
					2 + i * 2,
					new Color((byte)(60 + i * 20), (byte)(210 - i * 12), (byte)(120 + i * 13))
				);
			}
		}

		private static void DrawFilledRectangles(RenderPass pass, float time, Texture _)
		{
			for (int row = 0; row < 4; row++)
				for (int column = 0; column < 7; column++)
				{
					float pulse = 0.85f + MathF.Sin(time * 2 + row + column * 0.4f) * 0.12f;
					float w = 190 * pulse;
					float h = 145 * pulse;
					float x = 155 + column * 245 + (190 - w) / 2;
					float y = 190 + row * 205 + (145 - h) / 2;
					pass.FilledRectangle(
						x,
						y,
						w,
						h,
						new Color((byte)(45 + column * 28), (byte)(65 + row * 45), (byte)(210 - column * 16), 220)
					);
				}
		}

		private static void DrawRoundedRectangles(RenderPass pass, float time, Texture _)
		{
			for (int row = 0; row < 4; row++)
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
					pass.RoundedRectangle(
						x,
						y,
						280,
						150,
						radii,
						3 + row * 2,
						new Color((byte)(80 + column * 35), (byte)(220 - row * 28), (byte)(125 + row * 25)),
						row == 0 ? 2 : 0
					);
				}
		}

		private static void DrawFilledRoundedRectangles(RenderPass pass, float time, Texture _)
		{
			for (int row = 0; row < 3; row++)
				for (int column = 0; column < 5; column++)
				{
					float x = 410 + column * 290;
					float y = 210 + row * 275;
					float width = 235 + MathF.Sin(time * 1.5f + column) * 22;
					CornerRadii radii =
						row == 0
							? new CornerRadii(20 + column * 18)
							: new CornerRadii(15 + column * 8, 90, 35 + row * 15, column * 12);
					pass.FilledRoundedRectangle(
						x,
						y,
						width,
						190,
						radii,
						new Color((byte)(55 + column * 35), (byte)(90 + row * 55), (byte)(220 - column * 20), 220),
						column == 0 ? 2 : 0
					);
				}
		}

		private static void DrawLineStrips(RenderPass pass, float time, Texture _)
		{
			for (int strip = 0; strip < 6; strip++)
			{
				Vertex2[] points = new Vertex2[18];

				for (int i = 0; i < points.Length; i++)
				{
					float x = 100 + i * 100;
					float y = 220 + strip * 135 + MathF.Sin(time * 2 + i * 0.55f + strip) * 55;
					points[i] = new Vertex2(
						new Vector2(x, y),
						new Color((byte)(60 + i * 9), (byte)(230 - strip * 22), (byte)(100 + strip * 24))
					);
				}

				pass.LineStrip(points, 4 + strip * 2);
			}
		}

		private static void DrawPoints(RenderPass pass, float time, Texture _)
		{
			Vector2 center = new Vector2(Width / 2f, Height / 2f + 40);

			for (int ring = 0; ring < 5; ring++)
			{
				int count = 10 + ring * 6;
				float radius = 100 + ring * 85;

				for (int i = 0; i < count; i++)
				{
					float angle = time * (0.35f + ring * 0.12f) + i * MathF.Tau / count;
					Vector2 position = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
					pass.Point(
						new Vertex2(
							position,
							new Color((byte)(70 + ring * 38), (byte)(230 - ring * 25), (byte)(110 + i * 5))
						),
						9 + ring * 5
					);
				}
			}
		}

		private static void DrawTexturedRectangles(RenderPass pass, float time, Texture texture)
		{
			for (int i = 0; i < 8; i++)
			{
				float x = 135 + (i % 4) * 440;
				float y = 190 + (i / 4) * 390;
				float scale = 0.9f + MathF.Sin(time * 1.5f + i) * 0.08f;
				float w = 350 * scale;
				float h = 280 * scale;
				pass.TexturedRectangle(
					x + (350 - w) / 2,
					y + (280 - h) / 2,
					w,
					h,
					Color: new Color((byte)(255 - i * 12), (byte)(180 + i * 8), 255),
					Texture: texture
				);
			}
		}

		private static void DrawTexturedRoundedRectangles(RenderPass pass, float time, Texture texture)
		{
			for (int row = 0; row < 3; row++)
				for (int column = 0; column < 4; column++)
				{
					float x = 420 + column * 360;
					float y = 190 + row * 285;
					float height = 210 + MathF.Sin(time * 1.7f + row + column) * 25;
					CornerRadii radii =
						column == 3
							? new CornerRadii(180)
							: new CornerRadii(20 + row * 20, 80 - column * 10, 25 + column * 18, row * 12);
					pass.TexturedRoundedRectangle(
						x,
						y,
						300,
						height,
						radii,
						texture,
						Color: new Color((byte)(255 - row * 18), (byte)(205 + column * 10), 255, 225),
						CornerSegments: row == 0 ? 2 : 0
					);
				}
		}

		private static void DrawTexturedCircles(RenderPass pass, float time, Texture texture)
		{
			for (int ring = 0; ring < 4; ring++)
			{
				int count = 6 + ring * 3;

				for (int i = 0; i < count; i++)
				{
					float angle = time * (0.2f + ring * 0.08f) + i * MathF.Tau / count;
					Vector2 center =
						new Vector2(Width / 2f + 120, Height / 2f)
						+ new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (100 + ring * 105);
					pass.TexturedCircle(
						center,
						35 + ring * 10,
						texture,
						Color: new Color((byte)(220 - ring * 15), 255, (byte)(185 + ring * 15), 225),
						Segments: ring == 0 ? 12 : 0
					);
				}
			}
		}

		private static void DrawTexturedEllipses(RenderPass pass, float time, Texture texture)
		{
			for (int row = 0; row < 3; row++)
				for (int column = 0; column < 4; column++)
				{
					Vector2 center = new Vector2(500 + column * 375, 250 + row * 290);
					Vector2 radii = new Vector2(135 + MathF.Sin(time * 1.4f + column) * 25, 55 + row * 25);
					pass.TexturedEllipse(
						center,
						radii,
						texture,
						Color: new Color((byte)(205 + row * 15), (byte)(235 - column * 12), 255, 220),
						Segments: column == 0 ? 16 : 0
					);
				}
		}

		private static void DrawNinePatches(RenderPass pass, float time, Texture texture)
		{
			NinePatchInsets insets = new NinePatchInsets(64);
			pass.TexturedRectangle(90, 650, 256, 256, Texture: texture);
			pass.Rectangle(90, 650, 256, 256, 3, new Color(120, 135, 170));
			pass.NinePatch(410, 690, 430, 190, texture, insets);
			pass.NinePatch(900, 700, 850, 170, texture, insets, new Color(210, 245, 255));
			pass.NinePatch(130, 180, 210, 390, texture, insets, new Color(255, 220, 190));
			pass.NinePatch(410, 190, 380, 380, texture, insets, new Color(210, 255, 215, 225));
			pass.NinePatch(860, 230, 96, 84, texture, insets, new Color(255, 190, 210));

			float animatedWidth = 520 + MathF.Sin(time * 1.8f) * 180;
			float animatedHeight = 220 + MathF.Cos(time * 1.4f) * 70;
			pass.NinePatch(1080, 190, animatedWidth, animatedHeight, texture, insets, new Color(190, 220, 255, 215));
		}

		private static void DrawCircles(RenderPass pass, float time, Texture _)
		{
			for (int row = 0; row < 3; row++)
				for (int column = 0; column < 6; column++)
				{
					float radius = 45 + row * 35 + MathF.Sin(time * 2 + column) * 8;
					Vector2 center = new Vector2(220 + column * 290, 260 + row * 280);
					pass.Circle(
						center,
						radius,
						3 + column * 2,
						new Color((byte)(50 + column * 30), (byte)(225 - row * 35), (byte)(110 + row * 45)),
						column == 0 ? 12 : 0
					);
				}
		}

		private static void DrawFilledCircles(RenderPass pass, float time, Texture _)
		{
			for (int ring = 0; ring < 4; ring++)
			{
				int count = 7 + ring * 3;

				for (int i = 0; i < count; i++)
				{
					float angle = time * (0.25f + ring * 0.08f) + i * MathF.Tau / count;
					Vector2 center =
						new Vector2(Width / 2f, Height / 2f + 40)
						+ new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (100 + ring * 115);
					pass.FilledCircle(
						center,
						24 + ring * 11,
						new Color((byte)(70 + ring * 42), (byte)(210 - ring * 24), (byte)(120 + i * 7), 215)
					);
				}
			}
		}

		private static void DrawRings(RenderPass pass, float time, Texture _)
		{
			Vector2 fullCenter = new Vector2(650, 560);

			for (int i = 0; i < 5; i++)
				pass.Ring(
					fullCenter,
					45 + i * 38,
					70 + i * 38,
					new Color((byte)(70 + i * 34), (byte)(225 - i * 18), (byte)(115 + i * 24), 215)
				);

			float animatedSweep = 0.2f + (MathF.Sin(time * 1.4f) * 0.5f + 0.5f) * (MathF.Tau - 0.2f);
			pass.Ring(
				new Vector2(1250, 650),
				85,
				220,
				-MathF.PI / 4,
				-MathF.PI / 4 + animatedSweep,
				new Color(80, 175, 255, 220)
			);

			pass.Ring(new Vector2(1180, 260), 0, 150, MathF.PI * 1.5f, MathF.PI * 2.5f, new Color(255, 145, 80, 215), 6);
			pass.Ring(new Vector2(1600, 300), 125, 145, 0, MathF.PI * 1.35f, new Color(215, 95, 235, 225));
		}

		private static void DrawRingLines(RenderPass pass, float time, Texture _)
		{
			Vector2 fullCenter = new Vector2(680, 560);

			for (int i = 0; i < 5; i++)
				pass.RingLines(
					fullCenter,
					35 + i * 42,
					70 + i * 42,
					3 + i * 2,
					new Color((byte)(75 + i * 32), (byte)(220 - i * 16), (byte)(125 + i * 23))
				);

			float start = time * 0.45f;
			float sweep = MathF.PI * 1.45f;
			pass.RingLines(new Vector2(1280, 660), 90, 230, start, start + sweep, 12, new Color(90, 190, 255), 0);

			pass.RingLines(new Vector2(1210, 250), 0, 145, MathF.PI, MathF.PI * 1.8f, 7, new Color(255, 170, 75), 5);
			pass.RingLines(new Vector2(1620, 290), 130, 130, 0, MathF.Tau, 10, new Color(225, 105, 240));
		}

		private static void DrawEllipses(RenderPass pass, float time, Texture _)
		{
			for (int row = 0; row < 3; row++)
				for (int column = 0; column < 5; column++)
				{
					Vector2 center = new Vector2(260 + column * 350, 270 + row * 285);
					Vector2 radii = new Vector2(120 + MathF.Sin(time + column) * 25, 45 + row * 28);
					pass.Ellipse(
						center,
						radii,
						4 + row * 4,
						new Color((byte)(80 + column * 30), (byte)(215 - row * 30), (byte)(230 - column * 20)),
						column == 0 ? 16 : 0
					);
				}
		}

		private static void DrawFilledEllipses(RenderPass pass, float time, Texture _)
		{
			for (int i = 0; i < 12; i++)
			{
				float angle = i * MathF.Tau / 12 + time * 0.2f;
				Vector2 center =
					new Vector2(Width / 2f, Height / 2f + 30)
					+ new Vector2(MathF.Cos(angle) * 570, MathF.Sin(angle) * 330);
				Vector2 radii = new Vector2(125 + i * 4, 35 + i * 2);
				pass.FilledEllipse(
					center,
					radii,
					new Color((byte)(60 + i * 15), (byte)(220 - i * 9), (byte)(115 + i * 10), 210)
				);
			}
		}

		private static void DrawQuadraticBeziers(RenderPass pass, float time, Texture _)
		{
			for (int i = 0; i < 10; i++)
			{
				float y = 180 + i * 85;
				Vector2 start = new Vector2(120, y);
				Vector2 control = new Vector2(Width / 2f, y + MathF.Sin(time * 1.8f + i * 0.5f) * 260);
				Vector2 end = new Vector2(1800, y);
				pass.QuadraticBezier(
					start,
					control,
					end,
					3 + i,
					new Color((byte)(55 + i * 19), (byte)(225 - i * 10), (byte)(120 + i * 12)),
					i == 0 ? 8 : 0
				);
			}
		}

		private static void DrawCubicBeziers(RenderPass pass, float time, Texture _)
		{
			for (int i = 0; i < 9; i++)
			{
				float y = 190 + i * 95;
				float motion = MathF.Sin(time * 1.5f + i * 0.4f) * 120;
				pass.CubicBezier(
					new Vector2(110, y),
					new Vector2(620, y + 260 + motion),
					new Vector2(1300, y - 260 - motion),
					new Vector2(1810, y),
					4 + i,
					new Color((byte)(75 + i * 18), (byte)(115 + i * 12), (byte)(245 - i * 12)),
					i == 0 ? 12 : 0
				);
			}
		}
	}
}
