using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Numerics;

namespace FishGfx.Voxels;

public sealed class VoxelModel
{
	private readonly VoxelVertex[] vertices;
	private readonly ReadOnlyCollection<VoxelVertex> readOnlyVertices;

	public VoxelModel(IEnumerable<VoxelVertex> vertices)
	{
		if (vertices == null)
		{
			throw new ArgumentNullException(nameof(vertices));
		}

		this.vertices = new List<VoxelVertex>(vertices).ToArray();

		if (this.vertices.Length == 0 || this.vertices.Length % 3 != 0)
		{
			throw new ArgumentException("Voxel models must contain a non-empty triangle list.", nameof(vertices));
		}

		Vector3[] positions = new Vector3[this.vertices.Length];

		for (int i = 0; i < this.vertices.Length; i++)
		{
			VoxelVertex vertex = this.vertices[i];

			if (
				!IsFinite(vertex.Position)
				|| !IsFinite(vertex.Normal)
				|| !IsFinite(vertex.TextureCoordinates)
			)
			{
				throw new ArgumentException("Voxel model vertices must contain only finite values.", nameof(vertices));
			}

			if (vertex.Normal.LengthSquared() <= 0)
			{
				throw new ArgumentException("Voxel model normals cannot be zero.", nameof(vertices));
			}

			vertex.Normal = Vector3.Normalize(vertex.Normal);
			this.vertices[i] = vertex;
			positions[i] = vertex.Position;
		}

		Bounds = AxisAlignedBoundingBox.FromPoints(positions);
		readOnlyVertices = Array.AsReadOnly(this.vertices);
	}

	public IReadOnlyList<VoxelVertex> Vertices => readOnlyVertices;
	public AxisAlignedBoundingBox Bounds { get; }
	internal VoxelVertex[] VertexArray => vertices;

	private static bool IsFinite(Vector2 value)
	{
		return float.IsFinite(value.X) && float.IsFinite(value.Y);
	}

	private static bool IsFinite(Vector3 value)
	{
		return float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
	}
}
