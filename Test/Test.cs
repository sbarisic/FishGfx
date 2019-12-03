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
		DevConsole Con;

		GameLevel Lvl;
		List<Entity> Entities = new List<Entity>();

		//Sprite PlayerSprite;
		//SpriteAnimator PlayerAnimator;

		protected override RenderWindow CreateWindow() {
			return new RenderWindow(800, 600, "Test");
		}

		protected override void Init() {
			//TestFont = new BMFont("data/fonts/proggy.fnt");

			int FontSize = 8;
			int W = (int)Math.Floor((float)Window.WindowWidth / FontSize);
			int H = (int)((Window.WindowHeight * 0.4f) / FontSize);

			Con = new DevConsole(Texture.FromFile("data/fonts/tileset/cheepicus8.png"), FontSize, W, H, H * 2, DefaultShader);
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
			Con.Enabled = false;

			Window.OnChar += (Wnd, Chr, Uni) => Con.SendInput(Chr);
			Window.OnKey += Con.SendKey;

			RenderState RS = Gfx.CreateDefaultRenderState();
			RS.EnableDepthTest = false;
			RS.EnableCullFace = false;
			Gfx.PushRenderState(RS);


			// Load the level
			Lvl = GameLevel.FromFile("levels/rm_level0.json");
			Lvl.Init(DefaultShader);
			foreach (var E in Lvl.GetAllEntities())
				Spawn(E);

			// Spawn player
			Player Ply = new Player(this, DefaultShader);
			Ply.Position = Lvl.GetEntitiesByName("spawn_player").First().Position + Ply.Size * new Vector2(0.5f, -1);
			Spawn(Ply);
		}

		public void Spawn(Entity Ent) {
			Entities.Add(Ent);
		}

		Vector2 LastMoveDirection;

		protected override void Update(float Dt) {
			if (Input.GetKeyPressed(Key.Escape))
				Window.Close();

			foreach (var Ent in Entities)
				Ent.Update(Dt, GameTime);

			/*Key KeyMoveLeft = Key.A;
			Key KeyMoveRight = Key.D;
			Vector2 MoveDirection = new Vector2(0, 0);

			PlayerSprite.Position = Input.GetMousePos() * new Vector2(1, -1) + Window.WindowSize * new Vector2(0, 1);

			if (Input.GetKeyDown(Key.A)) {
				PlayerAnimator.Play("walk_left", false);
				MoveDirection = new Vector2(-1, 0);
			}

			if (Input.GetKeyDown(Key.D)) {
				PlayerAnimator.Play("walk_right", false);
				MoveDirection = new Vector2(1, 0);
			}

			if (MoveDirection.Length() == 0) {
				if (LastMoveDirection.X < 0)
					PlayerAnimator.Play("stand_left");
				else
					PlayerAnimator.Play("stand_right");
			} else
				LastMoveDirection = MoveDirection;*/
		}

		protected override void Draw(float Dt) {
			Gfx.Clear(Color.Sky);
			Lvl.DrawBackground();

			foreach (var Ent in Entities)
				Ent.Draw();

			Lvl.DrawForeground();
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
