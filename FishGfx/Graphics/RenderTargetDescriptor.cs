using System;
using System.Collections.Generic;

namespace FishGfx.Graphics;

public sealed class RenderTargetDescriptor
{
	public RenderTargetDescriptor(
		int width,
		int height,
		IReadOnlyList<TextureFormat> colorFormats = null,
		TextureFormat? depthStencilFormat = TextureFormat.Depth24Stencil8,
		int sampleCount = 1
	)
	{
		if (width <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(width));
		}

		if (height <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(height));
		}

		if (sampleCount <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(sampleCount));
		}

		colorFormats ??= new[] { TextureFormat.RGBA8Unorm };
		TextureFormat[] colorFormatCopy = new TextureFormat[colorFormats.Count];

		for (int index = 0; index < colorFormats.Count; index++)
		{
			TextureFormat format = colorFormats[index];

			if (!Enum.IsDefined(format) || Texture.IsDepthFormat(format))
			{
				throw new ArgumentOutOfRangeException(nameof(colorFormats));
			}

			colorFormatCopy[index] = format;
		}

		if (depthStencilFormat.HasValue
			&& (!Enum.IsDefined(depthStencilFormat.Value)
				|| !Texture.IsDepthFormat(depthStencilFormat.Value)))
		{
			throw new ArgumentOutOfRangeException(nameof(depthStencilFormat));
		}

		if (colorFormatCopy.Length == 0 && !depthStencilFormat.HasValue)
		{
			throw new ArgumentException("A render target requires at least one attachment.");
		}

		Width = width;
		Height = height;
		ColorFormats = Array.AsReadOnly(colorFormatCopy);
		DepthStencilFormat = depthStencilFormat;
		SampleCount = sampleCount;
	}

	public int Width { get; }

	public int Height { get; }

	public IReadOnlyList<TextureFormat> ColorFormats { get; }

	public TextureFormat? DepthStencilFormat { get; }

	public int SampleCount { get; }
}
