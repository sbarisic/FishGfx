using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace FishGfx.Formats;

public sealed class GenericMesh
{
	public GenericMesh(string materialName)
	{
		MaterialName = string.IsNullOrWhiteSpace(materialName) ? "none" : materialName;
	}

	public GenericMesh(IEnumerable<Vertex3> vertices, string materialName = "none")
		: this(materialName)
	{
		ArgumentNullException.ThrowIfNull(vertices);

		Vertices.AddRange(vertices);
	}

	public string MaterialName { get; set; }

	public List<Vertex3> Vertices { get; } = new();

	public AxisAlignedBoundingBox Bounds => AxisAlignedBoundingBox.FromPoints(
		Vertices.Select(vertex => vertex.Position)
	);

	public BoundingSphere BoundingSphere => BoundingSphere.FromBounds(Bounds);

	public void SwapYAndZ()
	{
		TransformPositions(position => new Vector3(-position.X, position.Z, position.Y));
	}

	public void ReverseWinding()
	{
		if (Vertices.Count % 3 != 0)
		{
			throw new InvalidOperationException("A mesh must contain complete triangles to reverse its winding.");
		}

		for (int index = 0; index < Vertices.Count; index += 3)
		{
			(Vertex3 first, Vertex3 second) = (Vertices[index], Vertices[index + 1]);

			Vertices[index] = second;
			Vertices[index + 1] = first;
		}
	}

	public void TransformPositions(Func<Vector3, Vector3> transform)
	{
		ArgumentNullException.ThrowIfNull(transform);

		for (int index = 0; index < Vertices.Count; index++)
		{
			Vertex3 vertex = Vertices[index];
			vertex.Position = transform(vertex.Position);
			Vertices[index] = vertex;
		}
	}

	public override string ToString()
	{
		return $"{MaterialName} ({Vertices.Count} verts)";
	}
}
