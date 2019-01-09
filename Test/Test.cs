using FishGfx;
using FishGfx.Formats;
using FishGfx.Graphics;
using FishGfx.Graphics.Drawables;
using FishGfx.Gweny.Control;
using FishGfx.Gweny.Renderer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Test {
	class Program {
		static void Main(string[] args) {
			Run();
		}

		static RenderWindow Window;
		static Camera Cam;
		static Vector3 MoveVec = Vector3.Zero;

		static FishGfxRenderer Renderer;
		static FishGfx.Gweny.Skin.TexturedBase Skin;
		static Canvas Canvas;
		static FishGfx.Gweny.UnitTest.UnitTest TestTest;

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
				//Cam.Update(-new Vector2(X, Y));
			};

			Window.OnKey += (RenderWindow Wnd, Key Key, int Scancode, bool Pressed, bool Repeat, KeyMods Mods) => {
				if (Key == Key.Escape && Pressed)
					Environment.Exit(0);

				if (Key == Key.Space)
					MoveVec.Y = Pressed ? 1 : 0;
				else if (Key == Key.C)
					MoveVec.Y = Pressed ? -1 : 0;
				else if (Key == Key.W)
					MoveVec.Z = Pressed ? -1 : 0;
				else if (Key == Key.A)
					MoveVec.X = Pressed ? -1 : 0;
				else if (Key == Key.S)
					MoveVec.Z = Pressed ? 1 : 0;
				else if (Key == Key.D)
					MoveVec.X = Pressed ? 1 : 0;
			};

			ShaderProgram Default = new ShaderProgram(new ShaderStage(ShaderType.VertexShader, "data/default3d.vert"),
				new ShaderStage(ShaderType.FragmentShader, "data/defaultFlatColor.frag"));

			{
				Renderer = new FishGfxRenderer(new ShaderProgram(new ShaderStage(ShaderType.VertexShader, "data/default.vert"), new ShaderStage(ShaderType.FragmentShader, "data/defaultFlatColor.frag")), Window);
				Skin = new FishGfx.Gweny.Skin.TexturedBase(Renderer, "data/textures/gwen_skin.png");
				Skin.DefaultFont = new FishGfx.Gweny.Font(Renderer, "Arial");
				Canvas = new Canvas(Skin);
				Canvas.SetSize((int)Size.X, (int)Size.Y);
				Canvas.ShouldCacheToTexture = false;
				Canvas.ShouldDrawBackground = true;
				Canvas.DrawDebugOutlines = true;
				Canvas.BackgroundColor = new Color(150, 170, 170);
				TestTest = new FishGfx.Gweny.UnitTest.UnitTest(Canvas);
			}

			Stopwatch SWatch = Stopwatch.StartNew();
			float Dt = 0;

			RenderState RS = Gfx.CreateDefaultRenderState();
			RS.EnableDepthTest = false;
			Gfx.PushRenderState(RS);

			ShaderUniforms U = ShaderUniforms.Current;
			U.Camera.SetOrthogonal(0, 0, Window.WindowWidth, Window.WindowHeight);

			Texture Test = Texture.FromFile("data/textures/test16.png");

			while (!Window.ShouldClose) {
				while (SWatch.ElapsedMilliseconds / 1000.0f < (1.0f / 60))
					;

				Dt = SWatch.ElapsedMilliseconds / 1000.0f;
				SWatch.Restart();

				Gfx.Clear();
				Canvas.RenderCanvas();

				/*Gfx.Line(new Vertex2(25, 10), new Vertex2(25, 100));
				Gfx.Rectangle(50, 10, 100, 100);
				Gfx.FilledRectangle(200, 10, 100, 100);

				Gfx.TexturedRectangle(350, 10, 100, 100, Texture: Test);
				Gfx.Rectangle(350, 10, 100, 100, Clr: Color.Red);*/

				Update(Dt);
				Window.SwapBuffers();
				Events.Poll();
			}
		}

		static void Update(float Dt) {
			const float MoveSpeed = 500;

			/*
			if (!(MoveVec.X == 0 && MoveVec.Y == 0 && MoveVec.Z == 0))
				Cam.Position += Cam.ToWorldNormal(Vector3.Normalize(MoveVec)) * MoveSpeed * Dt;
			*/

			Renderer.Update(Dt);
		}
	}
}
