using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Numerics;

using FishGfx;
using FishGfx.Graphics;
using FishGfx.Graphics.Drawables;
using FishGfx.System;

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
			File.WriteAllLines("gl_extensions.txt", RenderAPI.Extensions);
#endif

			Texture QTex = Texture.FromFile("data/quake.png");
			QTex.SetFilterSmooth();

			Texture Tex2 = Texture.FromFile("data/opengl.png");
			Tex2.SetFilterSmooth();

			ShaderProgram Default = new ShaderProgram(new ShaderStage(ShaderType.VertexShader, "data/default.vert"),
				new ShaderStage(ShaderType.FragmentShader, "data/default.frag"));
			Default.Uniforms.Camera.SetOrthogonal(0, 0, 800, 600, -10, 10);

			Vector2[] UVs = new Vector2[] {
				new Vector2(0, 0),
				new Vector2(0, 1),
				new Vector2(1, 1),
				new Vector2(0, 0),
				new Vector2(1, 1),
				new Vector2(1, 0)
			};

			Mesh2D Msh1 = new Mesh2D();
			Msh1.SetUVs(UVs);

			Msh1.SetVertices(new Vector2[] {
				new Vector2(0, 0),
				new Vector2(0, 600),
				new Vector2(800, 600),
				new Vector2(0, 0),
				new Vector2(800, 600),
				new Vector2(800, 0)
			});

			Mesh2D Msh2 = new Mesh2D();
			Msh2.PrimitiveType = PrimitiveType.LineStrip;
			Msh2.SetUVs(UVs);

			Msh2.SetVertices(new Vector2[] {
				new Vector2(50, 50),
				new Vector2(50, 400),
				new Vector2(750, 550),
				new Vector2(400, 350),
				new Vector2(750, 50),
			});

			while (!RWind.ShouldClose) {
				Gfx.Clear();
				//Gfx.Line(new Vector2(0, 0), new Vector2(100, 100));

				Default.Bind();
				
				QTex.BindTextureUnit();
				Msh1.Draw();
				QTex.UnbindTextureUnit();

				//Tex2.BindTextureUnit();
				Msh2.Draw();
				//Tex2.UnbindTextureUnit();

				Default.Unbind();

				RWind.SwapBuffers();
				Events.Poll();
			}
		}
	}
}
