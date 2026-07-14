using System;

namespace FishGfx.Graphics;

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

public readonly record struct GraphicsBufferDescriptor
{
	private const BufferBindFlags AllFlags =
		BufferBindFlags.Vertex |
		BufferBindFlags.Index |
		BufferBindFlags.Uniform |
		BufferBindFlags.Storage |
		BufferBindFlags.TransferSource |
		BufferBindFlags.TransferDestination;

	public GraphicsBufferDescriptor(
		int sizeInBytes,
		BufferBindFlags bindFlags,
		BufferUsage usage = BufferUsage.Static
	)
	{
		if (sizeInBytes <= 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(sizeInBytes),
				"Buffer size must be positive."
			);
		}

		if (bindFlags == BufferBindFlags.None || (bindFlags & ~AllFlags) != 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(bindFlags),
				"At least one known buffer binding flag is required."
			);
		}

		if (!Enum.IsDefined(usage))
		{
			throw new ArgumentOutOfRangeException(nameof(usage));
		}

		SizeInBytes = sizeInBytes;
		BindFlags = bindFlags;
		Usage = usage;
	}

	public int SizeInBytes { get; }

	public BufferBindFlags BindFlags { get; }

	public BufferUsage Usage { get; }
}
