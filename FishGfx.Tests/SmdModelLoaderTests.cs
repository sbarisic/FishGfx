using System;
using System.Collections.Generic;
using System.IO;
using FishGfx.Formats;
using Xunit;

namespace FishGfx.Tests;

public sealed class SmdModelLoaderTests
{
	[Fact]
	public void ParseGroupsTrianglesByMaterialAndPreservesWindingConvention()
	{
		const string source = """
			version 1
			nodes
			0 "root" -1
			end
			skeleton
			time 0
			0 0 0 0 0 0 0
			end
			triangles
			stone.png
			0 0 0 0 0 1 0 0 0
			0 1 0 0 0 1 0 1 0
			0 0 1 0 0 1 0 0 1
			end
			""";

		IReadOnlyList<GenericMesh> meshes = SmdModelLoader.Parse(new StringReader(source));

		GenericMesh mesh = Assert.Single(meshes);
		Assert.Equal("stone", mesh.MaterialName);
		Assert.Equal(3, mesh.Vertices.Count);
		Assert.Equal(1, mesh.Vertices[0].Position.Y);
		Assert.Equal(1, mesh.Vertices[1].Position.X);
	}

	[Fact]
	public void ParseRejectsMalformedVertex()
	{
		const string source = """
			version 1
			triangles
			stone
			0 0 0
			end
			""";

		FormatException error = Assert.Throws<FormatException>(
			() => SmdModelLoader.Parse(new StringReader(source))
		);

		Assert.Contains("vertex", error.Message, StringComparison.OrdinalIgnoreCase);
	}
}
