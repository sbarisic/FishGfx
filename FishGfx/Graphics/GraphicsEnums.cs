namespace FishGfx.Graphics
{
	public enum IndexElementType : int
	{
		UnsignedByte = 0x1401,
		UnsignedShort = 0x1403,
		UnsignedInt = 0x1405,
	}

	public enum VertexElementType : int
	{
		Byte = 0x1400,
		UnsignedByte = 0x1401,
		Short = 0x1402,
		UnsignedShort = 0x1403,
		Int = 0x1404,
		UnsignedInt = 0x1405,
		Float = 0x1406,
		Double = 0x140A,
		HalfFloat = 0x140B,
	}

	public enum RenderbufferFormat : int
	{
		DepthComponent16 = 0x81A5,
		DepthComponent24 = 0x81A6,
		DepthComponent32 = 0x81A7,
		Depth24Stencil8 = 0x88F0,
		Rgba8 = 0x8058,
	}
}
