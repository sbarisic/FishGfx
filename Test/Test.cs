using FishGfx;
using FishGfx.Formats;
using FishGfx.Graphics;
using FishGfx.Graphics.Drawables;
using System;
using System.Collections.Generic;
using System.Diagnostics;
//using System.Drawing;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using FishGfx.Game;

namespace Test {
	class TestGame : FishGfxGame {
		protected override RenderWindow CreateWindow() {
			return new RenderWindow(800, 600, "Test");
		}

		DevConsole Con;

		BMFont TestFont;

		protected override void Init() {
			/*TestSprite = new Sprite(Texture.FromFile("data/textures/test16.png"));
			TestSprite.Position = new Vector2(0, 0);
			TestSprite.Scale = new Vector2(16, 16);
			TestSprite.Center = TestSprite.Scale / 2;
			TestSprite.Shader = DefaultShader;

			Map = new Tilemap(16, 15, 15, Texture.FromFile("data/textures/tileset/test.png"));
			Map.Shader = DefaultShader;
			Map.Position = new Vector2(100, 100);
			Map.Scale = new Vector2(2, 2);

			Map.ClearTiles(68);
			Map.SetTile(0, 0, 0);
			Map.SetTile(1, 1, 1);
			Map.SetTile(2, 2, 2);
			Map.SetTile(3, 3, 3);*/

			//int W = 80;
			//int H = 40;
			//Con = new DevConsole(Texture.FromFile("data/fonts/tileset/cheepicus8.png"), 8, W, H, H * 2, DefaultShader);

			//TestFont = new BMFont("data/fonts/proggy.fnt");

			int W = 65;
			int H = 20;
			Con = new DevConsole(Texture.FromFile("data/fonts/tileset/nicecurses12.png"), 12, W, H, H * 2, DefaultShader);
			Con.Position = new Vector2(0, Window.WindowHeight - H * Con.CharSize);

			Con.OnInput += (In) => {
				if (In.Length == 0) {
					Con.PrintLine();
					return;
				}

				if (In.StartsWith("rainbow")) {
					foreach (var C in In.Substring(7).Trim()) {
						Con.TextColor = GfxUtils.RandomColor();
						Con.PutChar(C);
					}

					Con.TextColor = Color.White;
					Con.PrintLine();
					return;
				}

				Con.PrintLine("You wrote '{0}'", In);
			};

			Con.PrintLine("Welcome to the Developer Console");
			Con.BeginInput();

			Window.OnChar += (Wnd, Chr, Uni) => {
				Con.SendInput(Chr);
			};

			Window.OnKey += (Wnd, Key, Scancode, Pressed, Repeat, Mods) => {
				if (!Pressed)
					return;

				if (Key == Key.Enter || Key == Key.NumpadEnter)
					Con.PutChar('\n');

				if (Key == Key.Backspace)
					Con.PutChar('\b');

				if (Key == Key.Up)
					Con.SetViewScroll(Con.GetViewScroll() + 1);

				if (Key == Key.Down)
					Con.SetViewScroll(Con.GetViewScroll() - 1);
			};

			RenderState RS = Gfx.CreateDefaultRenderState();
			RS.EnableDepthTest = false;
			Gfx.PushRenderState(RS);
		}

		protected override void Update(float Dt) {
			if (Input.GetKeyPressed(Key.Escape))
				Window.Close();
		}

		protected override void Draw(float Dt) {
			Gfx.Clear(Color.Sky);

			Gfx.FilledRectangle(Con.Position.X, Con.Position.Y, Con.CharSize * Con.Width, Con.CharSize * Con.Height, Color.Coal);
			Con.Draw();

			//Gfx.DrawText(TestFont, new Vector2(100, 50), "The quick, brown fox! Hello. Hello?", Color.White, 32);
		}
	}

	class Program {
		static void Main(string[] args) {
			FishGfxGame.Run(new TestGame());
			//Run();
		}

		static RenderWindow Window;

		static void Run() {
			Vector2 Size = RenderWindow.GetDesktopResolution() * 0.9f;
			Window = new RenderWindow((int)Size.X, (int)Size.Y, "FishGfx Test");

#if DEBUG
			Console.WriteLine("Running {0}", RenderAPI.Version);
			Console.WriteLine(RenderAPI.Renderer);
			//File.WriteAllLines("gl_extensions.txt", RenderAPI.Extensions);
#endif

			Window.CaptureCursor = false;
			Window.OnMouseMoveDelta += (Wnd, X, Y) => {
			};

			Window.OnMouseMove += (Wnd, X, Y) => {
			};

			Window.OnKey += (RenderWindow Wnd, Key Key, int Scancode, bool Pressed, bool Repeat, KeyMods Mods) => {
				if (Key == Key.Escape && Pressed)
					Environment.Exit(0);
			};

			ShaderProgram Default = new ShaderProgram(new ShaderStage(ShaderType.VertexShader, "data/default3d.vert"),
				new ShaderStage(ShaderType.FragmentShader, "data/defaultFlatColor.frag"));

			Stopwatch SWatch = Stopwatch.StartNew();
			float Dt = 0;

			{
				RenderState RS = Gfx.CreateDefaultRenderState();
				RS.EnableDepthTest = false;
				Gfx.PushRenderState(RS);
			}

			ShaderUniforms U = ShaderUniforms.Current;
			U.Camera.SetOrthogonal(0, 0, Window.WindowWidth, Window.WindowHeight);

			GfxFont Fnt = new BMFont("data/fonts/proggy.fnt");


			while (!Window.ShouldClose) {
				while (SWatch.ElapsedMilliseconds / 1000.0f < (1.0f / 60))
					;

				Dt = SWatch.ElapsedMilliseconds / 1000.0f;
				SWatch.Restart();

				Gfx.Clear();

				//*
				{
					for (int i = 0; i < 30; i++) {
						Gfx.DrawText(Fnt, new Vector2(0, i * Fnt.LineHeight), "Hello World!", FishGfx.Color.White);
					}

					Gfx.Rectangle(300, 100, 100, 100);
				}
				//*/

				/*Gfx.Line(new Vertex2(25, 10), new Vertex2(25, 100));
				Gfx.Rectangle(50, 10, 100, 100);
				Gfx.FilledRectangle(200, 10, 100, 100);

				Gfx.TexturedRectangle(350, 10, 100, 100, Texture: Test);
				Gfx.Rectangle(350, 10, 100, 100, Clr: Color.Red);*/

				// Update
				{

				}
				Window.SwapBuffers();
				Events.Poll();
			}
		}
	}
}
