using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;
using System.Globalization;
using System.IO;

namespace FishGfx {
	public static partial class GfxUtils {
		static float ParseFloat(this string Str) {
			return float.Parse(Str, CultureInfo.InvariantCulture);
		}

		static int ParseInt(this string Str) {
			return int.Parse(Str, CultureInfo.InvariantCulture);
		}

		public static Vertex3[] LoadObj(string FileName) {
			List<Vertex3> ObjVertices = new List<Vertex3>();
			string[] Lines = File.ReadAllLines(FileName);

			List<Vector3> Verts = new List<Vector3>();
			List<Vector2> UVs = new List<Vector2>();

			foreach (var Line in Lines) {
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
						for (int i = 2; i < Tokens.Length - 1; i++) {
							string[] V = Tokens[1].Split('/');
							ObjVertices.Add(new Vertex3(Verts[V[0].ParseInt() - 1], UVs[V[1].ParseInt() - 1]));

							V = Tokens[i].Split('/');
							ObjVertices.Add(new Vertex3(Verts[V[0].ParseInt() - 1], UVs[V[1].ParseInt() - 1]));

							V = Tokens[i + 1].Split('/');
							ObjVertices.Add(new Vertex3(Verts[V[0].ParseInt() - 1], UVs[V[1].ParseInt() - 1]));
						}

						break;

					default:
						break;
				}
			}

			return ObjVertices.ToArray();
		}
	}
}
