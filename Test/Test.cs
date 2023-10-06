﻿using FishGfx;
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
using Humper;
using FishGfx.AdvGraphics;

namespace Test {
	[Flags]
	enum PhysicsTags {
		None = 0,
		Solid = 1 << 0,
		Pawn = 1 << 1,
		Spike = 1 << 2,
		Hazard = 1 << 3,
	}

	class TestGame : FishGfxGame {
		public DevConsole Con;

		public World PhysWorld;
		public GameLevel Lvl;

		public ParallaxSprite Background;
		public ParticleSystem Particles;

		List<Entity> Entities = new List<Entity>();


		protected override RenderWindow CreateWindow() {
			return new RenderWindow(1920, 1080, "Test");
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

				if (In == "respawn") {
					Player Ply = Entities.Where(Ent => Ent is Player).FirstOrDefault() as Player;
					Ply.Respawn(Lvl.GetEntitiesByName("spawn_player").First().Position);
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

			InitGame();
		}

		public void InitGame() {
			// Load the level
			Lvl = GameLevel.FromFile("levels/rm_level0.json");
			Lvl.Init(DefaultShader);

			// Create physics world
			int TileSize = Lvl.LayerMain.TileSize;
			PhysWorld = new World(Lvl.LayerMain.Width * TileSize, Lvl.LayerMain.Height * TileSize, TileSize * 4);

			// Create all tile collision boxes
			AABB?[] CollisionBoxes = new AABB?[] { };
			bool Merging;

			for (int Y = 0; Y < Lvl.LayerMain.Height; Y++)
				for (int X = 0; X < Lvl.LayerMain.Width; X++)
					if (Lvl.LayerMain.GetTile(X, Y) != -1)
						CollisionBoxes = CollisionBoxes.Add(new AABB(X * TileSize, Y * TileSize, TileSize, TileSize));

			// Merge all adjacent collision boxes
			do {
				Merging = false;

				for (int A = 0; A < CollisionBoxes.Length; A++) {
					for (int B = 0; B < CollisionBoxes.Length; B++) {
						if (A == B)
							continue;

						if (CollisionBoxes[A] == null || CollisionBoxes[B] == null)
							continue;

						AABB BoxA = CollisionBoxes[A].Value;
						AABB BoxB = CollisionBoxes[B].Value;

						if (BoxA.Adjacent(BoxB)) {
							CollisionBoxes[A] = BoxA.Union(BoxB);
							CollisionBoxes[B] = null;
							Merging = true;
						}
					}
				}
			} while (Merging);

			// Create collision box objects finally
			foreach (var Box in CollisionBoxes) {
				if (Box == null)
					continue;

				AABB B = Box.Value;

				IBox ColBox = PhysWorld.Create(B.Position.X, B.Position.Y, B.Size.X, B.Size.Y);
				ColBox.AddTags(PhysicsTags.Solid);
			}

			foreach (var E in Lvl.GetAllEntities())
				Spawn(E);

			// Create background parallax
			Background = new ParallaxSprite(this);

			// Create particle system
			Particles = new ParticleSystem(this);

            /*string BackgroundFolder = "data/textures/background/" + Lvl.Background;
			string[] BackgroundImages = Directory.GetFiles(BackgroundFolder);
			foreach (var Img in BackgroundImages)
				Background.AddLayer(Texture.FromFile(Img));*/

            Background.AddLayer(Texture.FromFile("data/textures/background/space1/1.png"));
            Background.AddLayer(Texture.FromFile("data/textures/background/space1/2.png"));
            Background.AddLayer(Texture.FromFile("data/textures/background/space1/3.png"));

            // Spawn player
            Player Ply = new Player();
			Ply.Position = Lvl.GetEntitiesByName("spawn_player").First().Position;
			Spawn(Ply);
		}

		public void Spawn(Entity Ent) {
			Ent.Game = this;
			Ent.OnSpawn();
			Entities.Add(Ent);
		}

		protected override void Update(float Dt) {
			if (Input.GetKeyPressed(Key.Escape))
				Window.Close();

			if (!Con.Enabled)
				foreach (var Ent in Entities)
					Ent.Update(Dt, GameTime);

			Particles.Update(Dt, GameTime);
		}

		protected override void Draw(float Dt) {
			Camera Cam = ShaderUniforms.Current.Camera;

			Gfx.Clear(Color.Sky);
			Background.Draw(Cam);
			Lvl.LayerBack.Draw();
			Lvl.LayerMain.Draw();


			foreach (var Ent in Entities)
				Ent.Draw();


			// DEBUG DRAW
			if (false) {
				PhysWorld.DrawDebug((int)Cam.Position.X, (int)Cam.Position.Y, (int)Cam.ViewportSize.X, (int)Cam.ViewportSize.Y, (X, Y, W, H, Alpha) => {
				}, (IBox) => {
					Color Clr = IBox.Data is Color C ? C : Color.White;

					Gfx.Rectangle(IBox.X, IBox.Y, IBox.Width, IBox.Height, Clr: Clr);
				}, (Str, X, Y, Alpha) => {
				});
			}

			Lvl.LayerFore.Draw();
			Particles.Draw();

			// TODO: Handle that shit better, what the fuck
			Vector2 ConPos = Con.Position;
			Con.Position = Cam.Position.XY() + ConPos;
			Con.Draw();
			Con.Position = ConPos;

			//Gfx.DrawText(TestFont, new Vector2(100, 50), "The quick, brown fox! Hello. Hello?", Color.White, 32);
		}
	}

	class Program {
		static void Main(string[] args) {
			FishGfxGame.Run(new TestGame());
		}
	}
}
