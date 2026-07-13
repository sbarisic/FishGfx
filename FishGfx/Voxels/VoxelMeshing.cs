using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;

namespace FishGfx.Voxels
{
	public sealed class VoxelMeshingOptions
	{
		public bool AmbientOcclusion { get; set; } = true;
		public byte AoLevel1 { get; set; } = 210;
		public byte AoLevel2 { get; set; } = 170;
		public byte AoLevel3 { get; set; } = 125;
	}

	public sealed class VoxelTransparentFace
	{
		private readonly VoxelVertex[] vertices;
		private readonly ReadOnlyCollection<VoxelVertex> readOnlyVertices;

		internal VoxelTransparentFace(Vector3 center, VoxelVertex[] vertices)
		{
			Center = center;
			this.vertices = vertices;
			readOnlyVertices = Array.AsReadOnly(vertices);
		}

		public Vector3 Center { get; }
		public IReadOnlyList<VoxelVertex> Vertices => readOnlyVertices;
		internal VoxelVertex[] VertexArray => vertices;
	}

	public sealed class VoxelMeshData
	{
		internal VoxelMeshData(
			ChunkCoordinate coordinate,
			long revision,
			VoxelVertex[] opaqueVertices,
			VoxelVertex[] cutoutVertices,
			VoxelTransparentFace[] transparentFaces,
			AABB bounds
		)
		{
			Coordinate = coordinate;
			Revision = revision;
			OpaqueVertices = opaqueVertices;
			CutoutVertices = cutoutVertices;
			TransparentFaces = transparentFaces;
			Bounds = bounds;
		}

		public ChunkCoordinate Coordinate { get; }
		public long Revision { get; }
		public VoxelVertex[] OpaqueVertices { get; }
		public VoxelVertex[] CutoutVertices { get; }
		public VoxelTransparentFace[] TransparentFaces { get; }
		public AABB Bounds { get; }
		public int TransparentVertexCount => TransparentFaces.Sum(face => face.Vertices.Count);
	}

	public static class VoxelMesher
	{
		private static readonly FaceDefinition[] Faces =
		{
			new FaceDefinition(
				VoxelFace.PositiveX,
				new Int3(1, 0, 0),
				Vector3.UnitX,
				Vector3.UnitY,
				Vector3.UnitZ,
				new Vector3(1, 1, 0),
				new Vector3(1, 1, 1),
				new Vector3(1, 0, 1),
				new Vector3(1, 0, 0),
				new Vector2(1, 0),
				new Vector2(0, 0),
				new Vector2(0, 1),
				new Vector2(1, 1)
			),
			new FaceDefinition(
				VoxelFace.NegativeX,
				new Int3(-1, 0, 0),
				-Vector3.UnitX,
				Vector3.UnitY,
				Vector3.UnitZ,
				new Vector3(0, 1, 1),
				new Vector3(0, 1, 0),
				new Vector3(0, 0, 0),
				new Vector3(0, 0, 1),
				new Vector2(1, 0),
				new Vector2(0, 0),
				new Vector2(0, 1),
				new Vector2(1, 1)
			),
			new FaceDefinition(
				VoxelFace.PositiveY,
				new Int3(0, 1, 0),
				Vector3.UnitY,
				Vector3.UnitX,
				Vector3.UnitZ,
				new Vector3(1, 1, 0),
				new Vector3(0, 1, 0),
				new Vector3(0, 1, 1),
				new Vector3(1, 1, 1),
				new Vector2(1, 0),
				new Vector2(0, 0),
				new Vector2(0, 1),
				new Vector2(1, 1)
			),
			new FaceDefinition(
				VoxelFace.NegativeY,
				new Int3(0, -1, 0),
				-Vector3.UnitY,
				Vector3.UnitX,
				Vector3.UnitZ,
				new Vector3(1, 0, 1),
				new Vector3(0, 0, 1),
				new Vector3(0, 0, 0),
				new Vector3(1, 0, 0),
				new Vector2(0, 1),
				new Vector2(1, 1),
				new Vector2(1, 0),
				new Vector2(0, 0)
			),
			new FaceDefinition(
				VoxelFace.PositiveZ,
				new Int3(0, 0, 1),
				Vector3.UnitZ,
				Vector3.UnitX,
				Vector3.UnitY,
				new Vector3(1, 0, 1),
				new Vector3(1, 1, 1),
				new Vector3(0, 1, 1),
				new Vector3(0, 0, 1),
				new Vector2(1, 1),
				new Vector2(1, 0),
				new Vector2(0, 0),
				new Vector2(0, 1)
			),
			new FaceDefinition(
				VoxelFace.NegativeZ,
				new Int3(0, 0, -1),
				-Vector3.UnitZ,
				Vector3.UnitX,
				Vector3.UnitY,
				new Vector3(1, 1, 0),
				new Vector3(1, 0, 0),
				new Vector3(0, 0, 0),
				new Vector3(0, 1, 0),
				new Vector2(0, 0),
				new Vector2(0, 1),
				new Vector2(1, 1),
				new Vector2(1, 0)
			),
		};

		public static VoxelMeshData Build(
			VoxelChunkSnapshot snapshot,
			VoxelPalette palette,
			VoxelAtlasLayout atlas,
			VoxelMeshingOptions options = null
		)
		{
			if (snapshot == null)
				throw new ArgumentNullException(nameof(snapshot));
			if (palette == null)
				throw new ArgumentNullException(nameof(palette));

			options ??= new VoxelMeshingOptions();
			List<VoxelVertex> opaque = new List<VoxelVertex>();
			List<VoxelVertex> cutout = new List<VoxelVertex>();
			List<VoxelTransparentFace> transparent = new List<VoxelTransparentFace>();
			List<Vector3> boundsPoints = new List<Vector3>();

			for (int z = 0; z < VoxelWorld.ChunkSize; z++)
				for (int y = 0; y < VoxelWorld.ChunkSize; y++)
					for (int x = 0; x < VoxelWorld.ChunkSize; x++)
					{
						ushort materialId = snapshot.GetMaterialUnchecked(x, y, z);

						if (materialId == 0)
							continue;
						if (!palette.Contains(materialId))
							throw new InvalidOperationException($"Chunk contains unknown voxel material ID {materialId}.");

						VoxelMaterial material = palette[materialId];
						Vector3 blockPosition = new Vector3(x, y, z);

						if (material.Models != null)
						{
							Vector3 worldOrigin = snapshot.Coordinate.WorldOrigin;
							VoxelModel model = material.Models.Select(
								(int)worldOrigin.X + x,
								(int)worldOrigin.Y + y,
								(int)worldOrigin.Z + z
							);
							AppendCustomModel(
								model,
								material,
								blockPosition,
								opaque,
								cutout,
								transparent,
								boundsPoints
							);
							continue;
						}

						foreach (FaceDefinition face in Faces)
						{
							ushort neighborId = snapshot.GetMaterialUnchecked(
								x + face.Neighbor.X,
								y + face.Neighbor.Y,
								z + face.Neighbor.Z
							);

							if (!ShouldEmit(materialId, material, neighborId, palette))
								continue;

							VoxelVertex[] vertices = CreateFace(
								snapshot,
								palette,
								atlas,
								options,
								material,
								face,
								x,
								y,
								z,
								blockPosition
							);

							foreach (VoxelVertex vertex in vertices)
								boundsPoints.Add(vertex.Position);

							switch (material.RenderMode)
							{
								case VoxelRenderMode.Opaque:
									opaque.AddRange(vertices);
									break;

								case VoxelRenderMode.Cutout:
									cutout.AddRange(vertices);
									break;

								case VoxelRenderMode.Transparent:
									transparent.Add(
										new VoxelTransparentFace(blockPosition + FaceCenter(face), vertices)
									);
									break;

								default:
									throw new ArgumentOutOfRangeException();
							}
						}
					}

			AABB bounds = boundsPoints.Count == 0 ? AABB.Empty : AABB.CalculateAABB(boundsPoints);

			return new VoxelMeshData(
				snapshot.Coordinate,
				snapshot.Revision,
				opaque.ToArray(),
				cutout.ToArray(),
				transparent.ToArray(),
				bounds
			);
		}

		private static void AppendCustomModel(
			VoxelModel model,
			VoxelMaterial material,
			Vector3 blockPosition,
			List<VoxelVertex> opaque,
			List<VoxelVertex> cutout,
			List<VoxelTransparentFace> transparent,
			List<Vector3> boundsPoints
		)
		{
			VoxelVertex[] source = model.VertexArray;

			for (int triangleStart = 0; triangleStart < source.Length; triangleStart += 3)
			{
				VoxelVertex[] triangle = new VoxelVertex[3];
				Vector3 center = Vector3.Zero;

				for (int i = 0; i < triangle.Length; i++)
				{
					VoxelVertex vertex = source[triangleStart + i];
					vertex.Position += blockPosition;
					vertex.Color = Multiply(vertex.Color, material.Tint);
					triangle[i] = vertex;
					center += vertex.Position;
					boundsPoints.Add(vertex.Position);
				}

				AppendCustomTriangle(material.RenderMode, triangle, center / 3, opaque, cutout, transparent);

				if (material.DoubleSided)
				{
					VoxelVertex[] back = { triangle[2], triangle[1], triangle[0] };

					for (int i = 0; i < back.Length; i++)
						back[i].Normal = -back[i].Normal;

					AppendCustomTriangle(material.RenderMode, back, center / 3, opaque, cutout, transparent);
				}
			}
		}

		private static void AppendCustomTriangle(
			VoxelRenderMode renderMode,
			VoxelVertex[] triangle,
			Vector3 center,
			List<VoxelVertex> opaque,
			List<VoxelVertex> cutout,
			List<VoxelTransparentFace> transparent
		)
		{
			switch (renderMode)
			{
				case VoxelRenderMode.Opaque:
					opaque.AddRange(triangle);
					break;
				case VoxelRenderMode.Cutout:
					cutout.AddRange(triangle);
					break;
				case VoxelRenderMode.Transparent:
					transparent.Add(new VoxelTransparentFace(center, triangle));
					break;
				default:
					throw new ArgumentOutOfRangeException(nameof(renderMode));
			}
		}

		private static Color Multiply(Color left, Color right)
		{
			return new Color(
				(byte)(left.R * right.R / 255),
				(byte)(left.G * right.G / 255),
				(byte)(left.B * right.B / 255),
				(byte)(left.A * right.A / 255)
			);
		}

		private static bool ShouldEmit(
			ushort materialId,
			VoxelMaterial material,
			ushort neighborId,
			VoxelPalette palette
		)
		{
			if (neighborId == 0)
				return true;
			if (!palette.Contains(neighborId))
				throw new InvalidOperationException($"Chunk contains unknown voxel material ID {neighborId}.");

			if (material.RenderMode == VoxelRenderMode.Transparent)
			{
				if (neighborId == materialId)
					return false;

				// A fully occluding neighbor emits the visible interface face. Emitting the
				// transparent side as well would place two triangles on the same plane.
				// Non-occluding cutouts still need this face behind their discarded texels.
				return !palette[neighborId].OccludesFaces;
			}

			return !palette[neighborId].OccludesFaces;
		}

		private static VoxelVertex[] CreateFace(
			VoxelChunkSnapshot snapshot,
			VoxelPalette palette,
			VoxelAtlasLayout atlas,
			VoxelMeshingOptions options,
			VoxelMaterial material,
			FaceDefinition face,
			int x,
			int y,
			int z,
			Vector3 blockPosition
		)
		{
			int tile = material.Tiles[face.Face];

			if (tile >= atlas.TileCount)
				throw new InvalidOperationException($"Voxel material '{material.Name}' references atlas tile {tile}, but only {atlas.TileCount} tiles exist.");

			GetUVBounds(tile, atlas, out float u0, out float v0, out float u1, out float v1);
			Vector3[] corners = { face.Q0, face.Q1, face.Q2, face.Q3 };
			Vector2[] uvs =
			{
				MapFaceUv(face.UV0, u0, v0, u1, v1),
				MapFaceUv(face.UV1, u0, v0, u1, v1),
				MapFaceUv(face.UV2, u0, v0, u1, v1),
				MapFaceUv(face.UV3, u0, v0, u1, v1),
			};
			VoxelVertex[] front = new VoxelVertex[6];
			int[] order = { 0, 1, 2, 3, 0, 2 };

			for (int i = 0; i < front.Length; i++)
			{
				int cornerIndex = order[i];
				byte ao = options.AmbientOcclusion
					? CalculateAo(snapshot, palette, options, face, corners[cornerIndex], x, y, z)
					: byte.MaxValue;
				front[i] = new VoxelVertex(
					blockPosition + corners[cornerIndex],
					ApplyAo(material.Tint, ao),
					uvs[cornerIndex],
					face.Normal
				);
			}

			if (!material.DoubleSided)
				return front;

			VoxelVertex[] result = new VoxelVertex[12];
			Array.Copy(front, result, front.Length);
			int[] backOrder = { 2, 1, 0, 2, 0, 3 };

			for (int i = 0; i < 6; i++)
			{
				int cornerIndex = backOrder[i];
				byte ao = options.AmbientOcclusion
					? CalculateAo(snapshot, palette, options, face, corners[cornerIndex], x, y, z)
					: byte.MaxValue;
				result[6 + i] = new VoxelVertex(
					blockPosition + corners[cornerIndex],
					ApplyAo(material.Tint, ao),
					uvs[cornerIndex],
					-face.Normal
				);
			}

			return result;
		}

		private static byte CalculateAo(
			VoxelChunkSnapshot snapshot,
			VoxelPalette palette,
			VoxelMeshingOptions options,
			FaceDefinition face,
			Vector3 corner,
			int x,
			int y,
			int z
		)
		{
			int signA = CornerSign(corner, face.TangentA);
			int signB = CornerSign(corner, face.TangentB);
			Int3 normal = Int3.FromVector(face.Normal);
			Int3 tangentA = Int3.FromVector(face.TangentA) * signA;
			Int3 tangentB = Int3.FromVector(face.TangentB) * signB;
			bool sideA = Occludes(snapshot, palette, x, y, z, normal + tangentA);
			bool sideB = Occludes(snapshot, palette, x, y, z, normal + tangentB);
			bool cornerBlocked = Occludes(snapshot, palette, x, y, z, normal + tangentA + tangentB);
			int level = sideA && sideB ? 3 : (sideA ? 1 : 0) + (sideB ? 1 : 0) + (cornerBlocked ? 1 : 0);

			return level switch
			{
				0 => byte.MaxValue,
				1 => options.AoLevel1,
				2 => options.AoLevel2,
				_ => options.AoLevel3,
			};
		}

		private static bool Occludes(
			VoxelChunkSnapshot snapshot,
			VoxelPalette palette,
			int x,
			int y,
			int z,
			Int3 offset
		)
		{
			ushort materialId = snapshot.GetMaterialUnchecked(x + offset.X, y + offset.Y, z + offset.Z);

			if (materialId == 0)
				return false;
			if (!palette.Contains(materialId))
				throw new InvalidOperationException($"Chunk contains unknown voxel material ID {materialId}.");

			return palette[materialId].OccludesFaces;
		}

		private static int CornerSign(Vector3 corner, Vector3 tangent)
		{
			float coordinate = tangent.X != 0 ? corner.X : tangent.Y != 0 ? corner.Y : corner.Z;
			return coordinate > 0.5f ? 1 : -1;
		}

		private static Color ApplyAo(Color tint, byte ao)
		{
			return new Color(
				(byte)(tint.R * ao / 255),
				(byte)(tint.G * ao / 255),
				(byte)(tint.B * ao / 255),
				tint.A
			);
		}

		private static Vector3 FaceCenter(FaceDefinition face)
		{
			return (face.Q0 + face.Q1 + face.Q2 + face.Q3) / 4;
		}

		private static void GetUVBounds(
			int tile,
			VoxelAtlasLayout atlas,
			out float u0,
			out float v0,
			out float u1,
			out float v1
		)
		{
			int column = tile % atlas.Columns;
			int row = tile / atlas.Columns;
			float insetU = 0.5f / atlas.TextureWidth;
			float insetV = 0.5f / atlas.TextureHeight;
			u0 = column / (float)atlas.Columns + insetU;
			u1 = (column + 1) / (float)atlas.Columns - insetU;
			v1 = 1 - row / (float)atlas.Rows - insetV;
			v0 = 1 - (row + 1) / (float)atlas.Rows + insetV;
		}

		private static Vector2 MapFaceUv(Vector2 sourceUv, float u0, float v0, float u1, float v1)
		{
			return new Vector2(
				float.Lerp(u0, u1, sourceUv.X),
				float.Lerp(v1, v0, sourceUv.Y)
			);
		}

		private readonly struct FaceDefinition
		{
			public FaceDefinition(
				VoxelFace face,
				Int3 neighbor,
				Vector3 normal,
				Vector3 tangentA,
				Vector3 tangentB,
				Vector3 q0,
				Vector3 q1,
				Vector3 q2,
				Vector3 q3,
				Vector2 uv0,
				Vector2 uv1,
				Vector2 uv2,
				Vector2 uv3
			)
			{
				Face = face;
				Neighbor = neighbor;
				Normal = normal;
				TangentA = tangentA;
				TangentB = tangentB;
				Q0 = q0;
				Q1 = q1;
				Q2 = q2;
				Q3 = q3;
				UV0 = uv0;
				UV1 = uv1;
				UV2 = uv2;
				UV3 = uv3;
			}

			public VoxelFace Face { get; }
			public Int3 Neighbor { get; }
			public Vector3 Normal { get; }
			public Vector3 TangentA { get; }
			public Vector3 TangentB { get; }
			public Vector3 Q0 { get; }
			public Vector3 Q1 { get; }
			public Vector3 Q2 { get; }
			public Vector3 Q3 { get; }
			public Vector2 UV0 { get; }
			public Vector2 UV1 { get; }
			public Vector2 UV2 { get; }
			public Vector2 UV3 { get; }
		}

		private readonly struct Int3
		{
			public Int3(int x, int y, int z)
			{
				X = x;
				Y = y;
				Z = z;
			}

			public int X { get; }
			public int Y { get; }
			public int Z { get; }

			public static Int3 FromVector(Vector3 value)
			{
				return new Int3((int)value.X, (int)value.Y, (int)value.Z);
			}

			public static Int3 operator +(Int3 left, Int3 right)
			{
				return new Int3(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
			}

			public static Int3 operator *(Int3 value, int scalar)
			{
				return new Int3(value.X * scalar, value.Y * scalar, value.Z * scalar);
			}
		}
	}
}
