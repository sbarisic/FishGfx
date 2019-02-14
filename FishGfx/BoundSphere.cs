using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace FishGfx {
	public struct BoundSphere {
		public static readonly BoundSphere Empty = new BoundSphere(Vector3.Zero, 0);

		public Vector3 Position;
		public float Radius;

		public BoundSphere(Vector3 Position, float Radius) {
			this.Position = Position;
			this.Radius = Radius;
		}

		public bool Collide(BoundSphere Other) {
			return Vector3.Distance(Position, Other.Position) < (Radius + Other.Radius);
			//return true;
		}

		public static BoundSphere FromPointCloud(IEnumerable<Vector3> Points) {
			return FromAABB(AABB.CalculateAABB(Points));
		}

		public static BoundSphere FromAABB(AABB Other) {
			return new BoundSphere(Other.Center, Vector3.Distance(Vector3.Zero, Other.Bounds / 2));
		}

		public static BoundSphere operator +(BoundSphere Sphere, Vector3 Point) {
			return new BoundSphere(Sphere.Position + Point, Sphere.Radius);
		}
	}
}
