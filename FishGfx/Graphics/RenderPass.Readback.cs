using System;
using Silk.NET.OpenGL;

namespace FishGfx.Graphics;

public sealed partial class RenderPass
{
	private const int GlPackRowLength = 0x0D02;
	private const int GlPackSkipPixels = 0x0D03;
	private const int GlPackSkipRows = 0x0D04;
	private const int GlPackAlignment = 0x0D05;
	private const int GlReadBuffer = 0x0C02;
	private const int GlPixelPackBuffer = 0x88EB;
	private const int GlPixelPackBufferBinding = 0x88ED;

	/// <summary>
	/// Reads the active resolved color target into bottom-left-origin, non-premultiplied RGBA8 bytes.
	/// </summary>
	public unsafe void ReadColorRgba32(Span<byte> destination)
	{
		EnsureActive();

		if (target.SampleCount != 1)
		{
			throw new InvalidOperationException(
				"Color readback requires a resolved single-sample render target."
			);
		}

		if (!target.IsBackbuffer && target.ColorAttachments.Count == 0)
		{
			throw new InvalidOperationException("The active render target has no color attachment.");
		}

		int requiredBytes = checked(target.Width * target.Height * 4);
		if (destination.Length < requiredBytes)
		{
			throw new ArgumentException(
				$"The destination must contain at least {requiredBytes} bytes.",
				nameof(destination)
			);
		}

		Internal_OpenGL.GL.GetInteger((GetPName)GlPixelPackBufferBinding, out int previousPixelPackBuffer);
		Internal_OpenGL.GL.GetInteger((GetPName)GlPackAlignment, out int previousPackAlignment);
		Internal_OpenGL.GL.GetInteger((GetPName)GlPackRowLength, out int previousPackRowLength);
		Internal_OpenGL.GL.GetInteger((GetPName)GlPackSkipRows, out int previousPackSkipRows);
		Internal_OpenGL.GL.GetInteger((GetPName)GlPackSkipPixels, out int previousPackSkipPixels);
		Internal_OpenGL.GL.GetInteger((GetPName)GlReadBuffer, out int previousReadBuffer);

		try
		{
			Internal_OpenGL.GL.BindBuffer((BufferTargetARB)GlPixelPackBuffer, 0);
			Internal_OpenGL.GL.PixelStore((PixelStoreParameter)GlPackAlignment, 1);
			Internal_OpenGL.GL.PixelStore((PixelStoreParameter)GlPackRowLength, 0);
			Internal_OpenGL.GL.PixelStore((PixelStoreParameter)GlPackSkipRows, 0);
			Internal_OpenGL.GL.PixelStore((PixelStoreParameter)GlPackSkipPixels, 0);
			Internal_OpenGL.GL.ReadBuffer(target.IsBackbuffer
				? ReadBufferMode.Back
				: ReadBufferMode.ColorAttachment0);

			fixed (byte* pixels = destination)
			{
				Internal_OpenGL.GL.ReadPixels(
					0,
					0,
					(uint)target.Width,
					(uint)target.Height,
					PixelFormat.Rgba,
					PixelType.UnsignedByte,
					pixels
				);
			}
		}
		finally
		{
			Internal_OpenGL.GL.ReadBuffer((ReadBufferMode)previousReadBuffer);
			Internal_OpenGL.GL.PixelStore((PixelStoreParameter)GlPackSkipPixels, previousPackSkipPixels);
			Internal_OpenGL.GL.PixelStore((PixelStoreParameter)GlPackSkipRows, previousPackSkipRows);
			Internal_OpenGL.GL.PixelStore((PixelStoreParameter)GlPackRowLength, previousPackRowLength);
			Internal_OpenGL.GL.PixelStore((PixelStoreParameter)GlPackAlignment, previousPackAlignment);
			Internal_OpenGL.GL.BindBuffer(
				(BufferTargetARB)GlPixelPackBuffer,
				(uint)previousPixelPackBuffer
			);
		}
	}
}
