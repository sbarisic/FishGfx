using FishGfx;
using FishGfx.Graphics;
using System.Runtime.InteropServices;
using Xunit;

namespace FishGfx.Tests;

public class CompatibilityTests {
	[Fact]
	public void PackedColorMatchesRgbaUploadLayout() {
		Color color = new Color(0x11, 0x22, 0x33, 0x44);
		Span<Color> colors = stackalloc[] { color };
		Span<byte> bytes = MemoryMarshal.AsBytes(colors);
		Assert.Equal(new byte[] { 0x11, 0x22, 0x33, 0x44 }, bytes.ToArray());
	}

	[Theory]
	[InlineData(PrimitiveType.Points, 0)]
	[InlineData(PrimitiveType.Lines, 1)]
	[InlineData(PrimitiveType.Triangles, 4)]
	[InlineData(PrimitiveType.Patches, 14)]
	public void PrimitiveValuesMatchOpenGl(PrimitiveType primitive, int expected) {
		Assert.Equal(expected, (int)primitive);
	}

	[Fact]
	public void TextureConstantsMatchOpenGl() {
		Assert.Equal(0x2600, (int)TextureFilter.Nearest);
		Assert.Equal(0x2601, (int)TextureFilter.Linear);
		Assert.Equal(0x812F, (int)TextureWrap.ClampToEdge);
	}
}
