using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Numerics;

using FishGfx;
using FishGfx.Formats;
using FishGfx.Graphics;
using FishGfx.Graphics.Drawables;
using FishGfx.System;
using System.Diagnostics;

namespace Test {
	class Program {
		static void Main(string[] args) {
			Run();
		}

		static RenderWindow Window;
		static Camera Cam;
		static Vector3 MoveVec = Vector3.Zero;

		static void Run() {
			Vector2 Size = RenderWindow.GetDesktopResolution() * 0.9f;
			Window = new RenderWindow((int)Size.X, (int)Size.Y, "FishGfx Test");

#if DEBUG
			Console.WriteLine("Running {0}", RenderAPI.Version);
			Console.WriteLine(RenderAPI.Renderer);
			//File.WriteAllLines("gl_extensions.txt", RenderAPI.Extensions);
#endif

			Window.CaptureCursor = true;
			Window.OnMouseMoveDelta += (Wnd, X, Y) => {
				Cam.Update(-new Vector2(X, Y));
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

			RenderModel WorldSurface = LoadWorldSurface();
			RenderModel Pin = LoadPin();

			SetupCamera();
			Stopwatch SWatch = Stopwatch.StartNew();
			float Dt = 0;

			while (!Window.ShouldClose) {
				while (SWatch.ElapsedMilliseconds / 1000.0f < (1.0f / 60))
					;

				Dt = SWatch.ElapsedMilliseconds / 1000.0f;
				SWatch.Restart();

				Gfx.Clear();
				{
					const float ScaleX = 1500;
					const float ScaleY = 1000;
					ShaderUniforms.Default.Model = Matrix4x4.CreateTranslation(new Vector3(0.5f, -0.5f, 0.5f)) * Matrix4x4.CreateScale(new Vector3(ScaleX, 10, ScaleY));
					//ShaderUniforms.Model *= Matrix4x4.CreateTranslation(new Vector3(-83, 0, -215));
					Default.Bind(ShaderUniforms.Default);
					WorldSurface.Draw();
					Default.Unbind();

					/*ShaderUniforms.Model = CameraClient.GetRotation() * Matrix4x4.CreateScale(25) * Matrix4x4.CreateTranslation(CameraClient.GetPos());
					//ShaderUniforms.Model = Matrix4x4.CreateTranslation(Vector3.Zero);

					Default.Bind();
					Pin.Draw();
					Default.Unbind();*/

				}
				Update(Dt);
				Window.SwapBuffers();
				Events.Poll();
			}
		}

		static void Update(float Dt) {
			const float MoveSpeed = 500;

			if (!(MoveVec.X == 0 && MoveVec.Y == 0 && MoveVec.Z == 0))
				Cam.Position += Cam.ToWorldNormal(Vector3.Normalize(MoveVec)) * MoveSpeed * Dt;
		}

		static void SetupCamera() {
			Cam = ShaderUniforms.Default.Camera;
			Cam.MouseMovement = true;

			Cam.SetPerspective(Window.WindowSize.X, Window.WindowSize.Y);
			Cam.Position = new Vector3(0, 50, 0);
			Cam.LookAt(new Vector3(100, 0, 20));
		}

		static RenderModel LoadWorldSurface() {
			RenderModel Cube = new RenderModel(Obj.Load("data/models/cube/cube.obj"));

			Texture Tex = Texture.FromFile("data/textures/grid.png");
			Cube.SetMaterialTexture("cube", Tex);

			return Cube;
		}

		static RenderModel LoadPin() {
			RenderModel Pin = new RenderModel(Obj.Load("data/models/pin/pin.obj"));

			Texture Tex = Texture.FromFile("data/models/pin/pin_mat.png");
			Pin.SetMaterialTexture("pin_mat", Tex);

			return Pin;
		}
	}
}
