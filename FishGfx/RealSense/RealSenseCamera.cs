﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
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
		Any = 0,
		Z16 = 1,
		Disparity16 = 2,
		Xyz32f = 3,
		Yuyv = 4,
		Rgb8 = 5,
		Bgr8 = 6,
		Rgba8 = 7,
		Bgra8 = 8,
		Y8 = 9,
		Y16 = 10,
		Raw10 = 11,
		Raw16 = 12,
		Raw8 = 13,
		Uyvy = 14,
		MotionRaw = 15,
		MotionXyz32f = 16,
		GpioRaw = 17
	}

	public struct FrameData {
		public int Idx;
		public int Width;
		public int Height;
		public FrameType Type;
		public FrameFormat Format;

		public int Stride;
		public int BitsPerPixel;
		public IntPtr Data;

		internal Sensor DataSensor;

		public void GetPixel(int X, int Y, out byte R, out byte G, out byte B) {
			int Offset = Y * Stride + (X * BitsPerPixel / 8);

			switch (Format) {
				case FrameFormat.Any:
				case FrameFormat.Rgb8:
					R = Marshal.ReadByte(Data, Offset);
					G = Marshal.ReadByte(Data, Offset + 1);
					B = Marshal.ReadByte(Data, Offset + 2);
					break;

				case FrameFormat.Bgr8:
					B = Marshal.ReadByte(Data, Offset);
					G = Marshal.ReadByte(Data, Offset + 1);
					R = Marshal.ReadByte(Data, Offset + 2);
					break;

				default:
					throw new Exception("Unsupported format " + Format);
			}
		}

		public void GetPixel(int X, int Y, out ushort Depth) {
			int Offset = Y * Stride + (X * BitsPerPixel / 8);

			switch (Format) {
				case FrameFormat.Any:
				case FrameFormat.Z16:
					Depth = (ushort)Marshal.ReadInt16(Data, Offset);
					break;

				default:
					throw new Exception("Unsupported format " + Format);
			}
		}

		public override string ToString() {
			return string.Format("({0}) {1}x{2} {3} {4}", Idx, Width, Height, Type, Format);
		}
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

		public static void EnableStream(FrameData Data) {
			EnableStream(Data.Type, Data.Width, Data.Height, Data.Format);
		}

		public static void EnableStream(params FrameData[] Data) {
			foreach (var D in Data)
				EnableStream(D);
		}

		public static string GetOptions(FrameData Sensor) {
			Sensor.CameraOption[] Options = Sensor.DataSensor.Options.Where(Opt => !Opt.ReadOnly).ToArray();
			return MicroConfig.Serialize(Options.Select(O => (object)O.Key).ToArray(), Options.Select(O => (object)O.Value).ToArray());
		}

		public static void SetOptions(FrameData Sensor, string Data) {
			MicroConfig.Deserialize(Data, typeof(Option), typeof(float), out object[] Keys, out object[] Values);


			for (int i = 0; i < Keys.Length; i++)
				Sensor.DataSensor.Options[(Option)Keys[i]].Value = (float)Values[i];
		}

		/*public static void SetOptions(FrameData Sensor, params Tuple<Option, float>[] Options) {
			foreach (var Opt in Options)
				Sensor.DataSensor.Options[Opt.Item1].Value = Opt.Item2;
		}*/

		public static void SetOption(FrameData Sensor, int Option, float Val) {
			Sensor.DataSensor.Options[(Option)Option].Value = Val;
		}

		public static IEnumerable<FrameData> QueryResolutions() {
			Device[] Devices = Ctx.Devices.ToArray();

			foreach (var Device in Devices) {
				foreach (var Sensor in Device.Sensors) {
					foreach (var P in Sensor.VideoStreamProfiles) {
						using (P) {
							yield return new FrameData() {
								Format = (FrameFormat)P.Format,
								Width = P.Width,
								Height = P.Height,
								Type = (FrameType)P.Stream,
								Idx = P.Index,
								DataSensor = Sensor
							};
						}
					}
				}
			}
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
							Idx = AllFrames[i].Profile.Index,
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