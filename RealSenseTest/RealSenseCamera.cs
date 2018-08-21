using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;
using FishGfx.RealSense;
using FishGfx;
using System.Threading;

namespace RealSenseTest {
	public static class RealSense {
		static int W;
		static int H;

		public static bool Ready;

		public static void Init() {
			Thread PollThread = new Thread(() => {
				if (!Debug.FakePoints) {
					IEnumerable<FrameData> Resolutions = RealSenseCamera.QueryResolutions().OrderBy((Data) => Data.Width).Reverse();
					IEnumerable<FrameData> DepthResolutions = Resolutions.Where((Data) => Data.Type == FrameType.Depth);

					int ReqW = 640;
					float ReqH = 480;

					//int ReqW = 1280;
					//float ReqH = 720;

					FrameData DepthRes = DepthResolutions.Where((Data) => Data.Width == ReqW && Data.Height == ReqH && Data.Format == FrameFormat.Z16).First();
					FrameData ColorRes = Resolutions.Where((Data) => Data.Width == ReqW && Data.Height == ReqH && Data.Format == FrameFormat.Rgb8).First();

					W = ColorRes.Width;
					H = ColorRes.Height;
					RealSenseCamera.SetOption(DepthRes, RealSenseOption.VisualPreset, 4);
					RealSenseCamera.SetOption(DepthRes, RealSenseOption.EmitterEnabled, 0);
					RealSenseCamera.SetOption(DepthRes, RealSenseOption.EnableAutoExposure, 1);
					//RealSenseCamera.SetOption(DepthRes, RealSenseOption.LaserPower, 30);

					RealSenseCamera.DisableAllStreams();
					RealSenseCamera.EnableStream(DepthRes, ColorRes);
					RealSenseCamera.Start();

					while (true) {
						if (RealSenseCamera.PollForFrames(null, OnPointCloud)) {
							if (!Ready)
								Ready = true;
						}
					}
				} else {
					Ready = true;

					float Scale = 1.0f / 500.0f;
					int PlaneSize = 100;
					Vertex3[] Verts = OnPointCloud(PlaneSize * PlaneSize, null, null);

					for (int y = 0; y < PlaneSize; y++)
						for (int x = 0; x < PlaneSize; x++)
							Verts[y * PlaneSize + x] = new Vertex3(x * Scale - ((PlaneSize / 2) * Scale), y * Scale - ((PlaneSize / 2) * Scale), 0.5f);

					while (true)
						OnPointCloud(Verts.Length, Verts, null);
				}
			});
			PollThread.IsBackground = true;
			PollThread.Start();

			while (!Ready)
				Thread.Sleep(10);
		}

		static int PointCount;
		static Vertex3[] Points;

		static Vertex3[] VertsArr;
		static Vertex3[] OnPointCloud(int Count, Vertex3[] Verts, FrameData[] Frames) {
			if (Verts == null) {
				if (VertsArr == null || VertsArr.Length < Count) {
					VertsArr = new Vertex3[Count];
					Points = new Vertex3[Count];
				}

				return VertsArr;
			}

			CameraClient.GetRotationAngles(out float Yaw, out float Pitch, out float Roll);
			Matrix4x4 TransMat = Matrix4x4.CreateFromYawPitchRoll(Yaw, Pitch + (float)Math.PI, -Roll) * CameraClient.GetTranslation();

			lock (Points) {
				PointCount = 0;
				for (int i = 0, j = 0; i < Count; i++, j++) {
					if (Verts[i].Position == Vector3.Zero)
						continue;

					if (Verts[i].Position.Z > 2)
						continue;

					Points[j] = Verts[i];
					Points[j].Color = GfxUtils.RandomColor();
					Points[j].Position = Vector3.Transform(Points[j].Position * 1000, TransMat);
					PointCount++;
				}
			}

			return null;
		}

		public static void GetVerts(out Vertex3[] Verts, out int Count) {
			lock (Points) {
				Verts = Points;
				Count = PointCount;
			}
		}
	}
}
