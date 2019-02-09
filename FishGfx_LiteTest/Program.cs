using FishGfx;
using FishGfx.Formats;
using FishGfx.Graphics;
using FishGfx.Graphics.Drawables;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace FishGfx_LiteTest {
	class Program {
		static void Main(string[] args) {
			Vector2 WindowSize = RenderWindow.GetDesktopResolution() * 0.5f;
			RenderWindow RWind = new RenderWindow((int)WindowSize.X, (int)WindowSize.Y, "Test Lite");

			Mesh3D CubeMesh = new Mesh3D();
			CubeMesh.SetVertices(Obj.Load("cube.obj").First().Vertices.ToArray());

			ShaderProgram Shader = new ShaderProgram(new ShaderStage(ShaderType.VertexShader, "data/shaders/default3d.vert"),
				new ShaderStage(ShaderType.FragmentShader, "data/shaders/default_tex_clr.frag"));

			ShaderUniforms.Current.Camera.SetPerspective(WindowSize);
			ShaderUniforms.Current.Camera.Position = new Vector3(1.5f);
			ShaderUniforms.Current.Camera.LookAt(Vector3.Zero);

			Texture Tex = Texture.FromFile("test.png");
			Stopwatch SWatch = Stopwatch.StartNew();

			while (!RWind.ShouldClose) {
				Gfx.Clear();
				ShaderUniforms.Current.Model = Matrix4x4.CreateFromYawPitchRoll(SWatch.ElapsedMilliseconds / 1000.0f, 0, 0);

				Tex.BindTextureUnit();
				Shader.Bind(ShaderUniforms.Current);
				CubeMesh.Draw();
				Shader.Unbind();
				Tex.UnbindTextureUnit();

				RWind.SwapBuffers();
				Events.Poll();
			}
		}
	}
}