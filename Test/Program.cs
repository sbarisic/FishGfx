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

		static void Run() {
			RenderWindow RWind = new RenderWindow(800, 600, "FishGfx Test");

#if DEBUG
			Console.WriteLine("Running {0}", RenderAPI.Version);
			Console.WriteLine(RenderAPI.Renderer);
			//File.WriteAllLines("gl_extensions.txt", RenderAPI.Extensions);
#endif

			ShaderProgram Default = new ShaderProgram(new ShaderStage(ShaderType.VertexShader, "data/default3d.vert"),
				new ShaderStage(ShaderType.FragmentShader, "data/defaultFlatColor.frag"));

			GenericMesh GMsh = Smd.Load("data/models/smd/oildrum001_explosive/oildrum001_explosive_reference.smd")[0];
			GMsh.SwapYZ();
			Mesh3D Msh = new Mesh3D(GMsh);

			Texture Tex1 = Texture.FromFile("data/" + GMsh.MaterialName + ".png");
			Tex1.SetFilterSmooth();

			GMsh.CalculateBoundingSphere(out Vector3 Pos, out float Rad);
			SetupCamera(Pos, Rad);

			Stopwatch SWatch = Stopwatch.StartNew();
			while (!RWind.ShouldClose) {
				Gfx.Clear();
				ShaderUniforms.Model = Matrix4x4.CreateRotationY((float)SWatch.ElapsedMilliseconds / 1000);

				Default.Bind();
				Tex1.BindTextureUnit();

				Msh.Draw();

				Tex1.UnbindTextureUnit();
				Default.Unbind();

				RWind.SwapBuffers();
				Events.Poll();
			}
		}

		static void SetupCamera(Vector3 Target, float Radius) {
			// Cam.SetOrthogonal(0, 0, 800, 600, -10, 10);
			Camera Cam = ShaderUniforms.Camera;

			Cam.SetPerspective(800, 600);
			Cam.Position = new Vector3(10, 30, 50) * 100;
			Cam.LookAtFitToScreen(Target, Radius + (Radius * (50.0f / 100)));
		}
	}
}
