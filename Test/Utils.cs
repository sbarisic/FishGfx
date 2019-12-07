using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;

namespace Test {
	static class Utils {
		static void Swap<T>(ref T A, ref T B) {
			T Tmp = A;
			A = B;
			B = Tmp;
		}

		public static Vector2 Round(Vector2 Vec) {
			return new Vector2((int)Vec.X, (int)Vec.Y);
		}

		public static float DistanceX(Vector2 A, Vector2 B, out float SignedDist) {
			return Math.Abs(SignedDist = (A.X - B.X));
		}

		public static IEnumerable<Vector2> BresenhamLine(int X0, int Y0, int X1, int Y1) {
			bool Steep = Math.Abs(Y1 - Y0) > Math.Abs(X1 - X0);

			if (Steep) {
				Swap(ref X0, ref Y0);
				Swap(ref X1, ref Y1);
			}

			if (X0 > X1) {
				Swap(ref X0, ref X1);
				Swap(ref Y0, ref Y1);
			}

			int DtX = X1 - X0;
			int DtY = Math.Abs(Y1 - Y0);
			int Error = 0;
			int YStep;
			int Y = Y0;

			if (Y0 < Y1)
				YStep = 1;
			else
				YStep = -1;
			for (int X = X0; X <= X1; X++) {
				if (Steep)
					yield return new Vector2(Y, X);
				else
					yield return new Vector2(X, Y);

				Error += DtY;

				if (2 * Error >= DtX) {
					Y += YStep;
					Error -= DtX;
				}
			}
		}

		public static IEnumerable<Vector2> BresenhamLine(Vector2 Start, Vector2 End) {
			return BresenhamLine((int)Start.X, (int)Start.Y, (int)End.X, (int)End.Y);
		}
	}
}
