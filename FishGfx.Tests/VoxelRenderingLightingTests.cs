using System.Numerics;
using System.Runtime.InteropServices;
using FishGfx.Graphics;
using FishGfx.Voxels;
using Xunit;

namespace FishGfx.Tests;

public partial class VoxelRenderingLightingTests
{
	private static readonly VoxelAtlasLayout Atlas = new(1, 1, 16, 16);

	[Fact]
	public void VoxelVertexPreservesThePackedGpuLayout()
	{
		Assert.Equal(56, Marshal.SizeOf<VoxelVertex>());
		Assert.Equal(new IntPtr(0), Marshal.OffsetOf<VoxelVertex>(nameof(VoxelVertex.Position)));
		Assert.Equal(new IntPtr(12), Marshal.OffsetOf<VoxelVertex>(nameof(VoxelVertex.Color)));
		Assert.Equal(
			new IntPtr(16),
			Marshal.OffsetOf<VoxelVertex>(nameof(VoxelVertex.TextureCoordinates))
		);
		Assert.Equal(new IntPtr(24), Marshal.OffsetOf<VoxelVertex>(nameof(VoxelVertex.Normal)));
		Assert.Equal(
			new IntPtr(36),
			Marshal.OffsetOf<VoxelVertex>(nameof(VoxelVertex.WaveParameters))
		);
		Assert.Equal(
			new IntPtr(52),
			Marshal.OffsetOf<VoxelVertex>(nameof(VoxelVertex.PackedLightChannels))
		);

		VoxelVertex vertex = new(Vector3.Zero, Color.White, Vector2.Zero, Vector3.UnitY);

		Assert.Equal(Vector4.Zero, vertex.WaveParameters);
		Assert.Equal(new Color(0, 0, 0, byte.MaxValue), vertex.PackedLightChannels);

		string shaderDirectory = Path.Combine(AppContext.BaseDirectory, "data", "shaders");
		string standard = File.ReadAllText(Path.Combine(shaderDirectory, "voxel.vert"));
		string waving = File.ReadAllText(Path.Combine(shaderDirectory, "voxel_wave.vert"));

		Assert.Contains("layout (location = 5) in vec4 Light;", standard);
		Assert.Contains("layout (location = 5) in vec4 Light;", waving);
	}

	[Fact]
	public void VoxelShadersConsumeTheRenderPassUniformContract()
	{
		string shaderDirectory = Path.Combine(AppContext.BaseDirectory, "data", "shaders");
		string standard = File.ReadAllText(Path.Combine(shaderDirectory, "voxel.vert"));
		string waving = File.ReadAllText(Path.Combine(shaderDirectory, "voxel_wave.vert"));
		string fragment = File.ReadAllText(Path.Combine(shaderDirectory, "voxel.frag"));

		AssertUniformDeclaration(standard, "mat4", RenderUniformState.ModelUniformName);
		AssertUniformDeclaration(standard, "mat4", RenderUniformState.ViewUniformName);
		AssertUniformDeclaration(standard, "mat4", RenderUniformState.ProjectionUniformName);
		AssertUniformDeclaration(waving, "mat4", RenderUniformState.ModelUniformName);
		AssertUniformDeclaration(waving, "mat4", RenderUniformState.ViewUniformName);
		AssertUniformDeclaration(waving, "mat4", RenderUniformState.ProjectionUniformName);
		AssertUniformDeclaration(waving, "float", RenderUniformState.TimeUniformName);
		AssertUniformDeclaration(
			fragment,
			"vec3",
			RenderUniformState.ViewPositionUniformName
		);
	}

	[Theory]
	[InlineData(VoxelRenderMode.Opaque)]
	[InlineData(VoxelRenderMode.Cutout)]
	[InlineData(VoxelRenderMode.Transparent)]
	public void CubeLightingAveragesFourOutsideFaceSamplesAcrossRenderStreams(
		VoxelRenderMode renderMode
	)
	{
		(VoxelWorld world, VoxelPalette palette, _) = CreateCube(renderMode, 1, 1, 1);
		ushort[] padded = CreatePaddedLights();
		SetLight(padded, 2, 1, 1, new VoxelLight(new VoxelBlockLight(0, 1, 2), 3));
		SetLight(padded, 2, 2, 1, new VoxelLight(new VoxelBlockLight(4, 5, 6), 7));
		SetLight(padded, 2, 1, 0, new VoxelLight(new VoxelBlockLight(8, 9, 10), 11));
		SetLight(padded, 2, 2, 0, new VoxelLight(new VoxelBlockLight(12, 13, 14), 15));

		VoxelMeshData mesh = BuildLit(world, palette, default, padded);
		VoxelVertex[] corner = Vertices(mesh, renderMode)
			.Where(vertex => vertex.Normal == Vector3.UnitX && vertex.Position == new Vector3(2, 2, 1))
			.ToArray();

		Assert.NotEmpty(corner);
		Assert.All(
			corner,
			vertex => Assert.Equal(new Color(102, 119, 136, 153), vertex.PackedLightChannels)
		);
	}

	[Fact]
	public void CubeLightingSamplesTheAdjacentChunkHaloAtAChunkEdge()
	{
		(VoxelWorld world, VoxelPalette palette, _) = CreateCube(VoxelRenderMode.Opaque, 15, 1, 1);
		ushort[] padded = CreatePaddedLights();
		VoxelLight haloLight = new(new VoxelBlockLight(15, 9, 3), 12);
		SetLight(padded, 16, 1, 1, haloLight);
		SetLight(padded, 16, 2, 1, haloLight);
		SetLight(padded, 16, 1, 0, haloLight);
		SetLight(padded, 16, 2, 0, haloLight);

		VoxelMeshData mesh = BuildLit(world, palette, default, padded);
		VoxelVertex[] corner = mesh.OpaqueVertices
			.Where(vertex => vertex.Normal == Vector3.UnitX && vertex.Position == new Vector3(16, 2, 1))
			.ToArray();

		Assert.NotEmpty(corner);
		Assert.All(
			corner,
			vertex => Assert.Equal(new Color(255, 153, 51, 204), vertex.PackedLightChannels)
		);
	}

	[Theory]
	[InlineData(VoxelRenderMode.Opaque)]
	[InlineData(VoxelRenderMode.Cutout)]
	[InlineData(VoxelRenderMode.Transparent)]
	public void CustomModelsTrilinearlySampleLightAndApplyAnEmissionFloor(
		VoxelRenderMode renderMode
	)
	{
		VoxelModel model = new(
			new[]
			{
				new VoxelVertex(Vector3.One, Color.White, Vector2.Zero, Vector3.UnitY),
				new VoxelVertex(new Vector3(1, 0, 1), Color.White, Vector2.UnitX, Vector3.UnitY),
				new VoxelVertex(new Vector3(0, 1, 1), Color.White, Vector2.UnitY, Vector3.UnitY),
			}
		);
		VoxelPaletteBuilder builder = new();
		ushort material = builder.Add(
			new VoxelMaterial(
				"Model",
				renderMode,
				new VoxelFaceTiles(0),
				models: new VoxelModelSet(model),
				light: new VoxelMaterialLightSettings(0, new VoxelBlockLight(3, 0, 5))
			)
		);
		VoxelPalette palette = builder.Build();
		VoxelWorld world = new();
		world.SetVoxel(3, 3, 3, new VoxelCell(material));
		ushort[] padded = CreatePaddedLights();

		for (int dz = 0; dz <= 1; dz++)
		{
			for (int dy = 0; dy <= 1; dy++)
			{
				for (int dx = 0; dx <= 1; dx++)
				{
					byte red = (byte)(2 * (dx + 2 * dy + 4 * dz));
					SetLight(
						padded,
						3 + dx,
						3 + dy,
						3 + dz,
						new VoxelLight(new VoxelBlockLight(red, 1, 0), 4)
					);
				}
			}
		}

		VoxelMeshData mesh = BuildLit(world, palette, default, padded);
		VoxelVertex vertex = Assert.Single(
			Vertices(mesh, renderMode),
			candidate => candidate.Position == new Vector3(4, 4, 4)
		);

		Assert.Equal(new Color(119, 17, 85, 68), vertex.PackedLightChannels);
	}

	[Fact]
	public void DoubleSidedWaveGeometryAndTransparentStreamPreservePackedLight()
	{
		VoxelWaveSettings wave = new(0.1f, 6, 0.2f);
		(VoxelWorld world, VoxelPalette palette, _) = CreateCube(
			VoxelRenderMode.Transparent,
			1,
			1,
			1,
			doubleSided: true,
			wave: wave
		);
		ushort[] padded = CreatePaddedLights();

		for (int z = -1; z <= VoxelWorld.ChunkSize; z++)
		{
			for (int y = -1; y <= VoxelWorld.ChunkSize; y++)
			{
				for (int x = -1; x <= VoxelWorld.ChunkSize; x++)
				{
					byte red = (byte)((x + 2 * y + 3 * z + 90) % 16);
					byte green = (byte)((3 * x + y + z + 90) % 16);
					SetLight(padded, x, y, z, new VoxelLight(new VoxelBlockLight(red, green, 4), 11));
				}
			}
		}

		VoxelMeshData mesh = BuildLit(world, palette, default, padded);
		Assert.Equal(6, mesh.TransparentFaces.Length);

		foreach (VoxelTransparentFace face in mesh.TransparentFaces)
		{
			Assert.Equal(12, face.Vertices.Count);
			VoxelVertex[] front = face.Vertices.Take(6).ToArray();

			foreach (VoxelVertex back in face.Vertices.Skip(6))
			{
				Assert.Contains(
					front,
					candidate => candidate.Position == back.Position
						&& candidate.PackedLightChannels == back.PackedLightChannels
						&& candidate.WaveParameters == back.WaveParameters
				);
			}
		}

		Vector3 origin = new(32, -16, 48);
		List<VoxelTransparentFaceInstance> instances = mesh.TransparentFaces
			.Select(
				(face, index) => new VoxelTransparentFaceInstance(
					new ChunkCoordinate(2, -1, 3),
					index,
					origin,
					face
				)
			)
			.ToList();
		VoxelVertex[] stream = VoxelTransparentStreamBuilder.Build(
			new Vector3(32, -16, 80),
			-Vector3.UnitZ,
			instances
		);
		int destination = 0;

		foreach (VoxelTransparentFaceInstance instance in instances)
		{
			foreach (VoxelVertex source in instance.Face.Vertices)
			{
				Assert.Equal(source.Position + origin, stream[destination].Position);
				Assert.Equal(
					source.PackedLightChannels,
					stream[destination].PackedLightChannels
				);
				Assert.Equal(source.WaveParameters, stream[destination].WaveParameters);
				destination++;
			}
		}
	}

	[Theory]
	[InlineData(VoxelRenderMode.Opaque)]
	[InlineData(VoxelRenderMode.Cutout)]
	[InlineData(VoxelRenderMode.Transparent)]
	public void MeshingWithoutLightingRetainsFullSkylightCompatibility(VoxelRenderMode renderMode)
	{
		(VoxelWorld world, VoxelPalette palette, _) = CreateCube(renderMode, 1, 1, 1);

		VoxelMeshData mesh = VoxelMesher.Build(world.CreateSnapshot(default), palette, Atlas);

		Assert.All(
			Vertices(mesh, renderMode),
			vertex => Assert.Equal(
				new Color(0, 0, 0, byte.MaxValue),
				vertex.PackedLightChannels
			)
		);
	}

	[Fact]
	public void SunSettingsNormalizeAndValidateWithoutAffectingMeshingState()
	{
		VoxelSunSettings sun = new(new Vector3(0, -4, 0), new Color(255, 230, 200), 1.25f, 0.3f);
		VoxelSunSettings equivalent = new(-Vector3.UnitY, new Color(255, 230, 200), 1.25f, 0.3f);

		Assert.Equal(-Vector3.UnitY, sun.Direction);
		Assert.Equal(1.25f, sun.Intensity);
		Assert.Equal(0.3f, sun.AmbientLight);
		Assert.Equal(equivalent, sun);
		Assert.True(equivalent == sun);
		Assert.Throws<ArgumentOutOfRangeException>(
			() => new VoxelSunSettings(Vector3.Zero, Color.White, 1, 0.2f)
		);
		Assert.Throws<ArgumentOutOfRangeException>(
			() => new VoxelSunSettings(new Vector3(float.NaN, 0, 0), Color.White, 1, 0.2f)
		);
		Assert.Throws<ArgumentOutOfRangeException>(
			() => new VoxelSunSettings(new Vector3(float.MaxValue, 0, 0), Color.White, 1, 0.2f)
		);
		Assert.Throws<ArgumentOutOfRangeException>(
			() => new VoxelSunSettings(Vector3.UnitY, Color.White, float.NaN, 0.2f)
		);
		Assert.Throws<ArgumentOutOfRangeException>(
			() => new VoxelSunSettings(Vector3.UnitY, Color.White, -0.01f, 0.2f)
		);
		Assert.Throws<ArgumentOutOfRangeException>(
			() => new VoxelSunSettings(Vector3.UnitY, Color.White, float.PositiveInfinity, 0.2f)
		);
		Assert.Throws<ArgumentOutOfRangeException>(
			() => new VoxelSunSettings(Vector3.UnitY, Color.White, 1, -0.01f)
		);
		Assert.Throws<ArgumentOutOfRangeException>(
			() => new VoxelSunSettings(Vector3.UnitY, Color.White, 1, 1.01f)
		);
		Assert.Throws<ArgumentOutOfRangeException>(
			() => new VoxelSunSettings(Vector3.UnitY, Color.White, 1, float.NaN)
		);
	}

	private static (VoxelWorld World, VoxelPalette Palette, ushort Material) CreateCube(
		VoxelRenderMode renderMode,
		int x,
		int y,
		int z,
		bool doubleSided = false,
		VoxelWaveSettings? wave = null
	)
	{
		VoxelPaletteBuilder builder = new();
		ushort material = builder.Add(
			new VoxelMaterial(
				"Cube",
				renderMode,
				new VoxelFaceTiles(0),
				doubleSided: doubleSided,
				wave: wave
			)
		);
		VoxelPalette palette = builder.Build();
		VoxelWorld world = new();
		world.SetVoxel(x, y, z, new VoxelCell(material));
		return (world, palette, material);
	}

	private static void AssertUniformDeclaration(string source, string type, string name)
	{
		Assert.Contains($"uniform {type} {name};", source, StringComparison.Ordinal);
	}

	private static VoxelMeshData BuildLit(
		VoxelWorld world,
		VoxelPalette palette,
		ChunkCoordinate coordinate,
		ushort[] paddedLights
	)
	{
		return VoxelMesher.Build(
			world.CreateSnapshot(coordinate),
			palette,
			Atlas,
			new VoxelMeshingOptions { AmbientOcclusion = false },
			new VoxelLightChunkSnapshot(coordinate, 7, paddedLights)
		);
	}

	private static IEnumerable<VoxelVertex> Vertices(VoxelMeshData mesh, VoxelRenderMode renderMode)
	{
		return renderMode switch
		{
			VoxelRenderMode.Opaque => mesh.OpaqueVertices,
			VoxelRenderMode.Cutout => mesh.CutoutVertices,
			VoxelRenderMode.Transparent => mesh.TransparentFaces.SelectMany(face => face.Vertices),
			_ => throw new ArgumentOutOfRangeException(nameof(renderMode)),
		};
	}

	private static ushort[] CreatePaddedLights()
	{
		return new ushort[
			VoxelLightChunkSnapshot.PaddedSize
			* VoxelLightChunkSnapshot.PaddedSize
			* VoxelLightChunkSnapshot.PaddedSize
		];
	}

	private static void SetLight(
		ushort[] padded,
		int x,
		int y,
		int z,
		VoxelLight light
	)
	{
		int size = VoxelLightChunkSnapshot.PaddedSize;
		padded[(x + 1) + size * ((y + 1) + size * (z + 1))] = light.Packed;
	}

	private static void Drain(VoxelLighting lighting)
	{
		for (int update = 0; update < 1_000 && !lighting.IsIdle; update++)
		{
			lighting.Update(1_000_000);
		}

		Assert.True(lighting.IsIdle, "Voxel lighting did not converge.");
	}

	private static VoxelMeshData WaitForResult(VoxelMeshingScheduler scheduler)
	{
		VoxelMeshData result = null;
		bool completed = SpinWait.SpinUntil(() => scheduler.TryDequeue(out result), 5_000);

		Assert.True(completed, "Timed out waiting for voxel meshing worker.");
		return result;
	}

	private static void WaitForWorker(VoxelMeshingScheduler scheduler)
	{
		Assert.True(
			SpinWait.SpinUntil(() => scheduler.InFlightCount == 0, 5_000),
			"Timed out waiting for the voxel meshing worker to leave the in-flight set."
		);
	}
}
