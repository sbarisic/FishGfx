using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using FishGfx;

namespace FishGfx.Formats {
	public class GenericMesh {
		public string MaterialName;
		public List<Vertex3> Vertices;

		public GenericMesh(string MaterialName) {
			Vertices = new List<Vertex3>();
			this.MaterialName = MaterialName;
		}

		public GenericMesh(Vertex3[] Verts, string MaterialName = "none") : this(MaterialName) {
			Vertices.AddRange(Verts);
		}

		public void CalculateBoundingBox(out Vector3 Min, out Vector3 Max) {
			Min = Vertices[0].Position;
			Max = Vertices[0].Position;

			foreach (var Vtx in Vertices) {
				Min.X = Math.Min(Min.X, Vtx.Position.X);
				Min.Y = Math.Min(Min.Y, Vtx.Position.Y);
				Min.Z = Math.Min(Min.Z, Vtx.Position.Z);

				Max.X = Math.Max(Max.X, Vtx.Position.X);
				Max.Y = Math.Max(Max.Y, Vtx.Position.Y);
				Max.Z = Math.Max(Max.Z, Vtx.Position.Z);
			}
		}

		public void CalculateBoundingSphere(out Vector3 Pos, out float Radius) {
			CalculateBoundingBox(out Vector3 Min, out Vector3 Max);
			Pos = Min + ((Max - Min) / 2);
			Radius = ((Max - Min) / 2).MaxElement();
		}

		public void SwapYZ() {
			for (int i = 0; i < Vertices.Count; i++)
				Vertices[i] = new Vertex3(Vertices[i].Position.XZY() * new Vector3(-1, 1, 1), Vertices[i].UV, Vertices[i].Color);
		}

		public void SwapWindingOrder() {
			for (int i = 0; i < Vertices.Count; i += 3) {
				Vertex3 Vtx = Vertices[i];
				Vertices[i] = Vertices[i + 1];
				Vertices[i + 1] = Vtx;
			}
		}

		public override string ToString() {
			return string.Format("{0} ({1} verts)", MaterialName, Vertices?.Count ?? 0);
		}
	}
}
