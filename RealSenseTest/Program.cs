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

namespace RealSenseTest {
	class Program {
		static int W;
		static int H;

		static Texture ClrTex;
		static Texture DptTex;
		static Mesh3D Points;

		static void Main(string[] args) {
			SetupCamera();
			RenderWindow RWind = new RenderWindow(W, H, "RealSense Test");

			ClrTex = Texture.Empty(W, H);
			DptTex = Texture.Empty(W, H);

			Camera OrthoCam = new Camera();
			OrthoCam.SetOrthogonal(0, 0, 800, 600, -10, 10);

			Camera PerspCam = new Camera();
			PerspCam.SetPerspective(W, H, (float)(91.2 * Math.PI / 180));
			PerspCam.Rotation = Quaternion.CreateFromAxisAngle(new Vector3(1, 0, 0), (float)Math.PI);

			ShaderProgram Default = new ShaderProgram(new ShaderStage(ShaderType.VertexShader, "data/default.vert"),
				new ShaderStage(ShaderType.FragmentShader, "data/realsense.frag"));
			Default.Uniform1("ColorTexture", 0);
			Default.Uniform1("DepthTexture", 1);

			ShaderProgram Default3D = new ShaderProgram(new ShaderStage(ShaderType.VertexShader, "data/default3d.vert"),
				new ShaderStage(ShaderType.FragmentShader, "data/realsense.frag"));
			Default3D.Uniform1("ColorTexture", 0);
			Default3D.Uniform1("DepthTexture", 1);


			Mesh2D Quad = new Mesh2D();
			Quad.SetUVs(new Vector2[] { new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 1) });
			Quad.SetVertices(new Vector2[] { new Vector2(0, 0), new Vector2(0, 600), new Vector2(800, 600), new Vector2(0, 0), new Vector2(800, 600), new Vector2(800, 0) }
				.Reverse().ToArray());

			Points = new Mesh3D(BufferUsage.DynamicDraw);
			Points.PrimitiveType = PrimitiveType.Points;

			while (!RWind.ShouldClose) {
				Gfx.Clear();

				RealSenseCamera.PollForFrames(OnFrameData, OnPointCloud);

				//Default.Bind();
				ShaderUniforms.Camera = PerspCam;
				Default3D.Bind();
				{

					ClrTex.BindTextureUnit();
					DptTex.BindTextureUnit(1);

					//Quad.Draw();
					Points.Draw();

					DptTex.UnbindTextureUnit(1);
					ClrTex.UnbindTextureUnit();

				}
				Default3D.Unbind();
				//Default.Unbind();

				RWind.SwapBuffers();
				Events.Poll();
			}
		}

		static void SetupCamera() {
			IEnumerable<FrameData> Resolutions = RealSenseCamera.QueryResolutions().OrderBy((Data) => Data.Width).Reverse();
			IEnumerable<FrameData> DepthResolutions = Resolutions.Where((Data) => Data.Type == FrameType.Depth);

			FrameData DepthRes = DepthResolutions.First();
			//FrameData DepthRes = DepthResolutions.Last();

			FrameData ColorRes = Resolutions.Where((Data) => Data.Width == DepthRes.Width && Data.Height == DepthRes.Height && Data.Format == FrameFormat.Rgb8).First();

			W = ColorRes.Width;
			H = ColorRes.Height;
			RealSenseCamera.SetOption(DepthRes, 12, 4);

			RealSenseCamera.DisableAllStreams();
			RealSenseCamera.EnableStream(DepthRes, ColorRes);
			RealSenseCamera.Start();
		}

		static void OnFrameData(FrameData[] Frames) {
			FrameData DepthFrame = Frames[0];
			FrameData ClrFrame = Frames[1];

			ClrTex.SetPixels2D_RGB8(ClrFrame.Data, ClrFrame.Width, ClrFrame.Height);
			DptTex.SetPixels2D_R16(DepthFrame.Data, DepthFrame.Width, DepthFrame.Height);
		}

		static Vertex3[] VertsArr = new Vertex3[0];

		static Vertex3[] OnPointCloud(int Count, Vertex3[] Verts, FrameData[] Frames) {
			if (Verts == null) {
				if (VertsArr.Length < Count)
					VertsArr = new Vertex3[Count];

				return VertsArr;
			}

			/*Verts = Verts.Where(V => V.Position != Vector3.Zero && V.UV.X > 0 && V.UV.X < 1 && V.UV.Y > 0 && V.UV.Y < 1).ToArray();
			Count = Verts.Length;*/

			Points.SetVertices(Count, Verts);
			return null;
		}
	}
}
