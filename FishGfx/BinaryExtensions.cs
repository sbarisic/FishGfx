using System;
using System.IO;
using System.Runtime.InteropServices;

namespace FishGfx;

public static class BinaryExtensions
{
	public static void WriteStruct<T>(this BinaryWriter writer, T value)
		where T : unmanaged
	{
		ArgumentNullException.ThrowIfNull(writer);

		ReadOnlySpan<T> values = MemoryMarshal.CreateReadOnlySpan(ref value, 1);
		writer.Write(MemoryMarshal.AsBytes(values));
	}

	public static T ReadStruct<T>(this BinaryReader reader)
		where T : unmanaged
	{
		ArgumentNullException.ThrowIfNull(reader);

		Span<byte> bytes = stackalloc byte[Marshal.SizeOf<T>()];
		reader.BaseStream.ReadExactly(bytes);

		return MemoryMarshal.Read<T>(bytes);
	}
}
