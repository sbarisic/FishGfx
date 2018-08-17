using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;
using FishGfx;
using FishGfx.Graphics;
using FishGfx.Graphics.Drawables;
using FishGfx.System;
using FishGfx.RealSense;
using System.IO;
using System.Numerics;
using System.Diagnostics;
using System.Threading;
using System.Runtime.InteropServices;
using FishGfx.Formats;

namespace RealSenseTest {
	class Program {
		static void Main(string[] args) {
			Run();
		}

		static RenderWindow RWind;
		static Camera Cam;
		static Vector3 MoveVec = Vector3.Zero;

		static void Run() {
			Vector2 Size = RenderWindow.GetDesktopResolution() * 0.9f;
			RWind = new RenderWindow((int)Size.X, (int)Size.Y, "RealSense Test");

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

				if (Pressed && Key == Key.Escape)
					Environment.Exit(0);
			};

			ShaderProgram Default = new ShaderProgram(new ShaderStage(ShaderType.VertexShader, "data/default3d.vert"),
				new ShaderStage(ShaderType.FragmentShader, "data/default.frag"));

			RenderModel WorldSurface = LoadWorldSurface();
			RenderModel Pin = LoadPin();

			SetupCamera();
			Stopwatch SWatch = Stopwatch.StartNew();
			float Dt = 0;

			Mesh3D Points = new Mesh3D(BufferUsage.StreamDraw);
			Points.PrimitiveType = PrimitiveType.Points;
			Points.SetVertices(new Vertex3[] { }, 0, false, false);

			CameraClient.Init();
			RealSense.Init();

			while (!RWind.ShouldClose) {
				while (SWatch.ElapsedMilliseconds / 1000.0f < (1.0f / 60))
					;

				Dt = SWatch.ElapsedMilliseconds / 1000.0f;
				SWatch.Restart();

				Gfx.Clear();
				{
					RealSense.GetVerts(out Vertex3[] Verts, out int Count);
					if (Count != 0)
						Points.SetVertices(Verts, Count, false, false);

					const float ScaleX = 1500;
					const float ScaleY = 1000;
					ShaderUniforms.Model = Matrix4x4.CreateTranslation(new Vector3(0.5f, -0.5f, 0)) * Matrix4x4.CreateScale(new Vector3(ScaleX, 10, ScaleY));
					ShaderUniforms.Model *= Matrix4x4.CreateTranslation(-200, 0, 200);
					Default.Bind();
					WorldSurface.Draw();
					Default.Unbind();

					Matrix4x4 TransRot = CameraClient.GetRotation() * CameraClient.GetTranslation();
					ShaderUniforms.Model = Matrix4x4.CreateScale(10) * TransRot;
					Default.Bind();
					Pin.Draw();
					Default.Unbind();

					//ShaderUniforms.Model = TransRot;
					ShaderUniforms.Model = Matrix4x4.Identity;
					Default.Bind();
					Points.Draw();
					Default.Unbind();
				}
				Update(Dt);
				RWind.SwapBuffers();
				Events.Poll();
			}
		}

		static void Update(float Dt) {
			const float MoveSpeed = 500;

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

		static RenderModel LoadWorldSurface() {
			RenderModel Cube = new RenderModel(Obj.Load("data/models/cube/cube.obj"));

			Texture Tex = Texture.FromFile("data/textures/grid.png");
			Cube.SetMaterialTexture("cube", Tex);

			return Cube;
		}

		static RenderModel LoadPin() {
			/*RenderModel Pin = new RenderModel(Obj.Load("data/models/pin/pin.obj"));

			Texture Tex = Texture.FromFile("data/models/pin/pin_mat.png");
			Pin.SetMaterialTexture("pin_mat", Tex);*/

			GenericMesh[] Meshes = Obj.Load("data/models/biplane/biplane.obj");


			//Matrix4x4 RotMat = Matrix4x4.CreateFromYawPitchRoll(-(float)Math.PI / 2, (float)Math.PI / 2, 0);
			Matrix4x4 RotMat = Matrix4x4.CreateFromYawPitchRoll(-(float)Math.PI, 0, 0);
			for (int i = 0; i < Meshes.Length; i++)
				Meshes[i].ForEachPosition((In) => Vector3.Transform(In, RotMat));//*/

			RenderModel Pin = new RenderModel(Meshes, false, false);

			Texture WhiteTex = Texture.FromFile("data/textures/colors/white.png");
			for (int i = 0; i < Meshes.Length; i++) {
				Pin.SetMaterialTexture(Meshes[i].MaterialName, WhiteTex);
				Pin.GetMaterialMesh(Meshes[i].MaterialName).DefaultColor = GfxUtils.RandomColor();
			}

			return Pin;
		}
	}
}
