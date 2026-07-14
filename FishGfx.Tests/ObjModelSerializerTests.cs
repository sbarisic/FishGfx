using System.Collections.Generic;
using System.IO;
using FishGfx.Formats;
using Xunit;

namespace FishGfx.Tests;

public sealed class ObjModelSerializerTests
{
	[Fact]
	public void ParseTriangulatesFacesAndSupportsNegativeIndices()
	{
		const string source = """
			v 0 0 0
			v 1 0 0
			v 1 1 0
			v 0 1 0
			vt 0 0
			vt 1 0
			vt 1 1
			vt 0 1
			usemtl panel
			f -4/-4 -3/-3 -2/-2 -1/-1
			""";

		IReadOnlyList<GenericMesh> meshes = ObjModelSerializer.Parse(
			new StringReader(source),
			reverseWinding: false
		);

		GenericMesh mesh = Assert.Single(meshes);
		Assert.Equal("panel", mesh.MaterialName);
		Assert.Equal(6, mesh.Vertices.Count);
		Assert.Equal(mesh.Vertices[0].Position, mesh.Vertices[3].Position);
	}

	[Fact]
	public void ParseRejectsAnOutOfRangeIndex()
	{
		const string source = """
			v 0 0 0
			f 1 2 3
			""";

		Assert.Throws<FormatException>(
			() => ObjModelSerializer.Parse(new StringReader(source))
		);
	}
}
