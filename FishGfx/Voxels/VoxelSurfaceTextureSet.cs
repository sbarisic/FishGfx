using System;
using FishGfx.Graphics;

namespace FishGfx.Voxels;

public sealed class VoxelSurfaceTextureSet
{
	public VoxelSurfaceTextureSet(
		Texture modelAtlas,
		Texture cubeBaseColor,
		Texture normal,
		Texture specular,
		Texture roughness
	)
	{
		ModelAtlas = modelAtlas ?? throw new ArgumentNullException(nameof(modelAtlas));
		CubeBaseColor = cubeBaseColor ?? throw new ArgumentNullException(nameof(cubeBaseColor));
		Normal = normal ?? throw new ArgumentNullException(nameof(normal));
		Specular = specular ?? throw new ArgumentNullException(nameof(specular));
		Roughness = roughness ?? throw new ArgumentNullException(nameof(roughness));
		ValidateModelAtlas(ModelAtlas, nameof(modelAtlas));
		ValidateArray(CubeBaseColor, nameof(cubeBaseColor));

		if (ModelAtlas.Format != TextureFormat.SRGB8Alpha8
			|| ModelAtlas.MipLevels != 1)
		{
			throw new ArgumentException(
				"The voxel model atlas must use SRGB8Alpha8 storage with one mip level.",
				nameof(modelAtlas)
			);
		}

		if (CubeBaseColor.Format != TextureFormat.SRGB8Alpha8)
		{
			throw new ArgumentException(
				"The cube base-color array must use SRGB8Alpha8 storage.",
				nameof(cubeBaseColor)
			);
		}

		ValidateArray(Normal, nameof(normal));
		ValidateArray(Specular, nameof(specular));
		ValidateArray(Roughness, nameof(roughness));
		ValidateLinearSurfaceMap(Normal, nameof(normal));
		ValidateLinearSurfaceMap(Specular, nameof(specular));
		ValidateLinearSurfaceMap(Roughness, nameof(roughness));
		ValidateDimensions(Normal, nameof(normal));
		ValidateDimensions(Specular, nameof(specular));
		ValidateDimensions(Roughness, nameof(roughness));
	}

	public Texture ModelAtlas { get; }

	public Texture CubeBaseColor { get; }

	public Texture Normal { get; }

	public Texture Specular { get; }

	public Texture Roughness { get; }

	public bool HasSurfaceMaps => true;

	internal void EnsureOwner(GraphicsContext graphics)
	{
		ModelAtlas.EnsureOwner(graphics);
		CubeBaseColor.EnsureOwner(graphics);
		Normal.EnsureOwner(graphics);
		Specular.EnsureOwner(graphics);
		Roughness.EnsureOwner(graphics);
	}

	internal IDisposable Bind(ShaderProgram shader)
	{
		ArgumentNullException.ThrowIfNull(shader);
		shader.SetUniform("CubeBaseColor", 0);
		shader.SetUniform("CubeNormal", 1);
		shader.SetUniform("CubeSpecular", 2);
		shader.SetUniform("CubeRoughness", 3);
		shader.SetUniform("ModelAtlas", 4);
		shader.SetUniform("SurfaceMapsEnabled", 1);
		IDisposable[] bindings = new IDisposable[5];
		int bound = 0;

		try
		{
			bindings[bound++] = CubeBaseColor.Bind(0);

			bindings[bound++] = Normal.Bind(1);
			bindings[bound++] = Specular.Bind(2);
			bindings[bound++] = Roughness.Bind(3);

			bindings[bound++] = ModelAtlas.Bind(4);

			return new BindingScope(bindings);
		}
		catch
		{
			for (int index = bound - 1; index >= 0; index--)
			{
				bindings[index]?.Dispose();
			}

			throw;
		}
	}

	private void ValidateDimensions(Texture texture, string parameterName)
	{
		if (texture.Width != CubeBaseColor.Width
			|| texture.Height != CubeBaseColor.Height
			|| texture.ArrayLayers != CubeBaseColor.ArrayLayers
			|| texture.MipLevels != CubeBaseColor.MipLevels)
		{
			throw new ArgumentException(
				"All voxel surface textures must have identical dimensions.",
				parameterName
			);
		}
	}

	private static void ValidateModelAtlas(Texture texture, string parameterName)
	{
		if (texture.Is2DArray
			|| texture.Is3D
			|| texture.IsCubeMap
			|| texture.Multisampled
			|| Texture.IsDepthFormat(texture.Format))
		{
			throw new ArgumentException(
				"The voxel model atlas must be an ordinary two-dimensional color texture.",
				parameterName
			);
		}

		if ((texture.Usage & TextureUsageFlags.Sampled) == 0)
		{
			throw new ArgumentException(
				"Voxel surface maps require Sampled usage.",
				parameterName
			);
		}
	}

	private static void ValidateArray(Texture texture, string parameterName)
	{
		if (!texture.Is2DArray || Texture.IsDepthFormat(texture.Format))
		{
			throw new ArgumentException(
				"Cube voxel surface textures must be two-dimensional texture arrays.",
				parameterName
			);
		}

		if ((texture.Usage & TextureUsageFlags.Sampled) == 0)
		{
			throw new ArgumentException(
				"Voxel surface maps require Sampled usage.",
				parameterName
			);
		}
	}

	private static void ValidateLinearSurfaceMap(Texture texture, string parameterName)
	{
		ValidateLinearSurfaceMapFormat(texture.Format, parameterName);
	}

	internal static void ValidateLinearSurfaceMapFormat(
		TextureFormat format,
		string parameterName
	)
	{
		if (format != TextureFormat.RGBA8Unorm)
		{
			throw new ArgumentException(
				"Voxel normal, specular, and roughness maps must use linear RGBA8Unorm data.",
				parameterName
			);
		}
	}

	private sealed class BindingScope : IDisposable
	{
		private IDisposable[] bindings;

		internal BindingScope(IDisposable[] bindings)
		{
			this.bindings = bindings;
		}

		public void Dispose()
		{
			IDisposable[] current = System.Threading.Interlocked.Exchange(
				ref bindings,
				null
			);

			if (current == null)
			{
				return;
			}

			for (int index = current.Length - 1; index >= 0; index--)
			{
				current[index]?.Dispose();
			}
		}
	}
}
