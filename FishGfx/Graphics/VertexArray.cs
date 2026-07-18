using System;
using System.Collections.Generic;
using System.Numerics;
using Silk.NET.OpenGL;

namespace FishGfx.Graphics;

internal unsafe sealed partial class VertexArray : GraphicsResource
{
	private readonly Dictionary<uint, GraphicsBuffer> vertexBuffers = new();
	private GraphicsBuffer elementBuffer;
	private int nextBindingIndex;

	internal VertexArray(GraphicsContext owner)
		: base(owner)
	{
		Handle = Internal_OpenGL.Is45OrAbove
			? Internal_OpenGL.GL.CreateVertexArray()
			: Internal_OpenGL.GL.GenVertexArray();
		PrimitiveType = PrimitiveType.Triangles;
		RegisterResource();
	}

	internal PrimitiveType PrimitiveType { get; set; }

	internal bool HasElementBuffer => elementBuffer != null;

	internal void Draw(int first, int count)
	{
		EnsureCurrentOwner();

		if (first < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(first));
		}

		if (count < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(count));
		}

		if (count == 0)
		{
			return;
		}

		ValidateBuffers();
		WithBound(() => Internal_OpenGL.GL.DrawArrays(
			ToOpenGl(PrimitiveType),
			first,
			(uint)count
		));
	}

	internal void DrawElements(
		int offset = 0,
		int count = -1,
		IndexElementType elementType = IndexElementType.UnsignedShort
	)
	{
		EnsureCurrentOwner();

		if (elementBuffer == null)
		{
			throw new InvalidOperationException(
				"No element buffer is bound. Use Draw for non-indexed geometry."
			);
		}

		if (offset < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(offset));
		}

		if (count < 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(count),
				"An explicit element count is required."
			);
		}

		if (count == 0)
		{
			return;
		}

		int elementSize = GetElementSize(elementType);
		long byteEnd = checked(((long)offset + count) * elementSize);

		if (byteEnd > elementBuffer.SizeInBytes)
		{
			throw new ArgumentOutOfRangeException(
				nameof(count),
				"The indexed draw exceeds the element buffer bounds."
			);
		}

		ValidateBuffers();
		WithBound(() => Internal_OpenGL.GL.DrawElements(
			ToOpenGl(PrimitiveType),
			(uint)count,
			ToOpenGl(elementType),
			(void*)(offset * elementSize)
		));
	}

	internal void BindElementBuffer(GraphicsBuffer buffer)
	{
		EnsureCurrentOwner();
		buffer?.EnsureOwner(Owner);

		if (buffer != null && (buffer.BindFlags & BufferBindFlags.Index) == 0)
		{
			throw new InvalidOperationException(
				"The buffer was not created with the Index binding flag."
			);
		}

		elementBuffer = buffer;
		uint handle = buffer?.Handle ?? 0;

		if (Internal_OpenGL.Is45OrAbove)
		{
			Internal_OpenGL.GL.VertexArrayElementBuffer(Handle, handle);
		}
		else
		{
			WithBound(() => Internal_OpenGL.GL.BindBuffer(
				BufferTargetARB.ElementArrayBuffer,
				handle
			));
		}
	}

	internal static void VertexAttrib(uint attribute, Vector4 value)
	{
		Internal_OpenGL.GL.VertexAttrib4(
			attribute,
			value.X,
			value.Y,
			value.Z,
			value.W
		);
	}

	internal static void VertexAttrib(uint attribute, Color color)
	{
		Vector4 value = new(color.R, color.G, color.B, color.A);
		VertexAttrib(attribute, value / byte.MaxValue);
	}

	internal override void DeleteResource()
	{
		Owner.BindingCache.Invalidate();
		Internal_OpenGL.GL.DeleteVertexArray(Handle);
	}

	private void WithBound(Action action)
	{
		Owner.BindVertexArray(Handle);
		action();
	}

	private void ValidateBuffers()
	{
		if (elementBuffer?.IsDisposed == true)
		{
			throw new ObjectDisposedException(nameof(elementBuffer));
		}

		foreach (GraphicsBuffer buffer in vertexBuffers.Values)
		{
			if (buffer.IsDisposed)
			{
				throw new ObjectDisposedException(nameof(GraphicsBuffer));
			}
		}
	}

	private static int GetElementSize(IndexElementType elementType)
	{
		return elementType switch
		{
			IndexElementType.UnsignedByte => sizeof(byte),
			IndexElementType.UnsignedShort => sizeof(ushort),
			IndexElementType.UnsignedInt => sizeof(uint),
			_ => throw new ArgumentOutOfRangeException(nameof(elementType)),
		};
	}

	private static DrawElementsType ToOpenGl(IndexElementType elementType)
	{
		return elementType switch
		{
			IndexElementType.UnsignedByte => DrawElementsType.UnsignedByte,
			IndexElementType.UnsignedShort => DrawElementsType.UnsignedShort,
			IndexElementType.UnsignedInt => DrawElementsType.UnsignedInt,
			_ => throw new ArgumentOutOfRangeException(nameof(elementType)),
		};
	}

	private static VertexAttribType ToOpenGl(VertexElementType elementType)
	{
		return elementType switch
		{
			VertexElementType.Byte => VertexAttribType.Byte,
			VertexElementType.UnsignedByte => VertexAttribType.UnsignedByte,
			VertexElementType.Short => VertexAttribType.Short,
			VertexElementType.UnsignedShort => VertexAttribType.UnsignedShort,
			VertexElementType.Int => VertexAttribType.Int,
			VertexElementType.UnsignedInt => VertexAttribType.UnsignedInt,
			VertexElementType.Float => VertexAttribType.Float,
			VertexElementType.Double => VertexAttribType.Double,
			VertexElementType.HalfFloat => VertexAttribType.HalfFloat,
			_ => throw new ArgumentOutOfRangeException(nameof(elementType)),
		};
	}

	private static Silk.NET.OpenGL.PrimitiveType ToOpenGl(
		PrimitiveType primitiveType
	)
	{
		return primitiveType switch
		{
			PrimitiveType.Points => Silk.NET.OpenGL.PrimitiveType.Points,
			PrimitiveType.Lines => Silk.NET.OpenGL.PrimitiveType.Lines,
			PrimitiveType.LineLoop => Silk.NET.OpenGL.PrimitiveType.LineLoop,
			PrimitiveType.LineStrip => Silk.NET.OpenGL.PrimitiveType.LineStrip,
			PrimitiveType.Triangles => Silk.NET.OpenGL.PrimitiveType.Triangles,
			PrimitiveType.TriangleStrip => Silk.NET.OpenGL.PrimitiveType.TriangleStrip,
			PrimitiveType.TriangleFan => Silk.NET.OpenGL.PrimitiveType.TriangleFan,
			PrimitiveType.LinesAdjacency => Silk.NET.OpenGL.PrimitiveType.LinesAdjacency,
			PrimitiveType.LineStripAdjacency =>
				Silk.NET.OpenGL.PrimitiveType.LineStripAdjacency,
			PrimitiveType.TrianglesAdjacency =>
				Silk.NET.OpenGL.PrimitiveType.TrianglesAdjacency,
			PrimitiveType.TriangleStripAdjacency =>
				Silk.NET.OpenGL.PrimitiveType.TriangleStripAdjacency,
			PrimitiveType.Patches => Silk.NET.OpenGL.PrimitiveType.Patches,
			_ => throw new ArgumentOutOfRangeException(nameof(primitiveType)),
		};
	}
}
