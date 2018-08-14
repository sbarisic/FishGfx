using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using FishGfx;

namespace FishGfx.Formats {
	public static class Obj {
		public static void Save(string FileName, IEnumerable<GenericMesh> Meshes) {
			using (StreamWriter SW = new StreamWriter(FileName)) {
				int GlobalVertOffset = 0;

				SW.WriteLine("mtllib " + Path.GetFileNameWithoutExtension(FileName) + ".mtl");

				foreach (var Mesh in Meshes) {
					foreach (var Vert in Mesh.Vertices)
						SW.WriteLine(string.Format(CultureInfo.InvariantCulture, "v {0} {1} {2}", Vert.Position.X, Vert.Position.Y, Vert.Position.Z));

					foreach (var Vert in Mesh.Vertices)
						SW.WriteLine(string.Format(CultureInfo.InvariantCulture, "vt {0} {1}", Vert.UV.X, Vert.UV.Y));


					SW.WriteLine();
					SW.WriteLine("usemtl " + Mesh.MaterialName);

					for (int i = 0; i < Mesh.Vertices.Count; i += 3)
						SW.WriteLine("f {0}/{0} {1}/{1} {2}/{2}", GlobalVertOffset + i + 1, GlobalVertOffset + i + 2, GlobalVertOffset + i + 3);

					GlobalVertOffset += Mesh.Vertices.Count;
				}
			}

			FileName = Path.ChangeExtension(FileName, ".mtl");

			using (StreamWriter SW = new StreamWriter(FileName)) {
				foreach (var Mesh in Meshes) {
					SW.WriteLine("newmtl " + Mesh.MaterialName);
					SW.WriteLine("map_Kd " + Mesh.MaterialName + ".png");
					SW.WriteLine();
				}
			}
		}

		public static GenericMesh[] Load(string FileName) {
			List<GenericMesh> Meshes = new List<GenericMesh>();
			GenericMesh CurMesh = null;

			//List<Vertex3> ObjVertices = new List<Vertex3>();

			string[] Lines = File.ReadAllLines(FileName);
			List<Vector3> Verts = new List<Vector3>();
			List<Vector2> UVs = new List<Vector2>();

			for (int j = 0; j < Lines.Length; j++) {
				string Line = Lines[j].Trim().Replace('\t', ' ');

				while (Line.Contains("  "))
					Line = Line.Replace("  ", " ");

				if (Line.StartsWith("#"))
					continue;

				string[] Tokens = Line.Split(' ');
				switch (Tokens[0].ToLower()) {
					case "o":
						break;

					case "v": // Vertex
						Verts.Add(new Vector3(Tokens[1].ParseFloat(), Tokens[2].ParseFloat(), Tokens[3].ParseFloat()));
						break;

					case "vt": // Texture coordinate
						UVs.Add(new Vector2(Tokens[1].ParseFloat(), Tokens[2].ParseFloat()));
						break;

					case "vn": // Normal
						break;

					case "f": // Face
						if (CurMesh == null) {
							CurMesh = new GenericMesh("default");
							Meshes.Add(CurMesh);
						}

						for (int i = 2; i < Tokens.Length - 1; i++) {
							string[] V = Tokens[1].Split('/');
							CurMesh.Vertices.Add(new Vertex3(Verts[V[0].ParseInt() - 1], UVs[V[1].ParseInt() - 1]));

							V = Tokens[i].Split('/');
							CurMesh.Vertices.Add(new Vertex3(Verts[V[0].ParseInt() - 1], UVs[V[1].ParseInt() - 1]));

							V = Tokens[i + 1].Split('/');
							CurMesh.Vertices.Add(new Vertex3(Verts[V[0].ParseInt() - 1], UVs[V[1].ParseInt() - 1]));
						}

						break;

					case "usemtl":
						CurMesh = Meshes.Where(M => M.MaterialName == Tokens[1]).FirstOrDefault();
						if (CurMesh == null) {
							CurMesh = new GenericMesh(Tokens[1]);
							Meshes.Add(CurMesh);
						}
						break;

					default:
						break;
				}
			}

			return Meshes.ToArray();
		}
	}
}
