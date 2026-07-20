using System;
using System.Collections.Generic;
using Silk.NET.OpenGL;

namespace FishGfx.Graphics;

internal unsafe sealed partial class VertexArray
{
	private readonly Dictionary<uint, VertexBufferBinding> vertexBufferBindings = new();
	private readonly Dictionary<uint, VertexAttributeFormat> attributeFormats = new();
	private readonly Dictionary<uint, uint> attributeBindings = new();
	private readonly HashSet<uint> enabledAttributes = new();

	internal uint BindVertexBuffer(
		GraphicsBuffer buffer,
		int bindingIndex = -1,
		int offset = 0,
		int stride = 3 * sizeof(float)
	)
	{
		EnsureCurrentOwner();
		buffer?.EnsureOwner(Owner);

		if (bindingIndex < -1)
		{
			throw new ArgumentOutOfRangeException(nameof(bindingIndex));
		}

		if (offset < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(offset));
		}

		if (stride <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(stride));
		}

		if (buffer != null && (buffer.BindFlags & BufferBindFlags.Vertex) == 0)
		{
			throw new InvalidOperationException(
				"The buffer was not created with the Vertex binding flag."
			);
		}

		uint binding = SelectBindingIndex(bindingIndex);
		UpdateVertexBufferBinding(binding, buffer, offset, stride);
		ApplyVertexBufferBinding(binding, buffer, offset, stride);

		return binding;
	}

	internal void AttribEnable(uint attributeIndex, bool enabled = true)
	{
		EnsureCurrentOwner();

		if (enabled)
		{
			enabledAttributes.Add(attributeIndex);
		}
		else
		{
			enabledAttributes.Remove(attributeIndex);
		}

		ApplyAttributeEnable(attributeIndex, enabled);
	}

	internal void AttribFormat(
		uint attributeIndex,
		int size = 3,
		VertexElementType attributeType = VertexElementType.Float,
		bool normalized = false,
		uint relativeOffset = 0
	)
	{
		EnsureCurrentOwner();

		if (size < 1 || size > 4)
		{
			throw new ArgumentOutOfRangeException(nameof(size));
		}

		if (!Enum.IsDefined(attributeType))
		{
			throw new ArgumentOutOfRangeException(nameof(attributeType));
		}

		VertexAttributeFormat format = new(
			size,
			attributeType,
			normalized,
			relativeOffset,
			false
		);
		attributeFormats[attributeIndex] = format;

		if (Internal_OpenGL.Is45OrAbove)
		{
			Internal_OpenGL.GL.VertexArrayAttribFormat(
				Handle,
				attributeIndex,
				size,
				ToOpenGl(attributeType),
				normalized,
				relativeOffset
			);

			return;
		}

		if (Owner.Capabilities.SupportsVertexAttributeBinding)
		{
			WithBound(() => Internal_OpenGL.GL.VertexAttribFormat(
				attributeIndex,
				size,
				(GLEnum)ToOpenGl(attributeType),
				normalized,
				relativeOffset
			));

			return;
		}

		TryConfigureClassicAttribute(attributeIndex);
	}

	internal void AttribIFormat(
		uint attributeIndex,
		int size = 1,
		VertexElementType attributeType = VertexElementType.Int,
		uint relativeOffset = 0
	)
	{
		EnsureCurrentOwner();

		if (size < 1 || size > 4)
		{
			throw new ArgumentOutOfRangeException(nameof(size));
		}

		if (attributeType is not (
			VertexElementType.Byte
			or VertexElementType.UnsignedByte
			or VertexElementType.Short
			or VertexElementType.UnsignedShort
			or VertexElementType.Int
			or VertexElementType.UnsignedInt))
		{
			throw new ArgumentOutOfRangeException(nameof(attributeType));
		}

		VertexAttributeFormat format = new(
			size,
			attributeType,
			false,
			relativeOffset,
			true
		);
		attributeFormats[attributeIndex] = format;

		if (Internal_OpenGL.Is45OrAbove)
		{
			Internal_OpenGL.GL.VertexArrayAttribIFormat(
				Handle,
				attributeIndex,
				size,
				(GLEnum)ToOpenGl(attributeType),
				relativeOffset
			);
			return;
		}

		if (Owner.Capabilities.SupportsVertexAttributeBinding)
		{
			WithBound(() => Internal_OpenGL.GL.VertexAttribIFormat(
				attributeIndex,
				size,
				(GLEnum)ToOpenGl(attributeType),
				relativeOffset
			));
			return;
		}

		TryConfigureClassicAttribute(attributeIndex);
	}

	internal void AttribBinding(uint attributeIndex, uint bindingIndex)
	{
		EnsureCurrentOwner();
		attributeBindings[attributeIndex] = bindingIndex;

		if (Internal_OpenGL.Is45OrAbove)
		{
			Internal_OpenGL.GL.VertexArrayAttribBinding(
				Handle,
				attributeIndex,
				bindingIndex
			);
		}
		else if (Owner.Capabilities.SupportsVertexAttributeBinding)
		{
			WithBound(() => Internal_OpenGL.GL.VertexAttribBinding(
				attributeIndex,
				bindingIndex
			));
		}
		else
		{
			ConfigureClassicAttribute(attributeIndex);
		}

		AttribEnable(attributeIndex);
	}

	internal void BindingDivisor(uint bindingIndex, uint divisor)
	{
		EnsureCurrentOwner();

		if (!Owner.Capabilities.SupportsVertexAttributeBinding)
		{
			throw new NotSupportedException(
				"Vertex binding divisors require OpenGL 4.3 or GL_ARB_vertex_attrib_binding."
			);
		}

		if (Internal_OpenGL.Is45OrAbove)
		{
			Internal_OpenGL.GL.VertexArrayBindingDivisor(Handle, bindingIndex, divisor);
			return;
		}

		WithBound(() => Internal_OpenGL.GL.VertexBindingDivisor(bindingIndex, divisor));
	}

	private uint SelectBindingIndex(int bindingIndex)
	{
		if (bindingIndex == -1)
		{
			bindingIndex = nextBindingIndex;
			nextBindingIndex++;
		}
		else
		{
			nextBindingIndex = Math.Max(
				nextBindingIndex,
				checked(bindingIndex + 1)
			);
		}

		return (uint)bindingIndex;
	}

	private void UpdateVertexBufferBinding(
		uint binding,
		GraphicsBuffer buffer,
		int offset,
		int stride
	)
	{
		if (buffer == null)
		{
			vertexBuffers.Remove(binding);
			vertexBufferBindings.Remove(binding);

			return;
		}

		vertexBuffers[binding] = buffer;
		vertexBufferBindings[binding] = new VertexBufferBinding(
			buffer,
			offset,
			stride
		);
	}

	private void ApplyVertexBufferBinding(
		uint binding,
		GraphicsBuffer buffer,
		int offset,
		int stride
	)
	{
		uint handle = buffer?.Handle ?? 0;

		if (Internal_OpenGL.Is45OrAbove)
		{
			Internal_OpenGL.GL.VertexArrayVertexBuffer(
				Handle,
				binding,
				handle,
				offset,
				(uint)stride
			);

			return;
		}

		if (Owner.Capabilities.SupportsVertexAttributeBinding)
		{
			WithBound(() => Internal_OpenGL.GL.BindVertexBuffer(
				binding,
				handle,
				offset,
				(uint)stride
			));

			return;
		}

		ConfigureClassicAttributesForBinding(binding, buffer != null);
	}

	private void ConfigureClassicAttributesForBinding(
		uint bindingIndex,
		bool hasBuffer
	)
	{
		foreach ((uint attributeIndex, uint binding) in attributeBindings)
		{
			if (binding != bindingIndex)
			{
				continue;
			}

			if (!hasBuffer)
			{
				ApplyAttributeEnable(attributeIndex, false);

				continue;
			}

			ConfigureClassicAttribute(attributeIndex);
			ApplyAttributeEnable(
				attributeIndex,
				enabledAttributes.Contains(attributeIndex)
			);
		}
	}

	private void TryConfigureClassicAttribute(uint attributeIndex)
	{
		if (!attributeBindings.TryGetValue(attributeIndex, out uint bindingIndex)
			|| !vertexBufferBindings.ContainsKey(bindingIndex))
		{
			return;
		}

		ConfigureClassicAttribute(attributeIndex);
	}

	private void ConfigureClassicAttribute(uint attributeIndex)
	{
		if (!attributeFormats.TryGetValue(
			attributeIndex,
			out VertexAttributeFormat format
		))
		{
			throw new InvalidOperationException(
				$"Attribute {attributeIndex} has no format."
			);
		}

		if (!attributeBindings.TryGetValue(attributeIndex, out uint bindingIndex)
			|| !vertexBufferBindings.TryGetValue(
				bindingIndex,
				out VertexBufferBinding binding
			))
		{
			throw new InvalidOperationException(
				$"Attribute {attributeIndex} has no vertex buffer."
			);
		}

		long offset = checked((long)binding.Offset + format.RelativeOffset);

		WithBound(() =>
		{
			Internal_OpenGL.GL.GetInteger(
				GetPName.ArrayBufferBinding,
				out int previousBuffer
			);
			Internal_OpenGL.GL.BindBuffer(
				BufferTargetARB.ArrayBuffer,
				binding.Buffer.Handle
			);

			try
			{
				if (format.Integer)
				{
					Internal_OpenGL.GL.VertexAttribIPointer(
						attributeIndex,
						format.Size,
						(GLEnum)ToOpenGl(format.Type),
						(uint)binding.Stride,
						(void*)(nint)offset
					);
				}
				else
				{
					Internal_OpenGL.GL.VertexAttribPointer(
						attributeIndex,
						format.Size,
						(GLEnum)ToOpenGl(format.Type),
						format.Normalized,
						(uint)binding.Stride,
						(void*)(nint)offset
					);
				}
			}
			finally
			{
				Internal_OpenGL.GL.BindBuffer(
					BufferTargetARB.ArrayBuffer,
					(uint)previousBuffer
				);
			}
		});
	}

	private void ApplyAttributeEnable(uint attributeIndex, bool enabled)
	{
		if (Internal_OpenGL.Is45OrAbove)
		{
			if (enabled)
			{
				Internal_OpenGL.GL.EnableVertexArrayAttrib(Handle, attributeIndex);
			}
			else
			{
				Internal_OpenGL.GL.DisableVertexArrayAttrib(Handle, attributeIndex);
			}

			return;
		}

		if (enabled)
		{
			WithBound(() => Internal_OpenGL.GL.EnableVertexAttribArray(attributeIndex));
		}
		else
		{
			WithBound(() => Internal_OpenGL.GL.DisableVertexAttribArray(attributeIndex));
		}
	}

	private readonly record struct VertexBufferBinding(
		GraphicsBuffer Buffer,
		int Offset,
		int Stride
	);

	private readonly record struct VertexAttributeFormat(
		int Size,
		VertexElementType Type,
		bool Normalized,
		uint RelativeOffset,
		bool Integer
	);
}
