using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Intel.RealSense;

namespace FishGfx.RealSense {
	public delegate void OnFramesReceived(params FrameData[] Frames);

	public enum FrameType {
		Any = 0,
		Depth = 1,
		Color = 2,
		Infrared = 3,
		Fisheye = 4,
		Gyro = 5,
		Accel = 6,
		Gpio = 7,
		Pose = 8,
		Confidence = 9
	}

	public enum FrameFormat {

	}

	public struct FrameData {
		public int Width;
		public int Height;
		public int Stride;
		public int BitsPerPixel;
		public FrameType Type;
		public IntPtr Data;
	}

	public static class RealSenseCamera {
		static Context Ctx = new Context();
		static Config Cfg = new Config();
		static Pipeline Pipeline = new Pipeline(Ctx);

		static PipelineProfile Profile;

		public static void DisableAllStreams() {
			Cfg.DisableAllStreams();
		}

		public static void EnableStream(FrameType Stream) {
			Cfg.EnableStream((Stream)Stream);
		}

		public static void EnableStream(FrameType Stream, int Width, int Height, FrameFormat Format) {
			Cfg.EnableStream((Stream)Stream, Width, Height, (Format)Format);
		}

		public static void QueryResolution() {
			Device[] Devices = Ctx.Devices.ToArray();
		}

		public static void Start() {
			if (Profile != null)
				throw new Exception("Already started");

			Profile = Pipeline.Start(Cfg);
		}

		public static void Stop() {
			if (Profile == null)
				throw new Exception("Already stopped");

			Pipeline.Stop();
			Profile = null;
		}

		public static bool PollForFrames(OnFramesReceived OnFrames) {
			if (Pipeline.PollForFrames(out FrameSet Frames)) {
				using (Frames) {
					Frame[] AllFrames = Frames.ToArray();
					FrameData[] FrameData = new FrameData[AllFrames.Length];

					for (int i = 0; i < FrameData.Length; i++) {
						VideoFrame Vid = AllFrames[i] as VideoFrame;
						DepthFrame Dpt = AllFrames[i] as DepthFrame;
						int W = Vid?.Width ?? Dpt?.Width ?? 0;
						int H = Vid?.Height ?? Dpt?.Height ?? 0;
						int Bpp = Vid?.BitsPerPixel ?? Dpt?.BitsPerPixel ?? 0;
						int Stride = Vid?.Stride ?? Dpt?.Stride ?? 0;

						FrameData[i] = new FrameData() {
							BitsPerPixel = Bpp,
							Data = AllFrames[i].Data,
							Width = W,
							Height = H,
							Stride = Stride,
							Type = (FrameType)AllFrames[i].Profile.Stream
						};
					}

					OnFrames(FrameData);

					for (int i = 0; i < AllFrames.Length; i++)
						AllFrames[i].Dispose();
				}

				return true;
			}

			return false;
		}
	}
}
