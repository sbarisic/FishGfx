using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace FishGfx {
	public static unsafe partial class GfxUtils {
		static Random Rnd = new Random();

		public static byte RandomByte() {
			return (byte)Rnd.Next(256);
		}

		public static Color RandomColor() {
			return new Color(RandomByte(), RandomByte(), RandomByte());
		}

		public static float Clamp(this float Num, float Min, float Max) {
			if (Num < Min)
				return Min;

			if (Num > Max)
				return Max;

			return Num;
		}

		public static int Clamp(this int Num, int Min, int Max) {
			if (Num < Min)
				return Min;

			if (Num > Max)
				return Max;

			return Num;
		}

		public static double Clamp(this double Num, double Min, double Max) {
			if (Num < Min)
				return Min;

			if (Num > Max)
				return Max;

			return Num;
		}

		public static long Clamp(this long Num, long Min, long Max) {
			if (Num < Min)
				return Min;

			if (Num > Max)
				return Max;

			return Num;
		}

		public static Vector3 Round(this Vector3 V) {
			return new Vector3((int)V.X, (int)V.Y, (int)V.Z);
		}

		public static Vector2 Round(this Vector2 V) {
			return new Vector2((int)V.X, (int)V.Y);
		}

		public static Vector2 GetWidth(this Vector2 V) {
			return new Vector2(V.X, 0);
		}

		public static Vector3 GetWidth(this Vector3 V) {
			return new Vector3(V.X, 0, 0);
		}

		public static Vector2 GetHeight(this Vector2 V) {
			return new Vector2(0, V.Y);
		}

		public static Vector3 GetHeight(this Vector3 V) {
			return new Vector3(0, V.Y, 0);
		}

		public static Vector3 GetDepth(this Vector3 V) {
			return new Vector3(0, 0, V.Z);
		}

		public static Vector3 Abs(this Vector3 V) {
			return new Vector3(Math.Abs(V.X), Math.Abs(V.Y), Math.Abs(V.Z));
		}

		public static Vector2 Abs(this Vector2 V) {
			return new Vector2(Math.Abs(V.X), Math.Abs(V.Y));
		}

		public static Vector2 Max(this Vector2 V, Vector2 V2) {
			return new Vector2(Math.Max(V.X, V2.X), Math.Max(V.Y, V2.Y));
		}

		public static Vector2 Min(this Vector2 V, Vector2 V2) {
			return new Vector2(Math.Min(V.X, V2.X), Math.Min(V.Y, V2.Y));
		}

		public static Vector3 Max(this Vector3 V, Vector3 V2) {
			return new Vector3(Math.Max(V.X, V2.X), Math.Max(V.Y, V2.Y), Math.Max(V.Z, V2.Z));
		}

		public static Vector3 Min(this Vector3 V, Vector3 V2) {
			return new Vector3(Math.Min(V.X, V2.X), Math.Min(V.Y, V2.Y), Math.Min(V.Z, V2.Z));
		}

		public static float MaxElement(this Vector3 V) {
			return Math.Max(Math.Max(V.X, V.Y), V.Z);
		}

		public static float MinElement(this Vector3 V) {
			return Math.Min(Math.Min(V.X, V.Y), V.Z);
		}

		public static Vector3 XYZ(this Vector4 V) {
			return new Vector3(V.X, V.Y, V.Z);
		}

		public static Vector2 XY(this Vector4 V) {
			return new Vector2(V.X, V.Y);
		}

		public static Vector3 XZY(this Vector3 Vec) {
			return new Vector3(Vec.X, Vec.Z, Vec.Y);
		}

		public static Vector3 ZYX(this Vector3 V) {
			return new Vector3(V.Z, V.Y, V.X);
		}

		public static Vector2 XY(this Vector3 V) {
			return new Vector2(V.X, V.Y);
		}

		public static Vector2 XZ(this Vector3 V) {
			return new Vector2(V.X, V.Z);
		}

		public static Vector3 YZX(this Vector3 V) {
			return new Vector3(V.Y, V.Z, V.X);
		}

		public static float[] Multiply(this float[] FloatArr, float Val) {
			for (int i = 0; i < FloatArr.Length; i++)
				FloatArr[i] *= Val;

			return FloatArr;
		}

		public static float Copysign(this float F, float SignVal) {
			if ((SignVal < 0 && F > 0) || (SignVal > 0 && F < 0))
				return -F;

			return F;
		}

		public static void GetEulerAngles(this Quaternion Quat, out float Pitch, out float Yaw, out float Roll) {
			double SqW = Quat.W * Quat.W;
			double SqX = Quat.X * Quat.X;
			double SqY = Quat.Y * Quat.Y;
			double SqZ = Quat.Z * Quat.Z;
			double Test = Quat.X * Quat.Y + Quat.Z * Quat.W;

			if (Test > 0.49999)  // Singularity at north pole
				Yaw = (float)(2 * Math.Atan2(Quat.X, Quat.W));
			else if (Test < -0.49999)  // Singularity at south pole
				Yaw = (float)(-2 * Math.Atan2(Quat.X, Quat.W));
			else
				Yaw = (float)Math.Atan2(2 * Quat.Y * Quat.W - 2 * Quat.X * Quat.Z, SqX - SqY - SqZ + SqW);

			Yaw *= (float)(180.0 / Math.PI);
			if (Yaw < 0)
				Yaw += 360;

			Pitch = (float)-Math.Atan2(2.0 * Quat.X * Quat.W + 2.0 * Quat.Y * Quat.Z, 1.0 - 2.0 * (SqZ + SqW));
			Pitch *= (float)(180.0 / Math.PI);

			if (Yaw > 270 || Yaw < 90)
				if (Pitch < 0)
					Pitch += 180;
				else
					Pitch -= 180;

			// TODO: Stop, drop and ROLL baby
			Roll = 0;
		}

		public static void Deconstruct(this Vector3 V, out float X, out float Y, out float Z) {
			X = V.X;
			Y = V.Y;
			Z = V.Z;
		}

		public static void Deconstruct(this Quaternion Q, out float Pitch, out float Yaw, out float Roll) {
			Q.GetEulerAngles(out Pitch, out Yaw, out Roll);
		}

		public static void Deconstruct(this Quaternion Q, out float W, out float X, out float Y, out float Z) {
			W = Q.W;
			X = Q.X;
			Y = Q.Y;
			Z = Q.Z;
		}

		public static float ParseFloat(this string Str, float Default) {
			if (string.IsNullOrWhiteSpace(Str))
				return Default;

			return Str.ParseFloat();
		}

		public static float ParseFloat(this string Str) {
			return float.Parse(Str, CultureInfo.InvariantCulture);
		}

		public static int ParseInt(this string Str, int Default) {
			if (string.IsNullOrWhiteSpace(Str))
				return Default;

			return Str.ParseInt();
		}

		public static int ParseInt(this string Str) {
			return int.Parse(Str, CultureInfo.InvariantCulture);
		}

		public static void WriteStruct<T>(this BinaryWriter Writer, T Val) where T : struct {
			GCHandle Handle = GCHandle.Alloc(Val, GCHandleType.Pinned);
			IntPtr Ptr = Handle.AddrOfPinnedObject();
			int Len = Marshal.SizeOf<T>();

			byte[] Bytes = new byte[Len];
			Marshal.Copy(Ptr, Bytes, 0, Bytes.Length);
			Writer.Write(Bytes);

			Handle.Free();
		}

		public static T ReadStruct<T>(this BinaryReader Reader) where T : struct {
			T Val = default(T);
			GCHandle Handle = GCHandle.Alloc(Val, GCHandleType.Pinned);
			IntPtr Ptr = Handle.AddrOfPinnedObject();
			int Len = Marshal.SizeOf<T>();

			byte[] Bytes = new byte[Len];
			Reader.Read(Bytes, 0, Bytes.Length);
			Marshal.Copy(Bytes, 0, Ptr, Len);

			Handle.Free();
			return Val;
		}

		public static Matrix4x4 ToRotation(this Vector3 Normal) {
			Vector3 UpVector = Vector3.UnitY;
			Vector3 H = Vector3.Normalize(UpVector + Normal);

			Quaternion Q = Quaternion.Identity;
			Q.W = Vector3.Dot(UpVector, H);
			Q.X = UpVector.Y * H.Z - UpVector.Z * H.Y;
			Q.Y = UpVector.Z * H.X - UpVector.X * H.Z;
			Q.Z = UpVector.X * H.Y - UpVector.Y * H.X;

			return Matrix4x4.CreateFromQuaternion(Q);
		}

		public static void NormalToPitchYaw(this Vector3 Normal, out float Pitch, out float Yaw) {
			Pitch = (float)Math.Asin(-Normal.Y);
			Yaw = (float)Math.Atan2(Normal.X, Normal.Z);
		}
	}
}
