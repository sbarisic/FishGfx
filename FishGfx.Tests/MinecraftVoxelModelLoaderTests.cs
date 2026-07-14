using System;
using System.Collections.Generic;
using System.Numerics;
using FishGfx.Voxels;
using Xunit;

namespace FishGfx.Tests;

public class MinecraftVoxelModelLoaderTests
{
	private const string OneFaceJson = """
		{
		  "elements": [
		    {
		      "from": [0, 0, 0],
		      "to": [16, 16, 16],
		      "faces": {
		        "north": { "uv": [0, 0, 16, 16], "texture": "#0" }
		      }
		    }
		  ]
		}
		""";

	[Fact]
	public void CreatesCorrectFaceGeometryAndUvs()
	{
		VoxelTextureRegion region = new(0, 0, 16, 16, 32, 32);
		VoxelModel model = MinecraftVoxelModelLoader.Load(
			OneFaceJson,
			new Dictionary<string, VoxelTextureRegion> { ["0"] = region }
		);

		Assert.Equal(6, model.Vertices.Count);
		Assert.All(model.Vertices, vertex => Assert.Equal(-Vector3.UnitZ, vertex.Normal));
		Assert.All(model.Vertices, vertex =>
		{
			Assert.InRange(vertex.TextureCoordinates.X, 0, 0.5f);
			Assert.InRange(vertex.TextureCoordinates.Y, 0.5f, 1);
		});
	}

	[Fact]
	public void UsesBlockbenchUvOrientationForEveryDirection()
	{
		Dictionary<string, Vector2[]> expected = new()
		{
			["east"] = Corners(
				new Vector2(1, 1),
				new Vector2(0, 1),
				new Vector2(0, 0),
				new Vector2(1, 0)
			),
			["west"] = Corners(
				new Vector2(1, 1),
				new Vector2(0, 1),
				new Vector2(0, 0),
				new Vector2(1, 0)
			),
			["up"] = Corners(
				new Vector2(1, 1),
				new Vector2(0, 1),
				new Vector2(0, 0),
				new Vector2(1, 0)
			),
			["down"] = Corners(
				new Vector2(1, 1),
				new Vector2(0, 1),
				new Vector2(0, 0),
				new Vector2(1, 0)
			),
			["south"] = Corners(
				new Vector2(1, 0),
				new Vector2(1, 1),
				new Vector2(0, 1),
				new Vector2(0, 0)
			),
			["north"] = Corners(
				new Vector2(0, 1),
				new Vector2(0, 0),
				new Vector2(1, 0),
				new Vector2(1, 1)
			),
		};

		foreach ((string direction, Vector2[] expectedCorners) in expected)
		{
			VoxelModel model = LoadSingleFace(direction, "[0, 0, 16, 16]");

			for (int corner = 0; corner < 4; corner++)
			{
				Assert.Equal(
					expectedCorners[corner],
					model.Vertices[corner].TextureCoordinates
				);
			}
		}
	}

	[Fact]
	public void AppliesFaceRotationsAndPreservesReversedUvEndpoints()
	{
		Dictionary<int, Vector2[]> expected = new()
		{
			[0] = Corners(
				new Vector2(1, 1),
				new Vector2(0, 1),
				new Vector2(0, 0),
				new Vector2(1, 0)
			),
			[90] = Corners(
				new Vector2(0, 1),
				new Vector2(0, 0),
				new Vector2(1, 0),
				new Vector2(1, 1)
			),
			[180] = Corners(
				new Vector2(0, 0),
				new Vector2(1, 0),
				new Vector2(1, 1),
				new Vector2(0, 1)
			),
			[270] = Corners(
				new Vector2(1, 0),
				new Vector2(1, 1),
				new Vector2(0, 1),
				new Vector2(0, 0)
			),
		};

		foreach ((int rotation, Vector2[] expectedCorners) in expected)
		{
			VoxelModel model = LoadSingleFace("east", "[0, 0, 16, 16]", rotation);

			for (int corner = 0; corner < 4; corner++)
			{
				Assert.Equal(
					expectedCorners[corner],
					model.Vertices[corner].TextureCoordinates
				);
			}
		}

		VoxelModel reversed = LoadSingleFace("east", "[16, 16, 0, 0]");
		Vector2[] reversedExpected = Corners(
			new Vector2(0, 0),
			new Vector2(1, 0),
			new Vector2(1, 1),
			new Vector2(0, 1)
		);

		for (int corner = 0; corner < 4; corner++)
		{
			Assert.Equal(
				reversedExpected[corner],
				reversed.Vertices[corner].TextureCoordinates
			);
		}
	}

	[Theory]
	[InlineData("[-1, 0, 16, 16]", null)]
	[InlineData("[0, 0, 17, 16]", null)]
	[InlineData("[0, 0, 16, 16]", 45)]
	[InlineData("[0, 0, 16, 16]", -90)]
	public void RejectsUvsOutsideTheirRegionAndUnsupportedFaceRotations(
		string uv,
		int? rotation
	)
	{
		Assert.Throws<FormatException>(() => LoadSingleFace("north", uv, rotation));
	}

	[Fact]
	public void ValidatesDeclaredTextureSizeAgainstPackedRegion()
	{
		VoxelModel valid = LoadSingleFace(
			"north",
			"[0, 0, 16, 16]",
			declaredTextureSize: 64,
			regionSize: 64
		);

		Assert.Equal(6, valid.Vertices.Count);
		Assert.Throws<FormatException>(
			() => LoadSingleFace(
				"north",
				"[0, 0, 16, 16]",
				declaredTextureSize: 64,
				regionSize: 16
			)
		);
	}

	[Fact]
	public void RejectsMalformedAndUnresolvedModels()
	{
		Assert.Throws<FormatException>(
			() => MinecraftVoxelModelLoader.Load(
				"{}",
				new Dictionary<string, VoxelTextureRegion>()
			)
		);
		Assert.Throws<FormatException>(
			() => MinecraftVoxelModelLoader.Load(
				OneFaceJson,
				new Dictionary<string, VoxelTextureRegion>()
			)
		);
		Assert.Throws<FormatException>(
			() => MinecraftVoxelModelLoader.Load(
				"{",
				new Dictionary<string, VoxelTextureRegion>()
			)
		);
	}

	private static Vector2[] Corners(params Vector2[] values)
	{
		return values;
	}

	private static VoxelModel LoadSingleFace(
		string direction,
		string uv,
		int? rotation = null,
		int? declaredTextureSize = null,
		int regionSize = 16
	)
	{
		string rotationProperty = rotation.HasValue
			? $", \"rotation\": {rotation.Value}"
			: string.Empty;
		string textureSizeProperty = declaredTextureSize.HasValue
			? $"\"texture_size\": [{declaredTextureSize.Value}, {declaredTextureSize.Value}],"
			: string.Empty;
		string json = $$"""
			{
			  {{textureSizeProperty}}
			  "elements": [
			    {
			      "from": [0, 0, 0],
			      "to": [16, 16, 16],
			      "faces": {
			        "{{direction}}": { "uv": {{uv}}, "texture": "#0"{{rotationProperty}} }
			      }
			    }
			  ]
			}
			""";
		VoxelTextureRegion region = new(
			0,
			0,
			regionSize,
			regionSize,
			regionSize,
			regionSize
		);

		return MinecraftVoxelModelLoader.Load(
			json,
			new Dictionary<string, VoxelTextureRegion> { ["0"] = region }
		);
	}
}
