using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Numerics;
using System.Threading.Tasks;
using Intel.RealSense;

namespace FishGfx.RealSense {
	public delegate void OnFramesReceived(params FrameData[] Frames);
	public delegate Vertex3[] OnPointCloudReceived(int Count, Vertex3[] Verts, FrameData[] Frames);

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

	public enum RealSenseOption {
		BacklightCompensation = 0,
		Brightness = 1,
		Contrast = 2,
		Exposure = 3,
		Gain = 4,
		Gamma = 5,
		Hue = 6,
		Saturation = 7,
		Sharpness = 8,
		WhiteBalance = 9,
		EnableAutoExposure = 10,
		EnableAutoWhiteBalance = 11,
		VisualPreset = 12,
		LaserPower = 13,
		Accuracy = 14,
		MotionRange = 15,
		FilterOption = 16,
		ConfidenceThreshold = 17,
		EmitterEnabled = 18,
		FramesQueueSize = 19,
		TotalFrameDrops = 20,
		AutoExposureMode = 21,
		PowerLineFrequency = 22,
		AsicTemperature = 23,
		ErrorPollingEnabled = 24,
		ProjectorTemperature = 25,
		OutputTriggerEnabled = 26,
		MotionModuleTemperature = 27,
		DepthUnits = 28,
		EnableMotionCorrection = 29,
		AutoExposurePriority = 30,
		ColorScheme = 31,
		HistogramEqualizationEnabled = 32,
		MinDistance = 33,
		MaxDistance = 34,
		TextureSource = 35,
		FilterMagnitude = 36,
		FilterSmoothAlpha = 37,
		FilterSmoothDelta = 38,
		HolesFill = 39,
		StereoBaseline = 40,
		AutoExposureConvergeStep = 41
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

		public Color GetPixel(Vector2 UV, bool Clamp = true) {
			GetPixel((int)(UV.X * Width), (int)(UV.Y * Height), out byte R, out byte G, out byte B, Clamp);
			return new Color(R, G, B);
		}

		public void GetPixel(int X, int Y, out byte R, out byte G, out byte B, bool Clamp = true) {
			if (Clamp) {
				if (X < 0)
					X = 0;
				if (Y < 0)
					Y = 0;
				if (X >= Width)
					X = Width - 1;
				if (Y >= Height)
					Y = Height - 1;
			} else {
				if (X < 0) {
					R = G = B = 0;
					return;
				}
				if (Y < 0) {
					R = G = B = 0;
					return;
				}
				if (X >= Width) {
					R = G = B = 0;
					return;
				}
				if (Y >= Height) {
					R = G = B = 0;
					return;
				}
			}

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

	public static unsafe class RealSenseCamera {
		static object ThreadLock = new object();

		static Context Ctx = new Context();
		static Config Cfg = new Config();
		static Pipeline Pipeline = new Pipeline(Ctx);

		static PipelineProfile Profile;
		static PointCloud PointCloud;

		static Points.Vertex[] PointVerts = new Points.Vertex[0];
		static Points.TextureCoordinate[] PointUVs = new Points.TextureCoordinate[0];

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

		public static void SetOption(FrameData Sensor, RealSenseOption Option, float Val) {
			SetOption(Sensor, (int)Option, Val);
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
			lock (ThreadLock) {
				if (Profile != null)
					throw new Exception("Already started");

				Profile = Pipeline.Start(Cfg);
			}
		}

		public static void Stop() {
			lock (ThreadLock) {
				if (Profile == null)
					throw new Exception("Already stopped");

				Pipeline.Stop();
				Profile = null;
			}
		}

		public static bool PollForFrames(OnFramesReceived OnFrames = null, OnPointCloudReceived OnPointCloud = null) {
			lock (ThreadLock) {
				if (Profile == null)
					return false;

				if (Pipeline.PollForFrames(out FrameSet Frames)) {
					using (Frames) {
						Frame[] AllFrames = Frames.ToArray();
						FrameData[] FrameData = new FrameData[AllFrames.Length];

						VideoFrame Video = null;
						DepthFrame Depth = null;

						//Parallel.For(0, FrameData.Length, (i) => {
						for (int i = 0; i < FrameData.Length; i++) {
							VideoFrame Vid = AllFrames[i] as VideoFrame;
							DepthFrame Dpt = AllFrames[i] as DepthFrame;
							int W = Vid?.Width ?? Dpt?.Width ?? 0;
							int H = Vid?.Height ?? Dpt?.Height ?? 0;
							int Bpp = Vid?.BitsPerPixel ?? Dpt?.BitsPerPixel ?? 0;
							int Stride = Vid?.Stride ?? Dpt?.Stride ?? 0;

							if (Vid != null)
								Video = Vid;
							if (Dpt != null)
								Depth = Dpt;

							FrameData[i] = new FrameData() {
								Idx = AllFrames[i].Profile.Index,
								BitsPerPixel = Bpp,
								Data = AllFrames[i].Data,
								Width = W,
								Height = H,
								Stride = Stride,
								Type = (FrameType)AllFrames[i].Profile.Stream
							};
							//});
						}

						if (OnPointCloud != null) {
							if (PointCloud == null)
								PointCloud = new PointCloud();

							Vertex3[] Verts;
							int VertCount = 0;

							using (Points Pts = PointCloud.Calculate(Depth)) {
								PointCloud.MapTexture(Video);
								Verts = OnPointCloud(VertCount = Pts.Count, null, null);

								if (PointVerts.Length < VertCount) {
									PointVerts = new Points.Vertex[VertCount];
									PointUVs = new Points.TextureCoordinate[VertCount];
								}

								Pts.CopyTo(PointVerts);
								Pts.CopyTo(PointUVs);
							}

							fixed (Points.Vertex* PointVertsPtr = PointVerts)
							fixed (Points.TextureCoordinate* PointUVsPtr = PointUVs) {
								IntPtr PointVertsIntPtr = (IntPtr)PointVertsPtr;
								IntPtr PointUVsIntPtr = (IntPtr)PointUVsPtr;

								Parallel.For(0, VertCount, (i) => {
									Verts[i] = new Vertex3(*(Vector3*)(PointVertsIntPtr + sizeof(Points.Vertex) * i),
										*(Vector2*)(PointUVsIntPtr + sizeof(Points.TextureCoordinate) * i));
								});
							}

							OnPointCloud(VertCount, Verts, FrameData);
						}

						OnFrames?.Invoke(FrameData);

						for (int i = 0; i < AllFrames.Length; i++)
							AllFrames[i].Dispose();
					}

					return true;
				}

				return false;
			}
		}
	}
}
