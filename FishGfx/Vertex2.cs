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

		public Vertex2(Vector2 Pos, Vector2 UV, Color Clr) {
			Position = Pos;
			this.UV = UV;
			Color = Clr;
		}

		public Vertex2(Vector2 Pos, Vector2 UV) : this(Pos, UV, Color.White) {
		}

		public Vertex2(Vector2 Pos, Color Clr) : this(Pos, Vector2.Zero, Clr) {
		}

		public Vertex2(Vector2 Pos) : this(Pos, new Vector2(0, 0)) {
		}

		public Vertex2(float X, float Y) : this(new Vector2(X, Y)) {
		}

		public static implicit operator Vertex2(Vector2 Pos) {
			return new Vertex2(Pos);
		}

		public static IEnumerable<Vertex2> CreateQuad(Vector2 Pos, Vector2 Size, Vector2 UV, Vector2 UVSize, Color Clr) {
			yield return new Vertex2(Pos, UV, Clr);
			yield return new Vertex2(Pos + Size.GetHeight(), UV + UVSize.GetHeight(), Clr);
			yield return new Vertex2(Pos + Size, UV + UVSize, Clr);
			yield return new Vertex2(Pos, UV, Clr);
			yield return new Vertex2(Pos + Size, UV + UVSize, Clr);
			yield return new Vertex2(Pos + Size.GetWidth(), UV + UVSize.GetWidth(), Clr);
		}

		public static IEnumerable<Vertex2> CreateQuad(Vector2 Pos, Vector2 Size, Vector2 UV, Vector2 UVSize) {
			return CreateQuad(Pos, Size, UV, UVSize, Color.White);
		}
	}
}
