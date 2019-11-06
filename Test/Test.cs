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

		//Sprite TestSprite;
		Tilemap Map;

		protected override void Init() {
			/*TestSprite = new Sprite(Texture.FromFile("data/textures/test16.png"));
			TestSprite.Position = new Vector2(0, 0);
			TestSprite.Size = new Vector2(32, 32);
			TestSprite.Shader = DefaultShader;*/

			Map = new Tilemap(16, 10, 10);
			Map.Shader = DefaultShader;
			Map.TileAtlas = Texture.FromFile("data/textures/tileset/test.png");
			Map.SetTile(1, 0, 0);
			Map.SetTile(2, 1, 0);
			Map.SetTile(3, 0, 1);
			Map.SetTile(4, 2, 2);
		}

		protected override void Update(float Dt) {
			if (Input.GetKeyPressed(Key.Escape))
				Window.Close();

			const float MoveSpeed = 100;

			/*if (Input.GetKeyDown(Key.W)) {
				TestSprite.Position += new Vector2(0, MoveSpeed) * Dt;
			}
			if (Input.GetKeyDown(Key.A)) {
				TestSprite.Position += new Vector2(-MoveSpeed, 0) * Dt;
			}
			if (Input.GetKeyDown(Key.S)) {
				TestSprite.Position += new Vector2(0, -MoveSpeed) * Dt;
			}
			if (Input.GetKeyDown(Key.D)) {
				TestSprite.Position += new Vector2(MoveSpeed, 0) * Dt;
			}*/
		}

		protected override void Draw(float Dt) {
			Gfx.Clear(Color.Sky);
			//TestSprite.Draw();
			Map.Draw();
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
