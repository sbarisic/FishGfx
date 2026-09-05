using System;
using System.Buffers;
using System.Numerics;

namespace FishGfx.Voxels;

public static partial class VoxelMesher
{
    private readonly struct MeshingScratch : IDisposable
    {
        internal readonly byte[] Visibility;
        internal readonly VoxelModel[] Models;
        internal readonly int[] MergeOffsets;
        internal MeshingScratch(bool greedy)
        {
            Visibility = ArrayPool<byte>.Shared.Rent(4096);
            Array.Clear(Visibility, 0, 4096);
            Models = ArrayPool<VoxelModel>.Shared.Rent(4096);
            MergeOffsets = greedy ? ArrayPool<int>.Shared.Rent(6 * 4096) : null;
            if (MergeOffsets != null) Array.Fill(MergeOffsets, -1, 0, 6 * 4096);
        }
        public void Dispose()
        {
            ArrayPool<byte>.Shared.Return(Visibility);
            ArrayPool<VoxelModel>.Shared.Return(Models, clearArray: true);
            if (MergeOffsets != null) ArrayPool<int>.Shared.Return(MergeOffsets);
        }
    }

    private static bool IsUniform(ReadOnlySpan<VoxelVertex> vertices)
    {
        for (int i = 1; i < vertices.Length; i++)
            if (!vertices[i].Color.Equals(vertices[0].Color)
                || !vertices[i].PackedLightChannels.Equals(vertices[0].PackedLightChannels)) return false;
        return true;
    }

    // The initial culled stream is also the fallback. Only proven uniform cube faces
    // enter the offset table; custom models and transparent ordering remain untouched.
    private static int MergeOpaqueFaces(VoxelChunkSnapshot snapshot, Span<VoxelVertex> vertices, int[] offsets)
    {
        for (int faceIndex = 0; faceIndex < Faces.Length; faceIndex++)
        {
            FaceDefinition face = Faces[faceIndex];
            Vector3 normalAxis = Vector3.Abs(face.Normal), a = face.TangentA, b = face.TangentB;
            int strideA = (int)a.X + 16 * ((int)a.Y + 16 * (int)a.Z);
            int strideB = (int)b.X + 16 * ((int)b.Y + 16 * (int)b.Z);
            bool uAlongA = false;
            for (int corner = 1; corner < 4; corner++)
                if (MathF.Abs(Vector3.Dot(face.GetCorner(corner) - face.Q0, a)) == 1
                    && Vector3.Dot(face.GetCorner(corner) - face.Q0, b) == 0)
                    uAlongA = face.GetUv(corner).X != face.UV0.X;

            for (int slice = 0; slice < 16; slice++)
            for (int v = 0; v < 16; v++)
            for (int u = 0; u < 16; u++)
            {
                Vector3 origin = normalAxis * slice + a * u + b * v;
                int cell = (int)origin.X + 16 * ((int)origin.Y + 16 * (int)origin.Z);
                int key = faceIndex * 4096 + cell, first = offsets[key];
                if (first < 0) continue;
                ushort material = snapshot.GetMaterialUnchecked((int)origin.X, (int)origin.Y, (int)origin.Z);
                int width = 1, height = 1;
                while (u + width < 16 && Matches(cell + width * strideA, faceIndex, first, material, snapshot, vertices, offsets)) width++;
                while (v + height < 16)
                {
                    bool match = true;
                    for (int x = 0; x < width; x++)
                        if (!Matches(cell + x * strideA + height * strideB, faceIndex, first, material, snapshot, vertices, offsets)) { match = false; break; }
                    if (!match) break;
                    height++;
                }
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int coveredKey = key + x * strideA + y * strideB;
                    int covered = offsets[coveredKey];
                    offsets[coveredKey] = -1;
                    if (covered != first)
                        for (int i = 0; i < 6; i++) vertices[covered + i].TextureLayer = int.MinValue;
                }
                for (int i = 0; i < 6; i++)
                {
                    ref VoxelVertex vertex = ref vertices[first + i];
                    if (width > 1 || height > 1) vertex.WaveParameters.W = -1;
                    Vector3 local = vertex.Position - origin;
                    vertex.Position += a * (Vector3.Dot(local, a) * (width - 1)) + b * (Vector3.Dot(local, b) * (height - 1));
                    vertex.TextureCoordinates *= new Vector2(uAlongA ? width : height, uAlongA ? height : width);
                }
            }
        }
        int count = 0;
        for (int i = 0; i < vertices.Length; i++)
            if (vertices[i].TextureLayer != int.MinValue) vertices[count++] = vertices[i];
        return count;
    }

    private static bool Matches(int cell, int face, int first, ushort material,
        VoxelChunkSnapshot snapshot, ReadOnlySpan<VoxelVertex> vertices, int[] offsets)
    {
        int offset = offsets[face * 4096 + cell];
        return offset >= 0 && snapshot.GetMaterialUnchecked(cell & 15, (cell >> 4) & 15, cell >> 8) == material
            && vertices[offset].Color.Equals(vertices[first].Color)
            && vertices[offset].PackedLightChannels.Equals(vertices[first].PackedLightChannels);
    }
}
