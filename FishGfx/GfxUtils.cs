using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;

namespace FishGfx {
	public static partial class GfxUtils {
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

		public static Vector3 XYZ(this Vector4 V) {
			return new Vector3(V.X, V.Y, V.Z);
		}

		public static Vector2 XY(this Vector3 V) {
			return new Vector2(V.X, V.Y);
		}

		public static Vector2 XZ(this Vector3 V) {
			return new Vector2(V.X, V.Z);
		}

		public static Vector2 XY(this Vector4 V) {
			return new Vector2(V.X, V.Y);
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

			if (Test > 0.49999)  // singularity at north pole
				Yaw = (float)(2 * Math.Atan2(Quat.X, Quat.W));
			else if (Test < -0.49999)  // singularity at south pole
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
	}
}
