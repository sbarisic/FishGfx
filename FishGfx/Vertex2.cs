using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;

namespace FishGfx {
	public struct Vertex2 {
		public Vector2 Position;
		public Vector2 UV;
		public Color Color;

		public Vertex2(Vector2 Pos) {
			Position = Pos;
			UV = new Vector2(0, 0);
			Color = Color.White;
		}

		public static implicit operator Vertex2(Vector2 Pos) {
			return new Vertex2(Pos);
		}
	}
}
