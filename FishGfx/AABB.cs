using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;

namespace FishGfx {
	public struct AABB {
		public Vector3 Position;
		public Vector3 Size;

		public AABB(Vector3 Position, Vector3 Size) {
			this.Position = Position;
			this.Size = Size;
		}

		public AABB(Vector2 Position, Vector2 Size) : this(new Vector3(Position, 0), new Vector3(Size, 0)) {
		}

		public bool Collide(AABB Other) {
			foreach (var BoxVert in Other.GetVertices())
				if (IsInside(BoxVert)) 
					return true;

			foreach (var Vert in GetVertices())
				if (Other.IsInside(Vert))
					return true;

			return false;
		}

		public bool IsInside(Vector3 Point) {
			Vector3 Max = Position + Size;

			if (Point.X >= Position.X && Point.X <= Max.X)
				if (Point.Y >= Position.Y && Point.Y <= Max.Y)
					if (Point.Z >= Position.Z && Point.Z <= Max.Z)
						return true;

			return false;
		}

		public bool IsInside(Vector2 Point) {
			return IsInside(new Vector3(Point, 0));
		}

		public IEnumerable<Vector3> GetVertices() {
			yield return Position;
			yield return Position + Size;
			yield return Position + Size.GetWidth();
			yield return Position + Size.GetHeight();
			yield return Position + Size.GetDepth();
			yield return Position + Size.GetDepth() + Size.GetHeight();
			yield return Position + Size.GetWidth() + Size.GetHeight();
			yield return Position + Size.GetDepth() + Size.GetWidth();
		}

		/*public bool Union(AABB Box, out AABB Union) {
			bool AnyIntersecting = false;

			foreach (var BoxVert in Box.GetVertices())
				if (IsInside(BoxVert)) {
					AnyIntersecting = true;
					break;
				}

			if (!AnyIntersecting)
				foreach (var Vert in GetVertices()) {
					if (Box.IsInside(Vert)) {
						AnyIntersecting = true;
						break;
					}
				}

			if (!AnyIntersecting) {
				Union = new AABB(Vector3.Zero, Vector3.Zero);
				return false;
			}

			Vector3 A = Position.Max(Box.Position);
			Vector3 B = (Position + Size).Max(Box.Position + Box.Size);

			Union = new AABB(Position.Max(Box.Position), B - A);
			return true;
		}*/

		public AABB Intersection(AABB Other) {
			if (!Collide(Other))
				return new AABB(Vector3.Zero, Vector3.Zero);

			Vector3 A = Position.Max(Other.Position);
			Vector3 B = (Position + Size).Min(Other.Position + Other.Size);
			return new AABB(Position.Max(Other.Position), B - A);
		}

		public override string ToString() {
			return string.Format("{0} .. {2} ({1})", Position, Size, Position + Size);
		}
	}
}
