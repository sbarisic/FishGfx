using System.Numerics;
using FishGfx.Cad;
using FishGfx.Graphics;

namespace FishGfx.ManifoldCad;

internal readonly record struct CadPickHit(
	float Distance,
	ulong TopologyId,
	Guid? SourceNodeId,
	IReadOnlyList<CadGeometrySourceRef> Sources
);

internal sealed class CadTriangleBvh
{
	private readonly CadTessellation tessellation;
	private readonly Triangle[] triangles;
	private readonly Node root;

	internal CadTriangleBvh(CadTessellation tessellation)
	{
		this.tessellation = tessellation ?? throw new ArgumentNullException(nameof(tessellation));
		List<Triangle> source = new();

		foreach (CadFaceRange face in tessellation.Faces)
		{
			for (int offset = 0; offset < face.IndexCount; offset += 3)
			{
				int first = face.FirstIndex + offset;
				source.Add(new Triangle(
					checked((int)tessellation.Indices[first]),
					checked((int)tessellation.Indices[first + 1]),
					checked((int)tessellation.Indices[first + 2]),
					face.TopologyId,
					face.SourceNodeId,
					face.Sources
				));
			}
		}

		triangles = source.ToArray();
		root = triangles.Length == 0 ? null : Build(Enumerable.Range(0, triangles.Length).ToArray());
	}

	internal bool TryIntersect(PickingRay ray, out CadPickHit hit)
	{
		hit = default;
		float nearest = float.PositiveInfinity;
		Triangle nearestTriangle = default;

		if (root == null)
		{
			return false;
		}

		Stack<Node> pending = new();
		pending.Push(root);

		while (pending.Count > 0)
		{
			Node node = pending.Pop();

			if (!IntersectsBounds(ray, node.Minimum, node.Maximum, nearest))
			{
				continue;
			}

			if (node.Indices != null)
			{
				foreach (int triangleIndex in node.Indices)
				{
					Triangle triangle = triangles[triangleIndex];

					if (IntersectTriangle(ray, triangle, out float distance) && distance < nearest)
					{
						nearest = distance;
						nearestTriangle = triangle;
					}
				}

				continue;
			}

			pending.Push(node.Left);
			pending.Push(node.Right);
		}

		if (!float.IsFinite(nearest))
		{
			return false;
		}

		hit = new CadPickHit(
			nearest,
			nearestTriangle.TopologyId,
			nearestTriangle.SourceNodeId,
			nearestTriangle.Sources
		);
		return true;
	}

	private Node Build(int[] indices)
	{
		(Vector3 minimum, Vector3 maximum) = Bounds(indices);

		if (indices.Length <= 12)
		{
			return new Node(minimum, maximum, indices, null, null);
		}

		Vector3 extent = maximum - minimum;
		int axis = extent.X >= extent.Y && extent.X >= extent.Z ? 0 : extent.Y >= extent.Z ? 1 : 2;
		Array.Sort(indices, (left, right) => Component(Centroid(triangles[left]), axis)
			.CompareTo(Component(Centroid(triangles[right]), axis)));
		int middle = indices.Length / 2;
		return new Node(
			minimum,
			maximum,
			null,
			Build(indices[..middle]),
			Build(indices[middle..])
		);
	}

	private (Vector3 Minimum, Vector3 Maximum) Bounds(int[] indices)
	{
		Vector3 minimum = new(float.PositiveInfinity);
		Vector3 maximum = new(float.NegativeInfinity);

		foreach (int index in indices)
		{
			Triangle triangle = triangles[index];

			foreach (int vertexIndex in new[] { triangle.A, triangle.B, triangle.C })
			{
				Vector3 point = Position(vertexIndex);
				minimum = Vector3.Min(minimum, point);
				maximum = Vector3.Max(maximum, point);
			}
		}

		return (minimum, maximum);
	}

	private bool IntersectTriangle(PickingRay ray, Triangle triangle, out float distance)
	{
		Vector3 a = Position(triangle.A);
		Vector3 edge1 = Position(triangle.B) - a;
		Vector3 edge2 = Position(triangle.C) - a;
		Vector3 p = Vector3.Cross(ray.Direction, edge2);
		float determinant = Vector3.Dot(edge1, p);

		if (MathF.Abs(determinant) < 1e-7f)
		{
			distance = 0;
			return false;
		}

		float inverse = 1 / determinant;
		Vector3 t = ray.Origin - a;
		float u = Vector3.Dot(t, p) * inverse;

		if (u < 0 || u > 1)
		{
			distance = 0;
			return false;
		}

		Vector3 q = Vector3.Cross(t, edge1);
		float v = Vector3.Dot(ray.Direction, q) * inverse;
		distance = Vector3.Dot(edge2, q) * inverse;
		return v >= 0 && u + v <= 1 && distance >= 0;
	}

	private static bool IntersectsBounds(PickingRay ray, Vector3 minimum, Vector3 maximum, float maximumDistance)
	{
		float near = 0;
		float far = maximumDistance;

		for (int axis = 0; axis < 3; axis++)
		{
			float origin = Component(ray.Origin, axis);
			float direction = Component(ray.Direction, axis);
			float low = Component(minimum, axis);
			float high = Component(maximum, axis);

			if (MathF.Abs(direction) < 1e-8f)
			{
				if (origin < low || origin > high)
				{
					return false;
				}

				continue;
			}

			float t0 = (low - origin) / direction;
			float t1 = (high - origin) / direction;

			if (t0 > t1)
			{
				(t0, t1) = (t1, t0);
			}

			near = MathF.Max(near, t0);
			far = MathF.Min(far, t1);

			if (near > far)
			{
				return false;
			}
		}

		return true;
	}

	private Vector3 Centroid(Triangle triangle)
	{
		return (Position(triangle.A) + Position(triangle.B) + Position(triangle.C)) / 3;
	}

	private Vector3 Position(int index)
	{
		CadMeshVertex vertex = tessellation.Vertices[index];
		return new Vector3(vertex.X, vertex.Y, vertex.Z);
	}

	private static float Component(Vector3 value, int axis) => axis switch
	{
		0 => value.X,
		1 => value.Y,
		_ => value.Z,
	};

	private readonly record struct Triangle(
		int A,
		int B,
		int C,
		ulong TopologyId,
		Guid? SourceNodeId,
		IReadOnlyList<CadGeometrySourceRef> Sources
	);

	private sealed record Node(Vector3 Minimum, Vector3 Maximum, int[] Indices, Node Left, Node Right);
}
