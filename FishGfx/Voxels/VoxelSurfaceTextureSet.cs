using System;
using FishGfx.Graphics;

namespace FishGfx.Voxels;

public sealed class VoxelSurfaceTextureSet
{
	public VoxelSurfaceTextureSet(
		Texture baseColor,
		Texture normal = null,
		Texture specular = null,
		Texture roughness = null
	)
	{
		BaseColor = baseColor ?? throw new ArgumentNullException(nameof(baseColor));
		bool hasAnySurfaceMap = normal != null || specular != null || roughness != null;
		bool hasEverySurfaceMap = normal != null && specular != null && roughness != null;

		if (hasAnySurfaceMap && !hasEverySurfaceMap)
		{
			throw new ArgumentException(
				"Normal, specular, and roughness textures must be supplied together."
			);
		}

		Normal = normal;
		Specular = specular;
		Roughness = roughness;
		ValidateTexture(BaseColor, nameof(baseColor));

		if (hasEverySurfaceMap)
		{
			ValidateTexture(Normal, nameof(normal));
			ValidateTexture(Specular, nameof(specular));
			ValidateTexture(Roughness, nameof(roughness));
			ValidateLinearSurfaceMap(Normal, nameof(normal));
			ValidateLinearSurfaceMap(Specular, nameof(specular));
			ValidateLinearSurfaceMap(Roughness, nameof(roughness));
			ValidateDimensions(Normal, nameof(normal));
			ValidateDimensions(Specular, nameof(specular));
			ValidateDimensions(Roughness, nameof(roughness));
		}
	}

	public Texture BaseColor { get; }

	public Texture Normal { get; }

	public Texture Specular { get; }

	public Texture Roughness { get; }

	public bool HasSurfaceMaps => Normal != null;

	internal VoxelSurfaceTextureSet WithBaseColor(Texture baseColor)
	{
		return new VoxelSurfaceTextureSet(baseColor, Normal, Specular, Roughness);
	}

	internal void EnsureOwner(GraphicsContext graphics)
	{
		BaseColor.EnsureOwner(graphics);
		Normal?.EnsureOwner(graphics);
		Specular?.EnsureOwner(graphics);
		Roughness?.EnsureOwner(graphics);
	}

	internal IDisposable Bind(ShaderProgram shader)
	{
		ArgumentNullException.ThrowIfNull(shader);
		shader.SetUniform("Texture0", 0);
		shader.SetUniform("NormalTexture", 1);
		shader.SetUniform("SpecularTexture", 2);
		shader.SetUniform("RoughnessTexture", 3);
		shader.SetUniform("SurfaceMapsEnabled", HasSurfaceMaps ? 1 : 0);
		IDisposable[] bindings = new IDisposable[HasSurfaceMaps ? 4 : 1];
		int bound = 0;

		try
		{
			bindings[bound++] = BaseColor.Bind(0);

			if (HasSurfaceMaps)
			{
				bindings[bound++] = Normal.Bind(1);
				bindings[bound++] = Specular.Bind(2);
				bindings[bound++] = Roughness.Bind(3);
			}

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
		if (texture.Width != BaseColor.Width || texture.Height != BaseColor.Height)
		{
			throw new ArgumentException(
				"All voxel surface textures must have identical dimensions.",
				parameterName
			);
		}
	}

	private static void ValidateTexture(Texture texture, string parameterName)
	{
		if (texture.Is3D
			|| texture.IsCubeMap
			|| texture.Multisampled
			|| Texture.IsDepthFormat(texture.Format))
		{
			throw new ArgumentException(
				"Voxel surface textures must be ordinary two-dimensional color textures.",
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
