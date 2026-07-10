using FishGfx;
using FishGfx.Graphics;
using FishGfx.Graphics.Drawables;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Numerics;

namespace FishGfx.SmokeTest {
	internal static class Program {
		private static void Main() {
			const int width = 640;
			const int height = 360;
			RenderWindow window = new RenderWindow(width, height, "FishGfx .NET 10 / Silk.NET Smoke Test", true);

			ShaderProgram shader = new ShaderProgram(
				new ShaderStage(ShaderType.VertexShader, "data/shaders/default2d.vert"),
				new ShaderStage(ShaderType.FragmentShader, "data/shaders/default_tex_clr.frag"));

			Mesh2D quad = new Mesh2D();
			quad.SetVertices(Vertex2.CreateQuad(
				new Vector2(160, 90), new Vector2(320, 180),
				Vector2.Zero, Vector2.One, new Color(255, 220, 180)).ToArray());

			Texture texture;
			using (Bitmap bitmap = new Bitmap(2, 2)) {
				bitmap.SetPixel(0, 0, System.Drawing.Color.CornflowerBlue);
				bitmap.SetPixel(1, 0, System.Drawing.Color.Gold);
				bitmap.SetPixel(0, 1, System.Drawing.Color.OrangeRed);
				bitmap.SetPixel(1, 1, System.Drawing.Color.White);
				texture = Texture.FromImage(bitmap);
			}

			ShaderUniforms.Current.Camera.SetOrthogonal(0, 0, width, height);
			ShaderUniforms.Current.Resolution = new Vector2(width, height);
			ShaderUniforms.Current.TextureSize = texture.Size;
			Stopwatch runtime = Stopwatch.StartNew();

			while (!window.ShouldClose && runtime.Elapsed < TimeSpan.FromSeconds(3)) {
				Events.Poll();
				Gfx.Clear(new Color(25, 30, 45));
				texture.BindTextureUnit();
				shader.Bind(ShaderUniforms.Current);
				quad.Draw();
				shader.Unbind();
				texture.UnbindTextureUnit();
				window.SwapBuffers();
			}

			quad.VAO.Dispose();
			texture.Dispose();
			shader.Dispose();
			RenderAPI.CollectGarbage();
			window.Close();
			Console.WriteLine($"Smoke test completed using {RenderAPI.Renderer}");
		}
	}
}
