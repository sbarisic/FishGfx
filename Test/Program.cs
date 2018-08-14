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

			GenericMesh HolodeckMesh = new GenericMesh(Obj.Load("data/models/holodeck/holodeck.obj"));
			HolodeckMesh.SwapWindingOrder();
			Mesh3D Holodeck = new Mesh3D(HolodeckMesh);

			Texture Tex = Texture.FromFile("data/models/holodeck/wall.png");
			Tex.SetWrap(TextureWrap.Repeat);
			Tex.SetFilter(TextureFilter.Linear);

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
				{
					Tex.BindTextureUnit();
					Holodeck.Draw();
					Tex.UnbindTextureUnit();
				}
				Default.Unbind();

				Update(Dt);
				RWind.SwapBuffers();
				Events.Poll();
			}
		}

		static void Update(float Dt) {
			const float MoveSpeed = 100;

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
	}
}
