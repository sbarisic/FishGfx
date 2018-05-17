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

		public Vertex2(Vector2 Pos, Vector2 UV) {
			Position = Pos;
			this.UV = UV;
			Color = Color.White;
		}

		public Vertex2(Vector2 Pos) : this(Pos, new Vector2(0, 0)) {
		}

		public static implicit operator Vertex2(Vector2 Pos) {
			return new Vertex2(Pos);
		}
	}
}
