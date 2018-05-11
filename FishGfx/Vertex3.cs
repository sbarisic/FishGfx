using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;

namespace FishGfx {
	public struct Vertex3 {
		public Vector3 Position;
		public Vector2 UV;
		public Color Color;

		public Vertex3(Vector3 Pos) {
			Position = Pos;
			UV = new Vector2(0, 0);
			Color = Color.White;
		}

		public static implicit operator Vertex3(Vector3 Pos) {
			return new Vertex3(Pos);
		}

		public static Vertex3[] FromFloatArray(float[] Positions) {
			Vertex3[] Result = new Vertex3[Positions.Length / 3];

			for (int i = 0; i < Result.Length; i++) {
				int idx = i * 3;
				Result[i] = new Vertex3(new Vector3(Positions[idx], Positions[idx + 1], Positions[idx + 2]));
			}

			return Result;
		}
	}
}
