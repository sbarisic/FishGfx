using FishGfx;
using FishGfx.Formats;
using FishGfx.Game;
using FishGfx.Graphics;
using System;
using System.Diagnostics;
using System.Linq;
using System.Numerics;

namespace FishGfx.SmokeTest {
	internal sealed class PrimitiveGallery {
		internal const int Width = 1920;
		internal const int Height = 1080;

		private readonly bool autoMode;
		private RenderWindow window;
		private GalleryScene[] scenes;
		private int sceneIndex;

		internal int SceneIndex => sceneIndex;

		internal PrimitiveGallery(string[] args) {
			autoMode = args.Contains("--auto", StringComparer.OrdinalIgnoreCase);
		}

		internal void Run() {
			window = new RenderWindow(Width, Height, "FishGfx Primitive Scene Gallery");
			InputManager input = new InputManager(window);
			Texture texture = LoadGridTexture();
			BMFont titleFont = new BMFont("data/fonts/proggy.fnt", 42);
			scenes = PrimitiveScenes.Create();
			GalleryConsole galleryConsole = new GalleryConsole(window, scenes, this);

			ShaderUniforms.Current.Camera.SetOrthogonal(0, 0, Width, Height);
			ShaderUniforms.Current.Resolution = new Vector2(Width, Height);
			ShaderUniforms.Current.TextureSize = texture.Size;

			sceneIndex = 0;
			int lastAutoScene = -1;
			Stopwatch runtime = Stopwatch.StartNew();

			while (!window.ShouldClose) {
				input.BeginNewFrame();
				Events.Poll();

				if (galleryConsole.IsOpen) {
					if (input.GetKeyPressed(Key.Escape))
						galleryConsole.Close();
				} else {
					if (input.GetKeyPressed(Key.Space))
						NextScene();
					if (input.GetKeyPressed(Key.Backspace))
						PreviousScene();
					if (input.GetKeyPressed(Key.Escape))
						RequestClose();
				}

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
				SceneMenu.Draw(titleFont, scenes, sceneIndex);
				galleryConsole.Draw();
				window.SwapBuffers();
			}

			galleryConsole.Dispose();
			foreach (Texture fontTexture in titleFont.PageNames.Values)
				fontTexture.Dispose();
			texture.Dispose();
			RenderAPI.CollectGarbage();
			window.Close();
			Console.WriteLine($"Primitive gallery completed using {RenderAPI.Renderer}");
		}

		internal void SelectScene(int index) {
			sceneIndex = ((index % scenes.Length) + scenes.Length) % scenes.Length;
		}

		internal void NextScene() => SelectScene(sceneIndex + 1);

		internal void PreviousScene() => SelectScene(sceneIndex - 1);

		internal void RequestClose() => window.ShouldClose = true;

		private static Texture LoadGridTexture() {
			Texture texture = Texture.FromFile("data/textures/grid.png");
			texture.SetFilter(TextureFilter.Nearest);
			return texture;
		}
	}
}
