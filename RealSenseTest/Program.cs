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

namespace RealSenseTest {
	class Program {
		static int W;
		static int H;
		static Texture Tex;

		static void Main(string[] args) {
			SetupCamera();
			RenderWindow RWind = new RenderWindow(W, H, "RealSense Test");
			Tex = Texture.Empty(W, H);

			ShaderProgram Default = new ShaderProgram(new ShaderStage(ShaderType.VertexShader, "data/default.vert"),
				new ShaderStage(ShaderType.FragmentShader, "data/default.frag"));
			Default.Uniforms.Camera.SetOrthogonal(0, 0, 800, 600, -10, 10);

			Mesh2D Quad = new Mesh2D();
			Quad.SetUVs(new Vector2[] { new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 1) });
			Quad.SetVertices(new Vector2[] { new Vector2(0, 0), new Vector2(0, 600), new Vector2(800, 600), new Vector2(0, 0), new Vector2(800, 600), new Vector2(800, 0) }
				.Reverse().ToArray());

			while (!RWind.ShouldClose) {
				Gfx.Clear();

				RealSenseCamera.PollForFrames(OnFrameData);

				Default.Bind();
				Tex.BindTextureUnit();
				Quad.Draw();
				Tex.UnbindTextureUnit();
				Default.Unbind();

				RWind.SwapBuffers();
				Events.Poll();
			}
		}

		static void SetupCamera() {
			IEnumerable<FrameData> Resolutions = RealSenseCamera.QueryResolutions().OrderBy((Data) => Data.Width).Reverse();
			FrameData DepthRes = Resolutions.Where((Data) => Data.Type == FrameType.Depth).First();
			FrameData ColorRes = Resolutions.Where((Data) => Data.Width == DepthRes.Width && Data.Height == DepthRes.Height && Data.Format == FrameFormat.Rgb8).First();

			W = ColorRes.Width;
			H = ColorRes.Height;
			RealSenseCamera.SetOption(DepthRes, 12, 4);

			RealSenseCamera.DisableAllStreams();
			RealSenseCamera.EnableStream(DepthRes, ColorRes);
			RealSenseCamera.Start();
		}

		static void OnFrameData(FrameData[] Frames) {
			FrameData ClrFrame = Frames[1];

			Tex.SetPixels2D_RGB8(ClrFrame.Data, ClrFrame.Width, ClrFrame.Height);
		}
	}
}
