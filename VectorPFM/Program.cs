using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;

using FishGfx;
using FishGfx.Graphics;
using FishGfx.System;

namespace VectorPFM {
	class Program {
		static RenderWindow RWind;

		static void Main(string[] args) {
			const float Scale = 0.9f;

			RenderAPI.GetDesktopResolution(out int W, out int H);
			RWind = new RenderWindow((int)(W * Scale), (int)(H * Scale), "Vector PFM");

			ShaderProgram Default = new ShaderProgram(new ShaderStage(ShaderType.VertexShader, "data/default.vert"),
				new ShaderStage(ShaderType.FragmentShader, "data/default.frag"));

			Vector2 Viewport = RWind.GetWindowSizeVec();
			Default.Uniforms.Viewport = Viewport;
			Default.Uniforms.Project = Matrix4x4.CreateOrthographicOffCenter(0, Viewport.X, 0, Viewport.Y, -10, 10);


			while (!RWind.ShouldClose) {
				Gfx.Clear();



				RWind.SwapBuffers();
				Events.Poll();
			}
		}
	}
}
