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
	}
}
