using System.Runtime.InteropServices;

namespace FishGfx.Graphics;

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly struct DrawArraysIndirectCommand
{
	public DrawArraysIndirectCommand(
		uint count,
		uint instanceCount,
		uint first,
		uint baseInstance
	)
	{
		Count = count;
		InstanceCount = instanceCount;
		First = first;
		BaseInstance = baseInstance;
	}

	public uint Count { get; }

	public uint InstanceCount { get; }

	public uint First { get; }

	public uint BaseInstance { get; }
}
