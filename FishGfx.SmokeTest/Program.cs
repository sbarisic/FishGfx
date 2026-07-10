using FishGfx;
using FishGfx.Formats;
using FishGfx.Game;
using FishGfx.Graphics;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Numerics;

namespace FishGfx.SmokeTest {
	internal static class Program {
		private const int Width = 1920;
		private const int Height = 1080;

		private sealed class Scene {
			public string Title { get; }
			public Action<float, Texture> Draw { get; }

			public Scene(string title, Action<float, Texture> draw) {
				Title = title;
				Draw = draw;
			}
		}

		private static void Main(string[] args) {
			bool autoMode = args.Contains("--auto", StringComparer.OrdinalIgnoreCase);
			RenderWindow window = new RenderWindow(Width, Height, "FishGfx Primitive Scene Gallery");
			InputManager input = new InputManager(window);
			Texture texture = CreateCheckerTexture();
			BMFont titleFont = new BMFont("data/fonts/proggy.fnt", 42);
			Scene[] scenes = CreateScenes();

			ShaderUniforms.Current.Camera.SetOrthogonal(0, 0, Width, Height);
			ShaderUniforms.Current.Resolution = new Vector2(Width, Height);
			ShaderUniforms.Current.TextureSize = texture.Size;

			int sceneIndex = 0;
			int lastAutoScene = -1;
			Stopwatch runtime = Stopwatch.StartNew();

			while (!window.ShouldClose) {
				input.BeginNewFrame();
				Events.Poll();

				if (input.GetKeyPressed(Key.Space))
					sceneIndex = (sceneIndex + 1) % scenes.Length;
				if (input.GetKeyPressed(Key.Backspace))
					sceneIndex = (sceneIndex + scenes.Length - 1) % scenes.Length;
				if (input.GetKeyPressed(Key.Escape))
					window.ShouldClose = true;

				if (autoMode) {
					int autoScene = (int)runtime.Elapsed.TotalSeconds;
					if (autoScene != lastAutoScene) {
						lastAutoScene = autoScene;
						sceneIndex = Math.Min(autoScene, scenes.Length - 1);
					}
					if (autoScene >= scenes.Length)
						window.ShouldClose = true;
				}

				Gfx.Clear(new Color(18, 23, 36));
				scenes[sceneIndex].Draw((float)runtime.Elapsed.TotalSeconds, texture);
				DrawTitle(titleFont, scenes[sceneIndex].Title, sceneIndex, scenes.Length);
				window.SwapBuffers();
			}

			foreach (Texture fontTexture in titleFont.PageNames.Values)
				fontTexture.Dispose();
			texture.Dispose();
			RenderAPI.CollectGarbage();
			window.Close();
			Console.WriteLine($"Primitive gallery completed using {RenderAPI.Renderer}");
		}

		private static Texture CreateCheckerTexture() {
			using Bitmap bitmap = new Bitmap(2, 2);
			bitmap.SetPixel(0, 0, System.Drawing.Color.CornflowerBlue);
			bitmap.SetPixel(1, 0, System.Drawing.Color.Gold);
			bitmap.SetPixel(0, 1, System.Drawing.Color.OrangeRed);
			bitmap.SetPixel(1, 1, System.Drawing.Color.White);
			return Texture.FromImage(bitmap);
		}

		private static Scene[] CreateScenes() {
			return new[] {
				new Scene("Gfx.Line", DrawLines),
				new Scene("Gfx.Rectangle", DrawRectangles),
				new Scene("Gfx.FilledRectangle", DrawFilledRectangles),
				new Scene("Gfx.LineStrip", DrawLineStrips),
				new Scene("Gfx.Point", DrawPoints),
				new Scene("Gfx.TexturedRectangle", DrawTexturedRectangles)
			};
		}

		private static void DrawTitle(BMFont font, string title, int index, int count) {
			string text = $"{title}   [{index + 1}/{count}]   SPACE: next   BACKSPACE: previous   ESC: quit";
			Gfx.DrawText(font, new Vector2(55, 55), text, Color.White, 42);
		}

		private static void DrawLines(float time, Texture _) {
			for (int i = 0; i < 14; i++) {
				float y = 170 + i * 58;
				float wave = MathF.Sin(time * 1.5f + i * 0.55f) * 100;
				Color start = new Color((byte)(40 + i * 14), (byte)(220 - i * 8), 255);
				Color end = new Color(255, (byte)(70 + i * 11), (byte)(190 - i * 7));
				Gfx.Line(new Vertex2(new Vector2(130, y), start),
					new Vertex2(new Vector2(1790, y + wave), end), 2 + i * 1.5f);
			}
		}

		private static void DrawRectangles(float time, Texture _) {
			for (int i = 0; i < 9; i++) {
				float inset = i * 45;
				float pulse = MathF.Sin(time * 2 + i * 0.4f) * 10;
				Gfx.Rectangle(160 + inset - pulse, 160 + inset - pulse,
					1600 - inset * 2 + pulse * 2, 790 - inset * 2 + pulse * 2,
					2 + i * 2, new Color((byte)(60 + i * 20), (byte)(210 - i * 12), (byte)(120 + i * 13)));
			}
		}

		private static void DrawFilledRectangles(float time, Texture _) {
			for (int row = 0; row < 4; row++) {
				for (int column = 0; column < 7; column++) {
					float pulse = 0.85f + MathF.Sin(time * 2 + row + column * 0.4f) * 0.12f;
					float w = 190 * pulse;
					float h = 145 * pulse;
					float x = 155 + column * 245 + (190 - w) / 2;
					float y = 190 + row * 205 + (145 - h) / 2;
					Gfx.FilledRectangle(x, y, w, h,
						new Color((byte)(45 + column * 28), (byte)(65 + row * 45), (byte)(210 - column * 16), 220));
				}
			}
		}

		private static void DrawLineStrips(float time, Texture _) {
			for (int strip = 0; strip < 6; strip++) {
				Vertex2[] points = new Vertex2[18];
				for (int i = 0; i < points.Length; i++) {
					float x = 100 + i * 100;
					float y = 220 + strip * 135 + MathF.Sin(time * 2 + i * 0.55f + strip) * 55;
					points[i] = new Vertex2(new Vector2(x, y),
						new Color((byte)(60 + i * 9), (byte)(230 - strip * 22), (byte)(100 + strip * 24)));
				}
				Gfx.LineStrip(points, 4 + strip * 2);
			}
		}

		private static void DrawPoints(float time, Texture _) {
			Vector2 center = new Vector2(Width / 2f, Height / 2f + 40);
			for (int ring = 0; ring < 5; ring++) {
				int count = 10 + ring * 6;
				float radius = 100 + ring * 85;
				for (int i = 0; i < count; i++) {
					float angle = time * (0.35f + ring * 0.12f) + i * MathF.Tau / count;
					Vector2 position = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
					Gfx.Point(new Vertex2(position,
						new Color((byte)(70 + ring * 38), (byte)(230 - ring * 25), (byte)(110 + i * 5))), 9 + ring * 5);
				}
			}
		}

		private static void DrawTexturedRectangles(float time, Texture texture) {
			for (int i = 0; i < 8; i++) {
				float x = 135 + (i % 4) * 440;
				float y = 190 + (i / 4) * 390;
				float scale = 0.9f + MathF.Sin(time * 1.5f + i) * 0.08f;
				float w = 350 * scale;
				float h = 280 * scale;
				Gfx.TexturedRectangle(x + (350 - w) / 2, y + (280 - h) / 2, w, h,
					Color: new Color((byte)(255 - i * 12), (byte)(180 + i * 8), 255), Texture: texture);
			}
		}
	}
}
