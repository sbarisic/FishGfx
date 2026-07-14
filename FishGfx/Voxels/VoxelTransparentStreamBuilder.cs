using System;
using System.Collections.Generic;
using System.Numerics;

namespace FishGfx.Voxels;

internal readonly struct VoxelTransparentFaceInstance
{
	internal VoxelTransparentFaceInstance(
		ChunkCoordinate coordinate,
		int faceIndex,
		Vector3 origin,
		VoxelTransparentFace face,
		float depth = 0
	)
	{
		Coordinate = coordinate;
		FaceIndex = faceIndex;
		Origin = origin;
		Face = face ?? throw new ArgumentNullException(nameof(face));
		Depth = depth;
	}

	internal ChunkCoordinate Coordinate { get; }
	internal int FaceIndex { get; }
	internal Vector3 Origin { get; }
	internal VoxelTransparentFace Face { get; }
	internal float Depth { get; }
	internal Vector3 WorldCenter => Face.Center + Origin;
}

internal static class VoxelTransparentStreamBuilder
{
	internal static VoxelVertex[] Build(
		Vector3 cameraPosition,
		Vector3 cameraForward,
		List<VoxelTransparentFaceInstance> faces
	)
	{
		if (faces == null)
		{
			throw new ArgumentNullException(nameof(faces));
		}

		if (!IsFinite(cameraPosition))
		{
			throw new ArgumentOutOfRangeException(nameof(cameraPosition));
		}

		if (!IsFinite(cameraForward) || cameraForward.LengthSquared() <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(cameraForward));
		}

		cameraForward = Vector3.Normalize(cameraForward);

		for (int i = 0; i < faces.Count; i++)
		{
			VoxelTransparentFaceInstance face = faces[i];
			faces[i] = new VoxelTransparentFaceInstance(
				face.Coordinate,
				face.FaceIndex,
				face.Origin,
				face.Face,
				Vector3.Dot(face.WorldCenter - cameraPosition, cameraForward)
			);
		}

		int vertexCount = CountVertices(faces);
		VoxelVertex[] vertices = new VoxelVertex[vertexCount];
		BuildSorted(faces, vertices);

		return vertices;
	}

	internal static int CountVertices(List<VoxelTransparentFaceInstance> faces)
	{
		int count = 0;

		for (int i = 0; i < faces.Count; i++)
		{
			count = checked(count + faces[i].Face.Vertices.Count);
		}

		return count;
	}

	internal static int BuildSorted(
		List<VoxelTransparentFaceInstance> faces,
		VoxelVertex[] vertices
	)
	{
		if (faces == null)
		{
			throw new ArgumentNullException(nameof(faces));
		}

		if (vertices == null)
		{
			throw new ArgumentNullException(nameof(vertices));
		}

		faces.Sort(Compare);
		int required = CountVertices(faces);

		if (vertices.Length < required)
		{
			throw new ArgumentException("The destination is too small for the transparent stream.", nameof(vertices));
		}

		int destination = 0;

		foreach (VoxelTransparentFaceInstance visibleFace in faces)
		{
			foreach (VoxelVertex source in visibleFace.Face.Vertices)
			{
				VoxelVertex vertex = source;
				vertex.Position += visibleFace.Origin;
				vertices[destination++] = vertex;
			}
		}

		return destination;
	}

	internal static int Compare(
		VoxelTransparentFaceInstance left,
		VoxelTransparentFaceInstance right
	)
	{
		int result = right.Depth.CompareTo(left.Depth);

		if (result == 0)
		{
			result = left.Coordinate.X.CompareTo(right.Coordinate.X);
		}

		if (result == 0)
		{
			result = left.Coordinate.Y.CompareTo(right.Coordinate.Y);
		}

		if (result == 0)
		{
			result = left.Coordinate.Z.CompareTo(right.Coordinate.Z);
		}

		if (result == 0)
		{
			result = left.FaceIndex.CompareTo(right.FaceIndex);
		}

		return result;
	}

	private static bool IsFinite(Vector3 value)
	{
		return float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
	}
}
