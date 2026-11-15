using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace FishGfx.Graphics.Drawables;

public sealed partial class Mesh3D
{
    /// <summary>Streams packed float32 position/normal/UV vertices without decoded array copies.</summary>
    public IEnumerable<bool> UploadPositionNormalUv(byte[] bytes, int offset, int count, int maximumWriteBytes = 256 * 1024)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(bytes);
        if (vertexBuffer != null) throw new InvalidOperationException("Progressive geometry requires a new mesh.");
        int length = checked(count * 32);
        if (count <= 0 || offset < 0 || offset > bytes.Length - length) throw new ArgumentOutOfRangeException(nameof(count));
        if (maximumWriteBytes < 32) throw new ArgumentOutOfRangeException(nameof(maximumWriteBytes));
        vertexBuffer = owner.CreateBuffer(new GraphicsBufferDescriptor(length, BufferBindFlags.Vertex, usage));
        vertexBinding = (int)VertexArray.BindVertexBuffer(vertexBuffer, stride: 32);
        normalBinding = (int)VertexArray.BindVertexBuffer(vertexBuffer, stride: 32);
        uvBinding = (int)VertexArray.BindVertexBuffer(vertexBuffer, stride: 32);
        VertexArray.AttribFormat(VertexAttribute, 3, VertexElementType.Float, false, 0);
        VertexArray.AttribBinding(VertexAttribute, (uint)vertexBinding);
        VertexArray.AttribFormat(NormalAttribute, 3, VertexElementType.Float, false, 12);
        VertexArray.AttribBinding(NormalAttribute, (uint)normalBinding);
        VertexArray.AttribFormat(UvAttribute, 2, VertexElementType.Float, false, 24);
        VertexArray.AttribBinding(UvAttribute, (uint)uvBinding);
        vertexCount = count;
        HasNormals = true;
        yield return true;
        int step = maximumWriteBytes / 32 * 32;
        for (int start = 0; start < length; start += step)
        {
            vertexBuffer.Write<byte>(bytes.AsSpan(offset + start, Math.Min(step, length - start)), start);
            yield return true;
        }
    }

    /// <summary>
    /// Uploads a new mesh in bounded writes on the graphics context thread.
    /// Do not draw the mesh until enumeration completes. Dispose the mesh if cancelled.
    /// Allocations are separate, indivisible steps; each true result admits one driver call.
    /// </summary>
    public IEnumerable<bool> UploadGeometry(Vector3[] positions, Vector3[] normals, Vector2[] uvs,
        uint[] elements = null, Color[] colors = null, Vector4[] joints = null, Vector4[] weights = null,
        int maximumWriteBytes = 256 * 1024)
    {
        ThrowIfDisposed();
        if (vertexBuffer != null) throw new InvalidOperationException("Progressive geometry requires a new mesh.");
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(normals);
        ArgumentNullException.ThrowIfNull(uvs);
        int count = positions.Length;
        if (count <= 0 || normals.Length != count || uvs.Length != count
            || colors != null && colors.Length != count || (joints == null) != (weights == null)
            || joints != null && (joints.Length != count || weights.Length != count))
            throw new ArgumentException("Geometry streams must contain matching vertex counts.");
        if (maximumWriteBytes < 16) throw new ArgumentOutOfRangeException(nameof(maximumWriteBytes));
        if (elements != null) foreach (uint index in elements)
            if (index >= count) throw new ArgumentException("Index is outside the vertex stream.");
        vertexBuffer = AllocateStream(ref vertexBinding, count, 12, VertexAttribute, 3, VertexElementType.Float);
        vertexCount = count;
        yield return true;
        foreach (bool step in WriteChunks(vertexBuffer, positions, maximumWriteBytes)) yield return step;
        normalBuffer = AllocateStream(ref normalBinding, count, 12, NormalAttribute, 3, VertexElementType.Float);
        yield return true;
        foreach (bool step in WriteChunks(normalBuffer, normals, maximumWriteBytes)) yield return step;
        HasNormals = true;
        uvBuffer = AllocateStream(ref uvBinding, count, 8, UvAttribute, 2, VertexElementType.Float);
        yield return true;
        foreach (bool step in WriteChunks(uvBuffer, uvs, maximumWriteBytes)) yield return step;
        if (colors != null)
        {
            colorBuffer = AllocateStream(ref colorBinding, count, 4, ColorAttribute, 4, VertexElementType.UnsignedByte, true);
            yield return true;
            foreach (bool step in WriteChunks(colorBuffer, colors, maximumWriteBytes)) yield return step;
            hasColors = true;
        }
        if (joints != null)
        {
            jointBuffer = AllocateStream(ref jointBinding, count, 16, 4, 4, VertexElementType.Float);
            yield return true;
            foreach (bool step in WriteChunks(jointBuffer, joints, maximumWriteBytes)) yield return step;
            weightBuffer = AllocateStream(ref weightBinding, count, 16, 5, 4, VertexElementType.Float);
            yield return true;
            foreach (bool step in WriteChunks(weightBuffer, weights, maximumWriteBytes)) yield return step;
        }
        if (elements is { Length: > 0 })
        {
            elementBuffer = owner.CreateBuffer(new GraphicsBufferDescriptor(checked(elements.Length * 4), BufferBindFlags.Index, usage));
            VertexArray.BindElementBuffer(elementBuffer);
            elementCount = elements.Length;
            yield return true;
            foreach (bool step in WriteChunks(elementBuffer, elements, maximumWriteBytes)) yield return step;
        }
    }

    GraphicsBuffer AllocateStream(ref int binding, int count, int stride, uint attribute, int components,
        VertexElementType type, bool normalized = false)
    {
        var buffer = owner.CreateBuffer(new GraphicsBufferDescriptor(checked(count * stride), BufferBindFlags.Vertex, usage));
        try
        {
            binding = (int)VertexArray.BindVertexBuffer(buffer, stride: stride);
            VertexArray.AttribFormat(attribute, components, type, normalized, 0);
            VertexArray.AttribBinding(attribute, (uint)binding);
            return buffer;
        }
        catch { buffer.Dispose(); throw; }
    }

    static IEnumerable<bool> WriteChunks<T>(GraphicsBuffer buffer, T[] values, int maximumBytes) where T : unmanaged
    {
        int size = Unsafe.SizeOf<T>(), step = Math.Max(1, maximumBytes / size);
        for (int start = 0; start < values.Length; start += step)
        {
            buffer.Write<T>(values.AsSpan(start, Math.Min(step, values.Length - start)), checked(start * size));
            yield return true;
        }
    }
}
