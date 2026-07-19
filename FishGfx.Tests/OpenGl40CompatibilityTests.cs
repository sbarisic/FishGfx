using System.Text.RegularExpressions;
using FishGfx.Graphics;
using Xunit;

namespace FishGfx.Tests;

public sealed class OpenGl40CompatibilityTests
{
	private static readonly string[] ShaderExtensions =
	{
		".vert",
		".frag",
		".geom",
	};

	[Fact]
	public void PrimitiveTypePublishesOnlyCoreProfilePrimitives()
	{
		string[] names = Enum.GetNames<PrimitiveType>();

		Assert.DoesNotContain("Quads", names);
		Assert.DoesNotContain("QuadStrip", names);
		Assert.DoesNotContain("Polygon", names);
		Assert.Equal(0x000A, (int)PrimitiveType.LinesAdjacency);
		Assert.Equal(0x000E, (int)PrimitiveType.Patches);
	}

	[Fact]
	public void RenderWindowOptionsRejectVersionsBelowOpenGl40()
	{
		RenderWindowOptions options = new()
		{
			PreferredVersion = new OpenGlVersion(3, 3),
			MinimumVersion = new OpenGlVersion(3, 3),
		};

		Assert.Throws<ArgumentOutOfRangeException>(
			() => RenderWindow.ValidateOptions(options)
		);
	}

	[Fact]
	public void ExactContextValidationRejectsAnyDifferentVersion()
	{
		OpenGlVersion requested = new(4, 0);

		RenderWindow.ValidateCreatedContextVersion(requested, requested, true);
		RenderWindow.ValidateCreatedContextVersion(
			requested,
			new OpenGlVersion(4, 6),
			false
		);

		Assert.Throws<InvalidOperationException>(
			() => RenderWindow.ValidateCreatedContextVersion(
				requested,
				new OpenGlVersion(4, 1),
				true
			)
		);
		Assert.Throws<InvalidOperationException>(
			() => RenderWindow.ValidateCreatedContextVersion(
				requested,
				new OpenGlVersion(3, 3),
				false
			)
		);
	}

	[Fact]
	public void VertexAttributeBindingRequiresOpenGl43()
	{
		GraphicsCapabilities version40 = CreateCapabilities(new OpenGlVersion(4, 0));
		GraphicsCapabilities version43 = CreateCapabilities(new OpenGlVersion(4, 3));

		Assert.False(version40.SupportsVertexAttributeBinding);
		Assert.True(version43.SupportsVertexAttributeBinding);
		Assert.False(version40.SupportsMultiDrawIndirect);
		Assert.True(version43.SupportsMultiDrawIndirect);
	}

	[Fact]
	public void ShippedShadersTargetTheirDocumentedMinimumVersion()
	{
		foreach (string path in GetShaderPaths())
		{
			string firstLine = File.ReadLines(path).First().TrimStart('\uFEFF');
			string expected = Path.GetFileName(path).StartsWith(
				"voxel",
				StringComparison.OrdinalIgnoreCase
			)
				? "#version 430"
				: "#version 400";

			Assert.True(
				string.Equals(firstLine, expected, StringComparison.Ordinal),
				$"{Path.GetFileName(path)} targets '{firstLine}'."
			);
		}
	}

	[Fact]
	public void Glsl400ShadersDoNotUseInterStageLocationQualifiers()
	{
		foreach (string path in GetShaderPaths())
		{
			string source = File.ReadAllText(path);

			if (!source.StartsWith("#version 400", StringComparison.Ordinal))
			{
				continue;
			}

			string extension = Path.GetExtension(path);
			string qualifierPattern = extension switch
			{
				".vert" => "out",
				".frag" => "in",
				".geom" => "(?:in|out)",
				_ => throw new InvalidOperationException(),
			};
			string pattern = $@"layout\s*\([^)]*location[^)]*\)\s+{qualifierPattern}\b";

			Assert.False(
				Regex.IsMatch(source, pattern, RegexOptions.CultureInvariant),
				$"{Path.GetFileName(path)} uses a GLSL 4.10 inter-stage location qualifier."
			);
		}
	}

	private static IEnumerable<string> GetShaderPaths()
	{
		string dataDirectory = Path.Combine(AppContext.BaseDirectory, "data");

		return Directory
			.EnumerateFiles(dataDirectory, "*", SearchOption.AllDirectories)
			.Where(path => ShaderExtensions.Contains(
				Path.GetExtension(path),
				StringComparer.OrdinalIgnoreCase
			))
			.OrderBy(path => path, StringComparer.Ordinal);
	}

	private static GraphicsCapabilities CreateCapabilities(OpenGlVersion version)
	{
		return new GraphicsCapabilities(
			version,
			string.Empty,
			Array.Empty<string>(),
			1,
			1,
			1,
			1,
			1,
			1
		);
	}
}
