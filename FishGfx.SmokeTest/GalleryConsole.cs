using System;
using System.Globalization;
using System.IO;
using System.Numerics;
using FishGfx;
using FishGfx.Formats;
using FishGfx.Game;
using FishGfx.Graphics;

namespace FishGfx.SmokeTest
{
	internal sealed class GalleryConsole : IDisposable
	{
		private const int FontSize = 16;
		private const int Columns = 120;
		private const int VisibleRows = 24;
		private const int BufferRows = 96;

		private readonly RenderWindow window;
		private readonly GalleryScene[] scenes;
		private readonly PrimitiveGallery gallery;
		private readonly TTFFont font;
		private readonly DevConsole console;
		private readonly OnCharFunc charHandler;
		private bool disposed;

		internal bool IsOpen => console.Enabled;

		internal GalleryConsole(RenderWindow window, GalleryScene[] scenes, PrimitiveGallery gallery)
		{
			this.window = window;
			this.scenes = scenes;
			this.gallery = gallery;

			font = new TTFFont(PrimitiveGallery.AssetPath("fonts", "Consolas-Regular.ttf"));
			console = new DevConsole(font, FontSize, Columns, VisibleRows, BufferRows)
			{
				Position = Vector2.Zero,
				BackgroundColor = new Color(Color.Coal.R, Color.Coal.G, Color.Coal.B, 204),
				Enabled = false,
			};

			console.OnInput += Execute;
			charHandler = (sender, text, unicode) => console.SendInput(text);
			window.OnChar += charHandler;
			window.OnKey += console.SendKey;

			console.PrintLine("FishGfx developer console");
			console.PrintLine("F1 toggles the console; type 'help' for commands.");
			console.BeginInput();
		}

		internal void Draw(RenderPass pass)
		{
			if (!console.Enabled)
				return;

			RenderState overlayState = Gfx.CreateDefaultRenderState();
			overlayState.EnableDepthTest = false;
			overlayState.EnableDepthMask = false;
			overlayState.EnableCullFace = false;
			using (pass.PushState(overlayState))
				console.Draw();
		}

		internal void Close() => console.Enabled = false;

		private void Execute(string input)
		{
			string commandLine = input.Trim();

			if (commandLine.Length == 0)
				return;

			int separator = commandLine.IndexOf(' ');
			string command = (separator < 0 ? commandLine : commandLine.Substring(0, separator)).ToLowerInvariant();
			string argument = separator < 0 ? "" : commandLine.Substring(separator + 1).Trim();

			switch (command)
			{
				case "help":
					PrintHelp();
					break;
				case "scenes":
					PrintScenes();
					break;
				case "scene":
					SelectScene(argument);
					break;
				case "next":
					gallery.NextScene();
					PrintSelection();
					break;
				case "previous":
				case "prev":
					gallery.PreviousScene();
					PrintSelection();
					break;
				case "renderer":
					console.PrintLine(RenderAPI.Renderer);
					console.PrintLine("OpenGL context: {0}", RenderAPI.Version);
					break;
				case "quit":
					console.PrintLine("Closing gallery.");
					gallery.RequestClose();
					break;
				default:
					console.PrintLine("Unknown command '{0}'. Type 'help'.", command);
					break;
			}
		}

		private void PrintHelp()
		{
			console.PrintLine("help                 show commands");
			console.PrintLine("scenes               list scenes");
			console.PrintLine("scene <index|name>   select scene");
			console.PrintLine("next / prev          change scene");
			console.PrintLine("renderer             show GPU and OpenGL");
			console.PrintLine("quit                 close gallery");
		}

		private void PrintScenes()
		{
			for (int i = 0; i < scenes.Length; i++)
				console.PrintLine("{0,2}. {1}", i + 1, scenes[i].Title);
		}

		private void SelectScene(string argument)
		{
			if (argument.Length == 0)
			{
				console.PrintLine("Usage: scene <index|name>");
				return;
			}

			if (int.TryParse(argument, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
			{
				if (number < 1 || number > scenes.Length)
				{
					console.PrintLine("Scene index must be between 1 and {0}.", scenes.Length);
					return;
				}

				gallery.SelectScene(number - 1);
				PrintSelection();
				return;
			}

			string requested = NormalizeSceneName(argument);

			for (int i = 0; i < scenes.Length; i++)
				if (NormalizeSceneName(scenes[i].Title) == requested)
				{
					gallery.SelectScene(i);
					PrintSelection();
					return;
				}

			console.PrintLine("Unknown scene '{0}'. Type 'scenes'.", argument);
		}

		private void PrintSelection()
		{
			console.PrintLine("Selected {0}: {1}", gallery.SceneIndex + 1, scenes[gallery.SceneIndex].Title);
		}

		private static string NormalizeSceneName(string name)
		{
			string normalized = name.Trim();

			if (normalized.StartsWith("Gfx.", StringComparison.OrdinalIgnoreCase))
				normalized = normalized.Substring(4);
			return normalized.ToLowerInvariant();
		}

		public void Dispose()
		{
			if (disposed)
				return;
			disposed = true;
			window.OnChar -= charHandler;
			window.OnKey -= console.SendKey;
			console.OnInput -= Execute;
			font.Dispose();
		}
	}
}
