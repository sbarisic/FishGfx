using Silk.NET.OpenGL;

namespace FishGfx.Graphics;

internal sealed class OpenGlBindingCache
{
	private const uint Unknown = uint.MaxValue;
	private uint vertexArray = Unknown;
	private uint arrayBuffer = Unknown;
	private uint drawIndirectBuffer = Unknown;

	internal void BindVertexArray(uint handle)
	{
		if (vertexArray == handle)
		{
			return;
		}

		Internal_OpenGL.GL.BindVertexArray(handle);
		vertexArray = handle;
	}

	internal void BindBuffer(BufferTargetARB target, uint handle)
	{
		ref uint cached = ref GetBufferBinding(target);

		if (cached == handle)
		{
			return;
		}

		Internal_OpenGL.GL.BindBuffer(target, handle);
		cached = handle;
	}

	internal void Invalidate()
	{
		vertexArray = Unknown;
		arrayBuffer = Unknown;
		drawIndirectBuffer = Unknown;
	}

	private ref uint GetBufferBinding(BufferTargetARB target)
	{
		switch (target)
		{
			case BufferTargetARB.ArrayBuffer:
				return ref arrayBuffer;

			case BufferTargetARB.DrawIndirectBuffer:
				return ref drawIndirectBuffer;

			default:
				throw new System.ArgumentOutOfRangeException(
					nameof(target),
					"This buffer target is not tracked by the FishGfx binding cache."
				);
		}
	}
}
