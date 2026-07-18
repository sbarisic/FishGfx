using System;
using System.Runtime.CompilerServices;
using Silk.NET.OpenGL;
using BufferTarget = Silk.NET.OpenGL.BufferTargetARB;

namespace FishGfx.Graphics;

public unsafe sealed class GraphicsBuffer : GraphicsResource
{
	internal GraphicsBuffer(
		GraphicsContext owner,
		GraphicsBufferDescriptor descriptor
	)
		: base(owner)
	{
		ValidateDescriptor(descriptor);
		Descriptor = descriptor;
		Handle = Internal_OpenGL.Is45OrAbove
			? Internal_OpenGL.GL.CreateBuffer()
			: Internal_OpenGL.GL.GenBuffer();

		try
		{
			Allocate(descriptor.SizeInBytes);
			RegisterResource();
		}
		catch
		{
			Internal_OpenGL.GL.DeleteBuffer(Handle);
			Handle = 0;

			throw;
		}
	}

	public GraphicsBufferDescriptor Descriptor { get; private set; }

	public int SizeInBytes => Descriptor.SizeInBytes;

	public BufferBindFlags BindFlags => Descriptor.BindFlags;

	public BufferUsage Usage => Descriptor.Usage;

	public void ResizeDiscard(int sizeInBytes)
	{
		EnsureCurrentOwner();

		if (sizeInBytes <= 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(sizeInBytes),
				"Buffer size must be positive."
			);
		}

		if (sizeInBytes == SizeInBytes)
		{
			return;
		}

		Descriptor = new GraphicsBufferDescriptor(sizeInBytes, BindFlags, Usage);
		Allocate(sizeInBytes);
	}

	/// <summary>
	/// Replaces the buffer's data store without changing its size or descriptor.
	/// Existing contents become undefined, allowing streaming callers to avoid
	/// synchronizing with commands that still consume the previous store.
	/// </summary>
	public void DiscardContents()
	{
		EnsureCurrentOwner();
		Allocate(SizeInBytes);
	}

	public void Write<T>(
		ReadOnlySpan<T> data,
		int destinationByteOffset = 0
	)
		where T : unmanaged
	{
		EnsureCurrentOwner();

		if (destinationByteOffset < 0 || destinationByteOffset > SizeInBytes)
		{
			throw new ArgumentOutOfRangeException(nameof(destinationByteOffset));
		}

		int byteCount = checked(data.Length * Unsafe.SizeOf<T>());

		if (byteCount > SizeInBytes - destinationByteOffset)
		{
			throw new ArgumentOutOfRangeException(
				nameof(data),
				"The upload exceeds the buffer bounds."
			);
		}

		if (byteCount == 0)
		{
			return;
		}

		fixed (T* pointer = data)
		{
			if (Internal_OpenGL.Is45OrAbove)
			{
				Internal_OpenGL.GL.NamedBufferSubData(
					Handle,
					destinationByteOffset,
					(nuint)byteCount,
					pointer
				);
			}
			else
			{
				WriteBound(pointer, byteCount, destinationByteOffset);
			}
		}
	}

	public void CopyTo(GraphicsBuffer destination)
	{
		ArgumentNullException.ThrowIfNull(destination);
		CopyTo(destination, 0, 0, SizeInBytes);
	}

	public void CopyTo(
		GraphicsBuffer destination,
		int sourceByteOffset,
		int destinationByteOffset,
		int sizeInBytes
	)
	{
		GraphicsContext context = EnsureCurrentOwner();
		ArgumentNullException.ThrowIfNull(destination);
		destination.EnsureOwner(context);

		if (ReferenceEquals(this, destination))
		{
			throw new InvalidOperationException("A buffer cannot be copied to itself.");
		}

		if ((BindFlags & BufferBindFlags.TransferSource) == 0)
		{
			throw new InvalidOperationException(
				"The source buffer is missing TransferSource usage."
			);
		}

		if ((destination.BindFlags & BufferBindFlags.TransferDestination) == 0)
		{
			throw new InvalidOperationException(
				"The destination buffer is missing TransferDestination usage."
			);
		}

		ValidateRange(
			sourceByteOffset,
			sizeInBytes,
			SizeInBytes,
			nameof(sourceByteOffset)
		);
		ValidateRange(
			destinationByteOffset,
			sizeInBytes,
			destination.SizeInBytes,
			nameof(destinationByteOffset)
		);

		if (sizeInBytes == 0)
		{
			return;
		}

		if (Internal_OpenGL.Is45OrAbove)
		{
			Internal_OpenGL.GL.CopyNamedBufferSubData(
				Handle,
				destination.Handle,
				sourceByteOffset,
				destinationByteOffset,
				(nuint)sizeInBytes
			);
		}
		else
		{
			CopyBound(
				destination,
				sourceByteOffset,
				destinationByteOffset,
				sizeInBytes
			);
		}
	}

	internal void Bind(BufferTarget target)
	{
		GraphicsContext context = EnsureCurrentOwner();
		context.BindBuffer(target, Handle);
	}

	internal override void DeleteResource()
	{
		Owner.BindingCache.Invalidate();
		Internal_OpenGL.GL.DeleteBuffer(Handle);
	}

	private void Allocate(int sizeInBytes)
	{
		if (Internal_OpenGL.Is45OrAbove)
		{
			Internal_OpenGL.GL.NamedBufferData(
				Handle,
				(nuint)sizeInBytes,
				null,
				ToGlUsage(Usage)
			);
		}
		else
		{
			WithArrayBufferBound(() => Internal_OpenGL.GL.BufferData(
				BufferTarget.ArrayBuffer,
				(nuint)sizeInBytes,
				null,
				ToGlUsage(Usage)
			));
		}
	}

	private void WriteBound(
		void* data,
		int byteCount,
		int destinationByteOffset
	)
	{
		Owner.BindBuffer(BufferTarget.ArrayBuffer, Handle);
		Internal_OpenGL.GL.BufferSubData(
			BufferTarget.ArrayBuffer,
			destinationByteOffset,
			(nuint)byteCount,
			data
		);
	}

	private void CopyBound(
		GraphicsBuffer destination,
		int sourceByteOffset,
		int destinationByteOffset,
		int sizeInBytes
	)
	{
		Internal_OpenGL.GL.GetInteger((GetPName)0x8F36, out int previousRead);
		Internal_OpenGL.GL.GetInteger((GetPName)0x8F37, out int previousWrite);

		try
		{
			Internal_OpenGL.GL.BindBuffer(BufferTarget.CopyReadBuffer, Handle);
			Internal_OpenGL.GL.BindBuffer(
				BufferTarget.CopyWriteBuffer,
				destination.Handle
			);
			Internal_OpenGL.GL.CopyBufferSubData(
				(GLEnum)BufferTarget.CopyReadBuffer,
				(GLEnum)BufferTarget.CopyWriteBuffer,
				sourceByteOffset,
				destinationByteOffset,
				(nuint)sizeInBytes
			);
		}
		finally
		{
			Internal_OpenGL.GL.BindBuffer(
				BufferTarget.CopyReadBuffer,
				(uint)previousRead
			);
			Internal_OpenGL.GL.BindBuffer(
				BufferTarget.CopyWriteBuffer,
				(uint)previousWrite
			);
		}
	}

	private void WithArrayBufferBound(Action action)
	{
		Owner.BindBuffer(BufferTarget.ArrayBuffer, Handle);
		action();
	}

	private static void ValidateDescriptor(GraphicsBufferDescriptor descriptor)
	{
		if (descriptor.SizeInBytes <= 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(descriptor),
				"Buffer size must be positive."
			);
		}

		_ = new GraphicsBufferDescriptor(
			descriptor.SizeInBytes,
			descriptor.BindFlags,
			descriptor.Usage
		);
	}

	private static BufferUsageARB ToGlUsage(BufferUsage usage)
	{
		return usage switch
		{
			BufferUsage.Static => BufferUsageARB.StaticDraw,
			BufferUsage.Dynamic => BufferUsageARB.DynamicDraw,
			BufferUsage.Stream => BufferUsageARB.StreamDraw,
			_ => throw new ArgumentOutOfRangeException(nameof(usage)),
		};
	}

	private static void ValidateRange(
		int offset,
		int count,
		int capacity,
		string parameterName
	)
	{
		if (offset < 0 || offset > capacity)
		{
			throw new ArgumentOutOfRangeException(parameterName);
		}

		if (count < 0 || count > capacity - offset)
		{
			throw new ArgumentOutOfRangeException(
				nameof(count),
				"The copy exceeds the buffer bounds."
			);
		}
	}
}
