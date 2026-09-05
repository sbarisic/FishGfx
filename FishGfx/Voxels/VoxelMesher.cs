using System;
using System.Buffers;
using System.Numerics;

namespace FishGfx.Voxels;

public static partial class VoxelMesher
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
		return Build(snapshot, palette, atlas, options, lightSnapshot: null);
	}

	internal static VoxelMeshData Build(
		VoxelChunkSnapshot snapshot,
		VoxelPalette palette,
		VoxelAtlasLayout atlas,
		VoxelMeshingOptions options,
		VoxelLightChunkSnapshot lightSnapshot,
		bool poolOutputBuffers = false
	)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		ArgumentNullException.ThrowIfNull(palette);

		if (lightSnapshot != null && lightSnapshot.Coordinate != snapshot.Coordinate)
		{
			throw new ArgumentException(
				"The light snapshot must match the voxel chunk coordinate.",
				nameof(lightSnapshot)
			);
		}

		options ??= new VoxelMeshingOptions();
		if (!Enum.IsDefined(options.CubeMeshingMode)) throw new ArgumentOutOfRangeException(nameof(options));
		using MeshingScratch scratch = new(options.CubeMeshingMode == VoxelCubeMeshingMode.GreedyOpaque);
		GeometryCounts counts = CountGeometry(snapshot, palette, scratch);
		VoxelVertex[] opaque = CreateVertexBuffer(
			counts.OpaqueVertices,
			poolOutputBuffers
		);
		VoxelVertex[] cutout = CreateVertexBuffer(
			counts.CutoutVertices,
			poolOutputBuffers
		);
		VoxelVertex[] alphaShadow = CreateVertexBuffer(
			counts.AlphaShadowVertices,
			poolOutputBuffers
		);

		try
		{
			VoxelTransparentFace[] transparent = new VoxelTransparentFace[counts.TransparentFaces];
			int opaqueIndex = 0;
			int cutoutIndex = 0;
			int alphaShadowIndex = 0;
			int transparentIndex = 0;
			MeshBoundsBuilder bounds = new MeshBoundsBuilder();
			Span<VoxelVertex> faceVertices = stackalloc VoxelVertex[12];

			for (int z = 0; z < VoxelWorld.ChunkSize; z++)
			{
				for (int y = 0; y < VoxelWorld.ChunkSize; y++)
				{
					for (int x = 0; x < VoxelWorld.ChunkSize; x++)
					{
						ushort materialId = snapshot.GetMaterialUnchecked(x, y, z);

						if (materialId == 0)
						{
							continue;
						}

						VoxelMaterial material = palette[materialId];
						Vector3 blockPosition = new Vector3(x, y, z);

						if (material.Models != null)
						{
							VoxelModel model = scratch.Models[x + 16 * (y + 16 * z)];
							AppendCustomModel(
								model,
								material,
								blockPosition,
								lightSnapshot,
								opaque,
								ref opaqueIndex,
								cutout,
								ref cutoutIndex,
								alphaShadow,
								ref alphaShadowIndex,
								transparent,
								ref transparentIndex,
								ref bounds
							);
							continue;
						}

						for (int faceIndex = 0; faceIndex < Faces.Length; faceIndex++)
						{
							FaceDefinition face = Faces[faceIndex];
							int cellIndex = x + 16 * (y + 16 * z);
							if ((scratch.Visibility[cellIndex] & (1 << faceIndex)) == 0) continue;

							int vertexCount = WriteFace(
								snapshot,
								palette,
								atlas,
								options,
								material,
								face,
								lightSnapshot,
								x,
								y,
								z,
								blockPosition,
								faceVertices
							);
							ReadOnlySpan<VoxelVertex> written = faceVertices[..vertexCount];

							foreach (VoxelVertex vertex in written)
							{
								bounds.Add(vertex.Position);
							}

							switch (material.RenderMode)
							{
								case VoxelRenderMode.Opaque:
									if (scratch.MergeOffsets != null && !material.DoubleSided && !material.Wave.HasValue && IsUniform(written))
										scratch.MergeOffsets[faceIndex * 4096 + cellIndex] = opaqueIndex;
									written.CopyTo(opaque.AsSpan(opaqueIndex));
									opaqueIndex += vertexCount;
									break;
								case VoxelRenderMode.Cutout:
									written.CopyTo(cutout.AsSpan(cutoutIndex));
									cutoutIndex += vertexCount;
									break;
								case VoxelRenderMode.Transparent:
								transparent[transparentIndex++] = new VoxelTransparentFace(
									blockPosition + FaceCenter(face),
									written.ToArray()
								);

								if (material.ShadowCasterMode == VoxelShadowCasterMode.AlphaTest)
								{
									Span<VoxelVertex> shadowDestination = alphaShadow.AsSpan(
										alphaShadowIndex,
										vertexCount
									);
									written.CopyTo(shadowDestination);

									for (int shadowIndex = 0; shadowIndex < shadowDestination.Length; shadowIndex++)
									{
										shadowDestination[shadowIndex].WaveParameters.X = material.ShadowAlphaCutoff;
									}

									alphaShadowIndex += vertexCount;
								}
								break;
								default:
									throw new ArgumentOutOfRangeException();
							}
						}
					}
				}
			}

			if (scratch.MergeOffsets != null)
			{
				opaqueIndex = MergeOpaqueFaces(snapshot, opaque.AsSpan(0, opaqueIndex), scratch.MergeOffsets);
				if (!poolOutputBuffers && opaque.Length != opaqueIndex) Array.Resize(ref opaque, opaqueIndex);
				else if (poolOutputBuffers && opaqueIndex < opaque.Length / 2)
				{
					// Ready-queue accounting must not hide an entire culled buffer behind
					// a tiny merged vertex count. Keep normal pool rounding, not the old capacity.
					VoxelVertex[] compact = CreateVertexBuffer(opaqueIndex, pooled: true);
					opaque.AsSpan(0, opaqueIndex).CopyTo(compact);
					ArrayPool<VoxelVertex>.Shared.Return(opaque);
					opaque = compact;
				}
			}
			return poolOutputBuffers
				? new VoxelMeshData(
					snapshot.Coordinate,
					snapshot.Generation,
					snapshot.Revision,
					lightSnapshot?.Generation ?? 0,
					lightSnapshot?.Revision ?? 0,
					opaque,
					opaqueIndex,
					cutout,
					cutoutIndex,
					alphaShadow,
					alphaShadowIndex,
					transparent,
					bounds.Build()
				)
				: new VoxelMeshData(
					snapshot.Coordinate,
					snapshot.Generation,
					snapshot.Revision,
					lightSnapshot?.Generation ?? 0,
					lightSnapshot?.Revision ?? 0,
					opaque,
					cutout,
					alphaShadow,
					transparent,
					bounds.Build()
				);
		}
		catch
		{
			if (poolOutputBuffers)
			{
				if (opaque.Length > 0)
				{
					ArrayPool<VoxelVertex>.Shared.Return(opaque);
				}

				if (cutout.Length > 0)
				{
					ArrayPool<VoxelVertex>.Shared.Return(cutout);
				}

				if (alphaShadow.Length > 0)
				{
					ArrayPool<VoxelVertex>.Shared.Return(alphaShadow);
				}
			}

			throw;
		}
	}

	private static VoxelVertex[] CreateVertexBuffer(int count, bool pooled)
	{
		if (count == 0)
		{
			return Array.Empty<VoxelVertex>();
		}

		return pooled
			? ArrayPool<VoxelVertex>.Shared.Rent(count)
			: new VoxelVertex[count];
	}

	private static GeometryCounts CountGeometry(
		VoxelChunkSnapshot snapshot,
		VoxelPalette palette,
		MeshingScratch scratch
	)
	{
		GeometryCounts counts = new GeometryCounts();

		for (int z = 0; z < VoxelWorld.ChunkSize; z++)
		{
			for (int y = 0; y < VoxelWorld.ChunkSize; y++)
			{
				for (int x = 0; x < VoxelWorld.ChunkSize; x++)
				{
					ushort materialId = snapshot.GetMaterialUnchecked(x, y, z);

					if (materialId == 0)
					{
						continue;
					}

					if (!palette.Contains(materialId))
					{
						throw new InvalidOperationException(
							$"Chunk contains unknown voxel material ID {materialId}."
						);
					}

					VoxelMaterial material = palette[materialId];

					if (material.Models != null)
					{
						Vector3 origin = snapshot.Coordinate.WorldOrigin;
						VoxelModel model = material.Models.Select(
							(int)origin.X + x,
							(int)origin.Y + y,
							(int)origin.Z + z
						);
						scratch.Models[x + 16 * (y + 16 * z)] = model;
						counts.AddModel(material, model);
						continue;
					}

					for (int faceIndex = 0; faceIndex < Faces.Length; faceIndex++)
					{
						FaceDefinition face = Faces[faceIndex];
						ushort neighborId = snapshot.GetMaterialUnchecked(
							x + face.Neighbor.X,
							y + face.Neighbor.Y,
							z + face.Neighbor.Z
						);

						if (ShouldEmit(materialId, material, neighborId, palette))
						{
							scratch.Visibility[x + 16 * (y + 16 * z)] |= (byte)(1 << faceIndex);
							counts.AddFace(material);
						}
					}
				}
			}
		}

		return counts;
	}

	private struct GeometryCounts
	{
		internal int OpaqueVertices;
		internal int CutoutVertices;
		internal int TransparentFaces;
		internal int AlphaShadowVertices;

		internal void AddFace(VoxelMaterial material)
		{
			int vertices = material.DoubleSided ? 12 : 6;

			switch (material.RenderMode)
			{
				case VoxelRenderMode.Opaque:
					OpaqueVertices = checked(OpaqueVertices + vertices);
					break;
				case VoxelRenderMode.Cutout:
					CutoutVertices = checked(CutoutVertices + vertices);
					break;
				case VoxelRenderMode.Transparent:
					TransparentFaces = checked(TransparentFaces + 1);

					if (material.ShadowCasterMode == VoxelShadowCasterMode.AlphaTest)
					{
						AlphaShadowVertices = checked(AlphaShadowVertices + vertices);
					}
					break;
				default:
					throw new ArgumentOutOfRangeException();
			}
		}

		internal void AddModel(VoxelMaterial material, VoxelModel model)
		{
			int multiplier = material.DoubleSided ? 2 : 1;
			int vertices = checked(model.VertexArray.Length * multiplier);

			switch (material.RenderMode)
			{
				case VoxelRenderMode.Opaque:
					OpaqueVertices = checked(OpaqueVertices + vertices);
					break;
				case VoxelRenderMode.Cutout:
					CutoutVertices = checked(CutoutVertices + vertices);
					break;
				case VoxelRenderMode.Transparent:
					TransparentFaces = checked(
						TransparentFaces + model.VertexArray.Length / 3 * multiplier
					);

					if (material.ShadowCasterMode == VoxelShadowCasterMode.AlphaTest)
					{
						AlphaShadowVertices = checked(AlphaShadowVertices + vertices);
					}
					break;
				default:
					throw new ArgumentOutOfRangeException();
			}
		}
	}

	private struct MeshBoundsBuilder
	{
		private Vector3 minimum;
		private Vector3 maximum;
		private bool hasValue;

		internal void Add(Vector3 point)
		{
			if (!hasValue)
			{
				minimum = point;
				maximum = point;
				hasValue = true;
				return;
			}

			minimum = Vector3.Min(minimum, point);
			maximum = Vector3.Max(maximum, point);
		}

		internal AxisAlignedBoundingBox Build()
		{
			return hasValue
				? new AxisAlignedBoundingBox(minimum, maximum)
				: AxisAlignedBoundingBox.Empty;
		}
	}
}
