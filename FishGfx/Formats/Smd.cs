using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using FishGfx;

namespace FishGfx.Formats {
	enum SMDSegment {
		End,
		Nodes,
		Skeleton,
		Triangles,
	}

	public struct SmdSkeletonFrame {
		public int Time;
		public Vector3 Position;
		public Vector3 Rotation;

		public SmdSkeletonFrame(int Time, Vector3 Position, Vector3 Rotation) {
			this.Time = Time;
			this.Position = Position;
			this.Rotation = Rotation;
		}

		public SmdSkeletonFrame(int Time, SmdSkeletonFrame Clone) {
			this.Time = Time;
			Position = Clone.Position;
			Rotation = Clone.Rotation;
		}

		public SmdSkeletonFrame Set(Vector3 Position, Vector3 Rotation) {
			this.Position = Position;
			this.Rotation = Rotation;
			return this;
		}

		public override string ToString() {
			return string.Format("{0} {1} {2}", Time, Position, Rotation);
		}
	}

	public class SmdNode {
		public int ID;
		public string Name;

		public List<SmdNode> Children;
		public List<SmdSkeletonFrame> AnimFrames;

		public SmdNode(int ID, string Name) {
			this.ID = ID;
			this.Name = Name;

			Children = new List<SmdNode>();
			AnimFrames = new List<SmdSkeletonFrame>();
		}

		public void Add(SmdNode Child) {
			Children.Add(Child);
		}

		public override string ToString() {
			return string.Format("{0} - {1}", ID, Name);
		}
	}

	public struct SmdTriangle {
		public string Material;

		public int[] ParentBone;
		public Vector3[] Position;
		public Vector3[] Normal;
		public Vector2[] UV;

		public int[] Links;
		public int[] BoneID;
		public float[] Weight;

		public SmdTriangle(string Mat) {
			Material = Mat;

			ParentBone = new int[3];
			Position = new Vector3[3];
			Normal = new Vector3[3];
			UV = new Vector2[3];

			Links = new int[3];
			BoneID = new int[3];
			Weight = new float[3];
		}
	}

	public class Smd {
		public static void Save(string FileName, Vertex3[] Verts) {
			throw new NotImplementedException();
		}

		public static Vertex3[] Load(string FileName) {
			Smd SMD = new Smd();
			SMD.LoadFile(FileName);
			SMD.SwapYZ = true;
			return SMD.Vertices;
		}

		SmdNode WorldNode;
		List<SmdTriangle> Triangles;
		public bool SwapYZ;

		public Vertex3[] Vertices {
			get {
				List<Vertex3> Verts = new List<Vertex3>();

				foreach (var Tri in Triangles) {
					for (int i = 0; i < 3; i++)
						Verts.Add(new Vertex3(SwapYZ ? Tri.Position[i].XZY() : Tri.Position[i], Tri.UV[i]));
				}

				return Verts.ToArray();
			}
		}

		public Smd() {
			WorldNode = new SmdNode(-1, "World");
			Triangles = new List<SmdTriangle>();
		}

		SmdNode[] GetAllNodes(SmdNode Parent = null, List<SmdNode> AddTo = null) {
			List<SmdNode> AllNodes = new List<SmdNode>();
			if (Parent == null) {
				Parent = WorldNode;
				AddTo = AllNodes;
			}

			foreach (var C in Parent.Children) {
				AddTo.Add(C);
				GetAllNodes(C, AddTo);
			}

			if (Parent != WorldNode)
				return null;
			return AddTo.ToArray();
		}

		SmdNode FindNode(int ID, SmdNode StartNode = null) {
			if (ID == -1)
				return WorldNode;

			if (StartNode == null)
				StartNode = WorldNode;

			foreach (var C in StartNode.Children) {
				if (C.ID == ID)
					return C;
			}

			SmdNode SomeNode = null;
			foreach (var C in StartNode.Children)
				if ((SomeNode = FindNode(ID, C)) != null)
					return SomeNode;

			return null;
		}

		void AddNode(SmdNode Node, int Parent) {
			FindNode(Parent).Add(Node);
		}

		public void LoadFile(string FileName) {
			using (StreamReader Reader = new StreamReader(FileName)) {
				//bool Reading = true;
				int LineNum = 0;

				SmdTriangle? CurTri = null;
				int CurTriIdx = 0;
				int CurTimeNum = -1;


				SMDSegment CurSegment = SMDSegment.End;

				while (!Reader.EndOfStream) {
					string L = Reader.ReadLine().Replace('\t', ' ').Trim();
					if (L.StartsWith("//"))
						continue;

					LineNum++;
					if (LineNum == 1)
						if (L == "version 1")
							continue;
						else
							throw new Exception("Invalid SMD file");

					if (CurSegment == SMDSegment.End) {
						if (Enum.GetNames(typeof(SMDSegment)).Select(N => N.ToLower()).Contains(L)) {
							CurSegment = (SMDSegment)Enum.Parse(typeof(SMDSegment), L, true);
							continue;
						} else
							throw new Exception("Unknown SMD segment '" + L + "'");
					} else if (CurSegment == SMDSegment.Nodes) {
						if (L == "end") {
							CurSegment = SMDSegment.End;
							continue;
						}

						int FirstQuote = L.IndexOf('"');
						int LastQuote = L.LastIndexOf('"');

						int ID = L.Substring(0, FirstQuote).ParseInt();
						int ParentID = L.Substring(LastQuote + 1).ParseInt();
						string Name = L.Substring(FirstQuote + 1, LastQuote - FirstQuote - 1);

						AddNode(new SmdNode(ID, Name), ParentID);
					} else if (CurSegment == SMDSegment.Skeleton) {
						if (L == "end") {
							CurSegment = SMDSegment.End;
							continue;
						} else if (L.StartsWith("time")) {
							int TimeNum = L.Split(' ').Last().ParseInt();
							CurTimeNum = TimeNum;

							SmdNode[] AllNodes = GetAllNodes();
							foreach (var Node in AllNodes) {
								if (Node.AnimFrames.Count > 0)
									Node.AnimFrames.Add(new SmdSkeletonFrame(TimeNum, Node.AnimFrames.Last()));
								else
									Node.AnimFrames.Add(new SmdSkeletonFrame(TimeNum, Vector3.Zero, Vector3.Zero));
							}

						} else {
							string[] Tokens = L.Split(' ');
							int BoneID = Tokens[0].ParseInt();
							Vector3 Pos = new Vector3(Tokens[1].ParseFloat(), Tokens[2].ParseFloat(), Tokens[3].ParseFloat());
							Vector3 Rot = new Vector3(Tokens[4].ParseFloat(), Tokens[5].ParseFloat(), Tokens[6].ParseFloat());

							List<SmdSkeletonFrame> Frames = FindNode(BoneID).AnimFrames;
							int Idx = Frames.FindIndex(F => F.Time == CurTimeNum);
							Frames[Idx] = Frames[Idx].Set(Pos, Rot);

							//FindNode(BoneID).AnimFrames.Where(F => F.Time == CurrentTimeNum).First().Set(Pos, Rot);
						}
					} else if (CurSegment == SMDSegment.Triangles) {
						if (L == "end") {
							if (CurTri != null)
								Triangles.Add(CurTri.Value);

							CurSegment = SMDSegment.End;
							continue;
						} else if (!L.Contains(' ')) {
							string MaterialName = L;
							if (MaterialName.Contains("."))
								MaterialName = MaterialName.Substring(0, MaterialName.IndexOf('.'));

							if (CurTri != null)
								Triangles.Add(CurTri.Value);

							CurTri = new SmdTriangle(MaterialName);
							CurTriIdx = 0;
						} else {
							string[] Tokens = L.Split(' ');

							CurTri.Value.ParentBone[CurTriIdx] = Tokens[0].ParseInt();
							CurTri.Value.Position[CurTriIdx] = new Vector3(Tokens[1].ParseFloat(), Tokens[2].ParseFloat(), Tokens[3].ParseFloat());
							CurTri.Value.Normal[CurTriIdx] = new Vector3(Tokens[4].ParseFloat(), Tokens[5].ParseFloat(), Tokens[6].ParseFloat());
							CurTri.Value.UV[CurTriIdx] = new Vector2(Tokens[7].ParseFloat(), Tokens[8].ParseFloat());

							if (Tokens.Length >= 10)
								CurTri.Value.Links[CurTriIdx] = Tokens[9].ParseInt();

							if (Tokens.Length >= 11)
								CurTri.Value.BoneID[CurTriIdx] = Tokens[10].ParseInt();

							if (Tokens.Length >= 12)
								CurTri.Value.Weight[CurTriIdx] = Tokens[11].ParseFloat();

							CurTriIdx++;
						}
					} else
						throw new NotImplementedException("Unimplemented segment " + CurSegment);

				}
			}
		}
	}
}
