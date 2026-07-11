using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace FishGfx.Voxels
{
	internal readonly struct VoxelTransparentFaceInstance
	{
		internal VoxelTransparentFaceInstance(
			ChunkCoordinate coordinate,
			int faceIndex,
			Vector3 origin,
			VoxelTransparentFace face
		)
		{
			Coordinate = coordinate;
			FaceIndex = faceIndex;
			Origin = origin;
			Face = face ?? throw new ArgumentNullException(nameof(face));
		}

		internal ChunkCoordinate Coordinate { get; }
		internal int FaceIndex { get; }
		internal Vector3 Origin { get; }
		internal VoxelTransparentFace Face { get; }
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
				throw new ArgumentNullException(nameof(faces));
			if (!IsFinite(cameraPosition))
				throw new ArgumentOutOfRangeException(nameof(cameraPosition));
			if (!IsFinite(cameraForward) || cameraForward.LengthSquared() <= 0)
				throw new ArgumentOutOfRangeException(nameof(cameraForward));

			cameraForward = Vector3.Normalize(cameraForward);
			faces.Sort((left, right) => Compare(left, right, cameraPosition, cameraForward));
			VoxelVertex[] vertices = new VoxelVertex[faces.Sum(face => face.Face.Vertices.Count)];
			int destination = 0;

			foreach (VoxelTransparentFaceInstance visibleFace in faces)
				foreach (VoxelVertex source in visibleFace.Face.Vertices)
				{
					VoxelVertex vertex = source;
					vertex.Position += visibleFace.Origin;
					vertices[destination++] = vertex;
				}

			return vertices;
		}

		private static int Compare(
			VoxelTransparentFaceInstance left,
			VoxelTransparentFaceInstance right,
			Vector3 cameraPosition,
			Vector3 cameraForward
		)
		{
			float leftDepth = Vector3.Dot(left.WorldCenter - cameraPosition, cameraForward);
			float rightDepth = Vector3.Dot(right.WorldCenter - cameraPosition, cameraForward);
			int result = rightDepth.CompareTo(leftDepth);

			if (result == 0)
				result = left.Coordinate.X.CompareTo(right.Coordinate.X);
			if (result == 0)
				result = left.Coordinate.Y.CompareTo(right.Coordinate.Y);
			if (result == 0)
				result = left.Coordinate.Z.CompareTo(right.Coordinate.Z);
			if (result == 0)
				result = left.FaceIndex.CompareTo(right.FaceIndex);

			return result;
		}

		private static bool IsFinite(Vector3 value)
		{
			return float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
		}
	}
}
