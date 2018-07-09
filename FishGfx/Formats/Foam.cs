using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Threading.Tasks;
using System.IO;
using System.Runtime.InteropServices;
using System.Numerics;

namespace FishGfx.Formats {
	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	struct FoamHeader {
		public int Magic;
		public int Version;
	}

	enum FoamDataType : int {
		Unknown = 0,
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	struct FoamBinaryData {
		public FoamDataType DataType;
		public byte[] Data;
	}

	[Flags]
	enum FoamVertexFlags : int {
		None = 1 << 0,
		Position = 1 << 1,
		UV = 1 << 2,
		Color = 1 << 3,
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	struct FoamVertexData {
		public FoamVertexFlags Flags;
		public int Count;
		public Vector3[] Positions;
		public Vector2[] UVs;
		public Color[] Colors;
	}

	public class Foam {
		const int Version = 1;

		public static void Save(string FileName, Vertex3[] Verts) {
			Foam F = new Foam();
			F.SetVerts(Verts);

			using (FileStream FS = File.OpenWrite(FileName))
				F.WriteToStream(FS);
		}

		public static Vertex3[] Load(string FileName) {
			return null;
		}

		FoamHeader Header;
		FoamVertexData Vertices;

		public Foam() {
			Header = new FoamHeader();
			Header.Magic = BitConverter.ToInt32(Encoding.ASCII.GetBytes("FOAM"), 0);
			Header.Version = Version;

			Vertices = new FoamVertexData();
		}

		public void SetVerts(Vertex3[] Verts) {
			Vertices.Flags = FoamVertexFlags.Position | FoamVertexFlags.UV | FoamVertexFlags.Color;
			Vertices.Count = Verts.Length;
			Vertices.Positions = new Vector3[Verts.Length];
			Vertices.UVs = new Vector2[Verts.Length];
			Vertices.Colors = new Color[Verts.Length];

			for (int i = 0; i < Verts.Length; i++) {
				Vertices.Positions[i] = Verts[i].Position;
				Vertices.UVs[i] = Verts[i].UV;
				Vertices.Colors[i] = Verts[i].Color;
			}
		}

		public Vertex3[] GetVerts() {
			Vertex3[] Verts = new Vertex3[Vertices.Count];

			for (int i = 0; i < Verts.Length; i++) {
				if (Vertices.Flags.HasFlag(FoamVertexFlags.Position))
					Verts[i].Position = Vertices.Positions[i];

				if (Vertices.Flags.HasFlag(FoamVertexFlags.UV))
					Verts[i].UV = Vertices.UVs[i];

				if (Vertices.Flags.HasFlag(FoamVertexFlags.Color))
					Verts[i].Color = Vertices.Colors[i];
			}

			return Verts;
		}

		public void WriteToStream(Stream S) {
			using (BinaryWriter BW = new BinaryWriter(S)) {
				BW.WriteStruct(Header);

				BW.Write((int)Vertices.Flags);
				BW.Write(Vertices.Count);

				if (Vertices.Flags.HasFlag(FoamVertexFlags.Position))
					for (int i = 0; i < Vertices.Count; i++)
						BW.WriteStruct(Vertices.Positions[i]);

				if (Vertices.Flags.HasFlag(FoamVertexFlags.UV))
					for (int i = 0; i < Vertices.Count; i++)
						BW.WriteStruct(Vertices.UVs[i]);

				if (Vertices.Flags.HasFlag(FoamVertexFlags.Color))
					for (int i = 0; i < Vertices.Count; i++)
						BW.WriteStruct(Vertices.Colors[i]);
			}
		}
	}
}
