using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace FishGfx;

public struct Vertex3
{
	public Vertex3(Vertex3 source)
		: this(source.Position, source.UV, source.Color)
	{
	}

	public Vertex3(Vertex3 source, Color color)
		: this(source.Position, source.UV, color)
	{
	}

	public Vertex3(Vector3 position, Vector2 uv, Color color)
	{
		Position = position;
		UV = uv;
		Color = color;
	}

	public Vertex3(float x, float y, float z)
		: this(new Vector3(x, y, z))
	{
	}

	public Vertex3(Vector3 position)
		: this(position, Vector2.Zero, Color.White)
	{
	}

	public Vertex3(Vector3 position, Color color)
		: this(position, Vector2.Zero, color)
	{
	}

	public Vertex3(Vector3 position, Vector2 uv)
		: this(position, uv, Color.White)
	{
	}

	public Vector3 Position;

	public Vector2 UV;

	public Color Color;

	public byte[] ToByteArray()
	{
		ReadOnlySpan<Vertex3> source = MemoryMarshal.CreateReadOnlySpan(ref this, 1);

		return MemoryMarshal.AsBytes(source).ToArray();
	}

	public static Vertex3 FromByteArray(ReadOnlySpan<byte> bytes)
	{
		int expectedLength = Marshal.SizeOf<Vertex3>();

		if (bytes.Length != expectedLength)
		{
			throw new ArgumentException(
				$"A Vertex3 requires exactly {expectedLength} bytes.",
				nameof(bytes)
			);
		}

		return MemoryMarshal.Read<Vertex3>(bytes);
	}

	public static Vertex3[] FromPositionArray(ReadOnlySpan<float> positions)
	{
		if (positions.Length % 3 != 0)
		{
			throw new ArgumentException("Position data must contain complete xyz triples.", nameof(positions));
		}

		Vertex3[] result = new Vertex3[positions.Length / 3];

		for (int index = 0; index < result.Length; index++)
		{
			int offset = index * 3;
			Vector3 position = new(
				positions[offset],
				positions[offset + 1],
				positions[offset + 2]
			);

			result[index] = new Vertex3(position);
		}

		return result;
	}

	public static implicit operator Vertex3(Vector3 position)
	{
		return new Vertex3(position);
	}
}
