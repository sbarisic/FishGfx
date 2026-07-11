using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using FishGfx;
using FishGfx.Formats;
using FishGfx.Game;
using FishGfx.Graphics;

namespace FishGfx.SmokeTest
{
	internal sealed class PrimitiveGallery
	{
		internal const int Width = 1920;
		internal const int Height = 1080;
		private const double AutoSceneDuration = 1.0;
		private const float AutoRenderTime = 0.5f;

		private readonly bool autoMode;
		private RenderWindow window;
		private GalleryScene[] scenes;
		private int sceneIndex;

		internal int SceneIndex => sceneIndex;

		internal PrimitiveGallery(string[] args)
		{
			autoMode = args.Contains("--auto", StringComparer.OrdinalIgnoreCase);
		}

		internal void Run()
		{
			window = new RenderWindow(Width, Height, "FishGfx Primitive Scene Gallery");
			InputManager input = new InputManager(window);
			Texture texture = LoadGridTexture();
			TTFFont titleFont = new TTFFont(AssetPath("fonts", "Aaargh.ttf"));
			PrimitiveScenes.InitializeFonts();
			scenes = PrimitiveScenes.Create();
			GalleryConsole galleryConsole = new GalleryConsole(window, scenes, this);
			string screenshotDirectory = null;
			string[] screenshotFileNames = null;

			if (autoMode)
			{
				screenshotDirectory = GalleryScreenshot.FindPicturesDirectory();
				screenshotFileNames = GalleryScreenshot.FileNamesForTitles(scenes.Select(scene => scene.Title));
				Directory.CreateDirectory(screenshotDirectory);
			}

			ShaderUniforms.Current.Camera.SetOrthogonal(0, 0, Width, Height);
			ShaderUniforms.Current.Resolution = new Vector2(Width, Height);
			ShaderUniforms.Current.TextureSize = texture.Size;

			sceneIndex = 0;
			double autoSceneStartedAt = 0;
			int autoFramesRendered = 0;
			bool autoSceneCaptured = false;
			Stopwatch runtime = Stopwatch.StartNew();

			while (!window.ShouldClose)
			{
				input.BeginNewFrame();
				Events.Poll();
				double elapsedSeconds = runtime.Elapsed.TotalSeconds;

				if (autoMode)
				{
					galleryConsole.Close();

					if (input.GetKeyPressed(Key.Escape))
						RequestClose();

					if (autoSceneCaptured && elapsedSeconds - autoSceneStartedAt >= AutoSceneDuration)
					{
						if (sceneIndex == scenes.Length - 1)
						{
							RequestClose();
						}
						else
						{
							sceneIndex++;
							autoSceneStartedAt = elapsedSeconds;
							autoFramesRendered = 0;
							autoSceneCaptured = false;
						}
					}
				}
				else if (galleryConsole.IsOpen)
				{
					if (input.GetKeyPressed(Key.Escape))
						galleryConsole.Close();
				}
				else
				{
					if (input.GetKeyPressed(Key.Space))
						NextScene();
					if (input.GetKeyPressed(Key.Backspace))
						PreviousScene();
					if (input.GetKeyPressed(Key.Escape))
						RequestClose();
				}

				if (window.ShouldClose)
					break;

				Gfx.Clear(new Color(18, 23, 36));
				scenes[sceneIndex].Draw(autoMode ? AutoRenderTime : (float)elapsedSeconds, texture);
				SceneMenu.Draw(titleFont, scenes, sceneIndex);
				galleryConsole.Draw();

				if (autoMode)
				{
					autoFramesRendered++;

					if (autoFramesRendered == 2)
					{
						GalleryScreenshot.Capture(
							window,
							screenshotDirectory,
							screenshotFileNames[sceneIndex]
						);
						autoSceneCaptured = true;
						Console.WriteLine(
							$"Captured {scenes[sceneIndex].Title} to {screenshotFileNames[sceneIndex]}"
						);
					}
				}

				window.SwapBuffers();
			}

			galleryConsole.Dispose();
			PrimitiveScenes.DisposeFonts();
			titleFont.Dispose();
			texture.Dispose();
			RenderAPI.CollectGarbage();
			window.Close();
			Console.WriteLine($"Primitive gallery completed using {RenderAPI.Renderer}");
		}

		internal void SelectScene(int index)
		{
			sceneIndex = ((index % scenes.Length) + scenes.Length) % scenes.Length;
		}

		internal void NextScene() => SelectScene(sceneIndex + 1);

		internal void PreviousScene() => SelectScene(sceneIndex - 1);

		internal void RequestClose() => window.ShouldClose = true;

		private static Texture LoadGridTexture()
		{
			Texture texture = Texture.FromFile(AssetPath("textures", "grid.png"));
			texture.SetFilter(TextureFilter.Nearest);
			return texture;
		}

		internal static string AssetPath(params string[] parts) =>
			Path.Combine(new[] { AppContext.BaseDirectory, "data" }.Concat(parts).ToArray());
	}
}
