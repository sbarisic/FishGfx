using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Numerics;

namespace FishGfx.Voxels;

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
