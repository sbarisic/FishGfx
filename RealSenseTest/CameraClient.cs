using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;
using System.Net.Sockets;
using System.Net;
using System.Runtime.InteropServices;
using FishGfx;
using System.Diagnostics;

namespace RealSenseTest {
	public static unsafe class CameraClient {
		static UdpClient UDP;
		static int Port = 40023;

		public static Vector3 MarkerA;
		public static Vector3 MarkerB;
		public static Vector3 MarkerC;
		public static bool Visible;

		public static void Init() {
			Thread WorkerThread = new Thread(() => {
				if (!Debug.FakePosition) {
					UDP = new UdpClient(Port);
					UDP.DontFragment = true;
				} else {
					MarkerA = MarkerB = MarkerC = new Vector3(0, 100, 0);
				}

				while (true)
					ReceiveVectors();
			});
			WorkerThread.IsBackground = true;
			WorkerThread.Start();
		}

		public static byte[] ReceiveRaw() {
			IPEndPoint Sender = new IPEndPoint(IPAddress.Any, Port);
			return UDP.Receive(ref Sender);
		}

		public static Vector3 GetPos() {
			// TODO: ?
			//return ((MarkerA + MarkerB + MarkerC) / 3) - new Vector3(-83, 0, -215);

			return ((MarkerA + MarkerB + MarkerC) / 3);
		}

		public static Matrix4x4 GetTranslation() {
			return Matrix4x4.CreateTranslation(GetPos());
		}

		public static Vector3 GetNormal() {
			if (Debug.FakePosition)
				return Vector3.Normalize(new Vector3(-1, 0.3f, 0.5f));

			Vector3 U = MarkerB - MarkerA;
			Vector3 V = MarkerC - MarkerA;

			float X = U.Y * V.Z - U.Z * V.Y;
			float Y = U.Z * V.X - U.X * V.Z;
			float Z = U.X * V.Y - U.Y * V.X;
			return Vector3.Normalize(new Vector3(X, Y, Z));
		}

		public static Matrix4x4 GetRotation() {
			GetRotationAngles(out float Yaw, out float Pitch, out float Roll);
			return Matrix4x4.CreateFromYawPitchRoll(Yaw, Pitch, Roll);
		}

		public static void GetRotationAngles(out float Yaw, out float Pitch, out float Roll) {
			GetNormal().NormalToPitchYaw(out Pitch, out Yaw);
			Matrix4x4.Invert(Matrix4x4.CreateFromYawPitchRoll(Yaw, Pitch, 0), out Matrix4x4 InvYawPitch);

			Vector3 A = Vector3.Transform(MarkerA, InvYawPitch);
			Vector3 B = Vector3.Transform(MarkerB, InvYawPitch);
			Vector3 C = Vector3.Transform(MarkerC, InvYawPitch);
			Vector3 Center = (A + B + C) / 3;

			float XDiff = Center.X - A.X;
			float YDiff = Center.Y - A.Y;
			Roll = (float)(Math.Atan2(YDiff, XDiff) + Math.PI / 2);
		}

		static bool IsVisible(Vector3 V) {
			const float Threshold = 10000;

			if (V.X > Threshold || V.X < -Threshold)
				return false;

			return true;
		}

		static Stopwatch SWatch;

		public static void ReceiveVectors() {
			if (Debug.FakePosition) {
				if (SWatch == null)
					SWatch = Stopwatch.StartNew();

				float Rad = 300;
				float TS = 1000.0f;
				Vector3 Pos = new Vector3((float)Math.Sin(SWatch.ElapsedMilliseconds / TS) * Rad, 100, (float)Math.Cos(SWatch.ElapsedMilliseconds / TS) * Rad);

				MarkerA = MarkerB = MarkerC = Pos;
				return;
			}

			byte[] Bytes = ReceiveRaw();

			//Console.WriteLine(Bytes.Length);

			Vector3* Vectors = stackalloc Vector3[3];
			Marshal.Copy(Bytes, 0, new IntPtr(Vectors), 3 * 3 * sizeof(float));

			Vector3 A = Vectors[0];
			Vector3 B = Vectors[1];
			Vector3 C = Vectors[2];

			if (IsVisible(A))
				MarkerA = A.YZX();

			if (IsVisible(B))
				MarkerB = B.YZX();

			if (IsVisible(C))
				MarkerC = C.YZX();
		}
	}
}
