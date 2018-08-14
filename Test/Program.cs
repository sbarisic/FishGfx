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

		static RenderWindow RWind;
		static Camera Cam;
		static Vector3 MoveVec = Vector3.Zero;

		static void Run() {
			RWind = new RenderWindow(1366, 768, "FishGfx Test");

#if DEBUG
			Console.WriteLine("Running {0}", RenderAPI.Version);
			Console.WriteLine(RenderAPI.Renderer);
			//File.WriteAllLines("gl_extensions.txt", RenderAPI.Extensions);
#endif

			RWind.CaptureCursor = true;
			RWind.OnMouseMoveDelta += (Wnd, X, Y) => {
				Cam.Update(-new Vector2(X, Y));
			};

			RWind.OnKey += (RenderWindow Wnd, Key Key, int Scancode, bool Pressed, bool Repeat, KeyMods Mods) => {
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

			RenderModel Holodeck = LoadHolodeck();
			SetupCamera();

			Stopwatch SWatch = Stopwatch.StartNew();
			float Dt = 0;

			while (!RWind.ShouldClose) {
				while (SWatch.ElapsedMilliseconds / 1000.0f < (1.0f / 60))
					;

				Dt = SWatch.ElapsedMilliseconds / 1000.0f;
				SWatch.Restart();

				Gfx.Clear();

				Default.Bind();
				Holodeck.Draw();
				Default.Unbind();

				Update(Dt);
				RWind.SwapBuffers();
				Events.Poll();
			}
		}

		static void Update(float Dt) {
			const float MoveSpeed = 250;

			if (!(MoveVec.X == 0 && MoveVec.Y == 0 && MoveVec.Z == 0))
				Cam.Position += Cam.ToWorldNormal(Vector3.Normalize(MoveVec)) * MoveSpeed * Dt;
		}

		static void SetupCamera() {
			Cam = ShaderUniforms.Camera;
			Cam.MouseMovement = true;

			Cam.SetPerspective(RWind.WindowSize.X, RWind.WindowSize.Y);
			Cam.Position = new Vector3(0, 50, 0);
			Cam.LookAt(new Vector3(100, 0, 20));
		}

		static RenderModel LoadHolodeck() {
			RenderModel Holodeck = new RenderModel(Obj.Load("data/models/holodeck/holodeck.obj").Select((M) => { M.SwapWindingOrder(); return M; }));

			Texture Tex = Texture.FromFile("data/textures/colors/white.png");
			foreach (var Mat in Holodeck.GetMaterialNames())
				Holodeck.SetMaterialTexture(Mat, Tex);

			Holodeck.GetMaterialMesh("door").DefaultColor = new Color(1.0000, 0.0941, 0.0000);
			Holodeck.GetMaterialMesh("doorframe").DefaultColor = new Color(0.5647, 0.3059, 0.0941);
			Holodeck.GetMaterialMesh("doorinset").DefaultColor = new Color(0.9137, 0.9137, 0.9137);
			Holodeck.GetMaterialMesh("light").DefaultColor = new Color(0.8314, 0.8471, 0.5608);
			Holodeck.GetMaterialMesh("holdeck_material").DefaultColor = new Color(0.5880, 0.5880, 0.5880);

			Tex = Texture.FromFile("data/textures/holodeck/wall.png");
			Tex.SetWrap(TextureWrap.Repeat);
			Tex.SetFilter(TextureFilter.Linear);
			Holodeck.SetMaterialTexture("holodeckwirefrane", Tex);
			//Holodeck.GetMaterialMesh("holodeckwirefrane").DefaultColor = new Color(0.1, 0.1, 0.1);

			Tex = Texture.FromFile("data/textures/holodeck/screen.png");
			Tex.SetFilter(TextureFilter.Linear);
			Holodeck.SetMaterialTexture("screens", Tex);

			return Holodeck;
		}
	}
}
