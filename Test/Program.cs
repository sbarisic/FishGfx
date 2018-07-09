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

			ShaderProgram Default = new ShaderProgram(new ShaderStage(ShaderType.VertexShader, "data/default.vert"),
				new ShaderStage(ShaderType.FragmentShader, "data/defaultFlatColor.frag"));
			Default.Uniforms.Camera.SetOrthogonal(0, 0, 800, 600, -10, 10);

			Texture Tex1 = Texture.FromFile("data/quake.png");
			Tex1.SetFilterSmooth();

			Texture Tex2 = Texture.FromFile("data/opengl.png");
			Tex2.SetFilterSmooth();

			/*Sprite S = new Sprite(0, 0, 800, 600);
			S.Texture = Tex1;*/

			Random Rnd = new Random();

			Sprite[] AllSprites = new Sprite[10000];
			for (int i = 0; i < AllSprites.Length; i++) {
				AllSprites[i] = new Sprite(Rnd.Next(800), Rnd.Next(600), Rnd.Next(8, 80), Rnd.Next(6, 60));
				AllSprites[i].Texture = Tex1;
			}

			while (!RWind.ShouldClose) {
				Gfx.Clear();

				Default.Bind();

				//S.Draw();
				for (int i = 0; i < AllSprites.Length; i++)
					AllSprites[i].Draw();

				Default.Unbind();

				RWind.SwapBuffers();
				Events.Poll();
			}
		}
	}
}
