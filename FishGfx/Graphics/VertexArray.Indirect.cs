using System;
using Silk.NET.OpenGL;

namespace FishGfx.Graphics;

internal unsafe sealed partial class VertexArray
{
	internal void MultiDrawArraysIndirect(
		GraphicsBuffer indirectBuffer,
		int byteOffset,
		int drawCount,
		int stride = 0
	)
	{
		EnsureCurrentOwner();
		ArgumentNullException.ThrowIfNull(indirectBuffer);
		indirectBuffer.EnsureOwner(Owner);

		if (!Owner.Capabilities.SupportsMultiDrawIndirect)
		{
			throw new NotSupportedException(
				"Multi-draw indirect requires OpenGL 4.3 or GL_ARB_multi_draw_indirect."
			);
		}

		if ((indirectBuffer.BindFlags & BufferBindFlags.Indirect) == 0)
		{
			throw new InvalidOperationException(
				"The command buffer was not created with the Indirect binding flag."
			);
		}

		if (byteOffset < 0 || (byteOffset & 3) != 0)
		{
			throw new ArgumentOutOfRangeException(nameof(byteOffset));
		}

		if (drawCount < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(drawCount));
		}

		if (stride != 0 && (stride < 16 || (stride & 3) != 0))
		{
			throw new ArgumentOutOfRangeException(nameof(stride));
		}

		int commandStride = stride == 0 ? 16 : stride;
		long requiredBytes = checked((long)byteOffset + (long)drawCount * commandStride);

		if (requiredBytes > indirectBuffer.SizeInBytes)
		{
			throw new ArgumentOutOfRangeException(
				nameof(drawCount),
				"The indirect draw range exceeds the command buffer."
			);
		}

		if (drawCount == 0)
		{
			return;
		}

		ValidateBuffers();
		Owner.BindBuffer(BufferTargetARB.DrawIndirectBuffer, indirectBuffer.Handle);
		WithBound(() => Internal_OpenGL.GL.MultiDrawArraysIndirect(
			ToOpenGl(PrimitiveType),
			(void*)byteOffset,
			(uint)drawCount,
			(uint)stride
		));
	}
}
