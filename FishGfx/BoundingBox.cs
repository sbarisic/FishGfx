using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;

namespace FishGfx {
	public struct BoundingBox {
		public static readonly BoundingBox Empty = new BoundingBox(new Vector3(float.PositiveInfinity), new Vector3(float.NegativeInfinity));

		public Vector3 PositionMin;
		public Vector3 PositionMax;

		public Vector3 Size {
			get {
				return PositionMax - PositionMin;
			}
		}

		public BoundingBox(Vector3 Min, Vector3 Max) {
			PositionMin = Min;
			PositionMax = Max;
		}

		public BoundingBox Inflate(Vector3 Point) {
			BoundingBox New = this;

			if (Point.X < New.PositionMin.X)
				New.PositionMin.X = Point.X;

			if (Point.Y < New.PositionMin.Y)
				New.PositionMin.Y = Point.Y;

			if (Point.Z < New.PositionMin.Z)
				New.PositionMin.Z = Point.Z;

			if (Point.X > New.PositionMax.X)
				New.PositionMax.X = Point.X;

			if (Point.Y > New.PositionMax.Y)
				New.PositionMax.Y = Point.Y;

			if (Point.Z > New.PositionMax.Z)
				New.PositionMax.Z = Point.Z;

			return New;
		}

		public override string ToString() {
			return string.Format("({0} .. {1})", PositionMin, PositionMax);
		}
	}
}
