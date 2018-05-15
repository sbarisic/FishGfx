using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;

namespace FishGfx {
	public static class GfxUtils {
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
	}
}
