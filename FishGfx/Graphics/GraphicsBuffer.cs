using System;
using System.Runtime.CompilerServices;
using Silk.NET.OpenGL;
using BufferTarget = Silk.NET.OpenGL.BufferTargetARB;

namespace FishGfx.Graphics
{
	[Flags]
	public enum BufferBindFlags
	{
		None = 0,
		Vertex = 1 << 0,
		Index = 1 << 1,
		Uniform = 1 << 2,
		Storage = 1 << 3,
		TransferSource = 1 << 4,
		TransferDestination = 1 << 5,
	}

	public enum BufferUsage
	{
		Static,
		Dynamic,
		Stream,
	}

	public readonly struct GraphicsBufferDescriptor
	{
		private const BufferBindFlags AllFlags = BufferBindFlags.Vertex | BufferBindFlags.Index |
			BufferBindFlags.Uniform | BufferBindFlags.Storage | BufferBindFlags.TransferSource |
			BufferBindFlags.TransferDestination;

		public GraphicsBufferDescriptor(int sizeInBytes, BufferBindFlags bindFlags, BufferUsage usage = BufferUsage.Static)
		{
			if (sizeInBytes <= 0)
				throw new ArgumentOutOfRangeException(nameof(sizeInBytes), "Buffer size must be positive.");
			if (bindFlags == BufferBindFlags.None || (bindFlags & ~AllFlags) != 0)
				throw new ArgumentOutOfRangeException(nameof(bindFlags), "At least one known buffer binding flag is required.");
			if (!Enum.IsDefined(usage))
				throw new ArgumentOutOfRangeException(nameof(usage));

			SizeInBytes = sizeInBytes;
			BindFlags = bindFlags;
			Usage = usage;
		}

		public int SizeInBytes { get; }
		public BufferBindFlags BindFlags { get; }
		public BufferUsage Usage { get; }
	}

	public unsafe sealed class GraphicsBuffer : GraphicsObject
	{
		internal GraphicsBuffer(GraphicsBufferDescriptor descriptor)
		{
			if (descriptor.SizeInBytes <= 0)
				throw new ArgumentOutOfRangeException(nameof(descriptor), "Buffer size must be positive.");
			const BufferBindFlags allFlags = BufferBindFlags.Vertex | BufferBindFlags.Index | BufferBindFlags.Uniform |
				BufferBindFlags.Storage | BufferBindFlags.TransferSource | BufferBindFlags.TransferDestination;
			if (descriptor.BindFlags == BufferBindFlags.None || (descriptor.BindFlags & ~allFlags) != 0)
				throw new ArgumentOutOfRangeException(nameof(descriptor), "At least one known buffer binding flag is required.");
			if (!Enum.IsDefined(descriptor.Usage))
				throw new ArgumentOutOfRangeException(nameof(descriptor));
			Descriptor = descriptor;
			if (Internal_OpenGL.Is45OrAbove)
				ID = Internal_OpenGL.GL.CreateBuffer();
			else
				ID = Internal_OpenGL.GL.GenBuffer();
			Allocate(descriptor.SizeInBytes);
		}

		public GraphicsBufferDescriptor Descriptor { get; private set; }
		public int SizeInBytes => Descriptor.SizeInBytes;
		public BufferBindFlags BindFlags => Descriptor.BindFlags;
		public BufferUsage Usage => Descriptor.Usage;

		public void ResizeDiscard(int sizeInBytes)
		{
			EnsureCurrentOwner();
			if (sizeInBytes <= 0)
				throw new ArgumentOutOfRangeException(nameof(sizeInBytes), "Buffer size must be positive.");
			if (sizeInBytes == SizeInBytes)
				return;
			Descriptor = new GraphicsBufferDescriptor(sizeInBytes, BindFlags, Usage);
			Allocate(sizeInBytes);
		}

		public void Write<T>(ReadOnlySpan<T> data, int destinationByteOffset = 0) where T : unmanaged
		{
			EnsureCurrentOwner();
			if (destinationByteOffset < 0)
				throw new ArgumentOutOfRangeException(nameof(destinationByteOffset));
			if (destinationByteOffset > SizeInBytes)
				throw new ArgumentOutOfRangeException(nameof(destinationByteOffset));
			int byteCount = checked(data.Length * Unsafe.SizeOf<T>());
			if (byteCount > SizeInBytes - destinationByteOffset)
				throw new ArgumentOutOfRangeException(nameof(data), "The upload exceeds the buffer bounds.");
			if (byteCount == 0)
				return;

			fixed (T* pointer = data)
			{
				if (Internal_OpenGL.Is45OrAbove)
					Internal_OpenGL.GL.NamedBufferSubData(ID, destinationByteOffset, (nuint)byteCount, pointer);
				else
				{
					Internal_OpenGL.GL.GetInteger(GetPName.ArrayBufferBinding, out int previous);
					Internal_OpenGL.GL.BindBuffer(BufferTarget.ArrayBuffer, ID);
					try { Internal_OpenGL.GL.BufferSubData(BufferTarget.ArrayBuffer, destinationByteOffset, (nuint)byteCount, pointer); }
					finally { Internal_OpenGL.GL.BindBuffer(BufferTarget.ArrayBuffer, (uint)previous); }
				}
			}
		}

		public void CopyTo(GraphicsBuffer destination)
		{
			if (destination == null)
				throw new ArgumentNullException(nameof(destination));
			CopyTo(destination, 0, 0, SizeInBytes);
		}

		public void CopyTo(GraphicsBuffer destination, int sourceByteOffset, int destinationByteOffset, int sizeInBytes)
		{
			GraphicsContext context = EnsureCurrentOwner();
			if (destination == null)
				throw new ArgumentNullException(nameof(destination));
			destination.EnsureOwner(context);
			if (ReferenceEquals(this, destination))
				throw new InvalidOperationException("A buffer cannot be copied to itself.");
			if ((BindFlags & BufferBindFlags.TransferSource) == 0)
				throw new InvalidOperationException("The source buffer is missing TransferSource usage.");
			if ((destination.BindFlags & BufferBindFlags.TransferDestination) == 0)
				throw new InvalidOperationException("The destination buffer is missing TransferDestination usage.");
			ValidateRange(sourceByteOffset, sizeInBytes, SizeInBytes, nameof(sourceByteOffset));
			ValidateRange(destinationByteOffset, sizeInBytes, destination.SizeInBytes, nameof(destinationByteOffset));
			if (sizeInBytes == 0)
				return;

			if (Internal_OpenGL.Is45OrAbove)
				Internal_OpenGL.GL.CopyNamedBufferSubData(ID, destination.ID, sourceByteOffset, destinationByteOffset, (nuint)sizeInBytes);
			else
			{
				Internal_OpenGL.GL.GetInteger((GetPName)0x8F36, out int previousRead);
				Internal_OpenGL.GL.GetInteger((GetPName)0x8F37, out int previousWrite);
				try
				{
					Internal_OpenGL.GL.BindBuffer(BufferTarget.CopyReadBuffer, ID);
					Internal_OpenGL.GL.BindBuffer(BufferTarget.CopyWriteBuffer, destination.ID);
					Internal_OpenGL.GL.CopyBufferSubData((GLEnum)BufferTarget.CopyReadBuffer, (GLEnum)BufferTarget.CopyWriteBuffer, sourceByteOffset, destinationByteOffset, (nuint)sizeInBytes);
				}
				finally
				{
					Internal_OpenGL.GL.BindBuffer(BufferTarget.CopyReadBuffer, (uint)previousRead);
					Internal_OpenGL.GL.BindBuffer(BufferTarget.CopyWriteBuffer, (uint)previousWrite);
				}
			}
		}

		internal void Bind(BufferTarget target)
		{
			EnsureCurrentOwner();
			Internal_OpenGL.GL.BindBuffer(target, ID);
		}

		private void Allocate(int sizeInBytes)
		{
			if (Internal_OpenGL.Is45OrAbove)
				Internal_OpenGL.GL.NamedBufferData(ID, (nuint)sizeInBytes, null, ToGlUsage(Usage));
			else
				WithBound(BufferTarget.ArrayBuffer, () => Internal_OpenGL.GL.BufferData(BufferTarget.ArrayBuffer, (nuint)sizeInBytes, null, ToGlUsage(Usage)));
		}

		private void WithBound(BufferTarget target, Action action)
		{
			Internal_OpenGL.GL.GetInteger(GetPName.ArrayBufferBinding, out int previous);
			Internal_OpenGL.GL.BindBuffer(target, ID);
			try { action(); }
			finally { Internal_OpenGL.GL.BindBuffer(target, (uint)previous); }
		}

		private static BufferUsageARB ToGlUsage(BufferUsage usage) => usage switch
		{
			BufferUsage.Static => BufferUsageARB.StaticDraw,
			BufferUsage.Dynamic => BufferUsageARB.DynamicDraw,
			BufferUsage.Stream => BufferUsageARB.StreamDraw,
			_ => throw new ArgumentOutOfRangeException(nameof(usage)),
		};

		private static void ValidateRange(int offset, int count, int capacity, string parameterName)
		{
			if (offset < 0 || offset > capacity)
				throw new ArgumentOutOfRangeException(parameterName);
			if (count < 0 || count > capacity - offset)
				throw new ArgumentOutOfRangeException(nameof(count), "The copy exceeds the buffer bounds.");
		}

		public override void GraphicsDispose() => Internal_OpenGL.GL.DeleteBuffer(ID);
	}
}
