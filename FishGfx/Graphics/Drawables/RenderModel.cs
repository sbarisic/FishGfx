using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using FishGfx.Formats;

namespace FishGfx.Graphics.Drawables;

public sealed class RenderModel : IRenderable, IDisposable
{
	private readonly GraphicsContext graphics;
	private readonly MaterialPart[] parts;
	private readonly ReadOnlyCollection<string> materialNames;
	private bool disposed;

	public RenderModel(
		GraphicsContext graphics,
		IEnumerable<GenericMesh> meshes,
		ShaderProgram shader,
		bool includeUvs = true,
		bool includeColors = true
	)
	{
		ArgumentNullException.ThrowIfNull(graphics);
		ArgumentNullException.ThrowIfNull(meshes);
		this.graphics = graphics;
		Shader = shader ?? throw new ArgumentNullException(nameof(shader));
		Shader.EnsureOwner(graphics);

		GenericMesh[] sourceMeshes = meshes.ToArray();
		parts = new MaterialPart[sourceMeshes.Length];
		string[] names = new string[sourceMeshes.Length];

		try
		{
			for (int index = 0; index < sourceMeshes.Length; index++)
			{
				GenericMesh source = sourceMeshes[index]
					?? throw new ArgumentException("Meshes cannot contain null values.", nameof(meshes));
				Mesh3D mesh = graphics.CreateMesh3D(source, includeUvs, includeColors);
				parts[index] = new MaterialPart(source.MaterialName, mesh);
				names[index] = source.MaterialName;
			}
		}
		catch
		{
			DisposeParts();

			throw;
		}

		materialNames = Array.AsReadOnly(names);
	}

	public ShaderProgram Shader { get; }

	public IReadOnlyList<string> MaterialNames => materialNames;

	public void SetMaterialTexture(string materialName, Texture texture)
	{
		ThrowIfDisposed();
		MaterialPart part = FindPart(materialName);

		if (texture != null)
		{
			texture.EnsureOwner(graphics);
		}

		part.Texture = texture;
	}

	public Mesh3D GetMaterialMesh(string materialName)
	{
		ThrowIfDisposed();

		return FindPart(materialName).Mesh;
	}

	public void Render(RenderPass pass)
	{
		Render(pass, Shader);
	}

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}

		disposed = true;
		DisposeParts();
	}

	internal void Render(RenderPass pass, ShaderProgram shader)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(pass);
		ArgumentNullException.ThrowIfNull(shader);
		pass.EnsureActive();
		shader.EnsureOwner(pass.Context);
		shader.Bind(pass.Uniforms);

		try
		{
			foreach (MaterialPart part in parts)
			{
				part.Texture?.EnsureOwner(pass.Context);
				part.Texture?.BindTextureUnit();

				try
				{
					part.Mesh.Draw();
				}
				finally
				{
					part.Texture?.UnbindTextureUnit();
				}
			}
		}
		finally
		{
			shader.Unbind();
		}
	}

	private MaterialPart FindPart(string materialName)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(materialName);

		foreach (MaterialPart part in parts)
		{
			if (string.Equals(part.MaterialName, materialName, StringComparison.Ordinal))
			{
				return part;
			}
		}

		throw new KeyNotFoundException($"Material '{materialName}' was not found.");
	}

	private void DisposeParts()
	{
		foreach (MaterialPart part in parts)
		{
			part?.Mesh.Dispose();
		}
	}

	private void ThrowIfDisposed()
	{
		if (disposed)
		{
			throw new ObjectDisposedException(nameof(RenderModel));
		}
	}

	private sealed class MaterialPart
	{
		internal MaterialPart(string materialName, Mesh3D mesh)
		{
			MaterialName = materialName;
			Mesh = mesh;
		}

		internal string MaterialName { get; }

		internal Mesh3D Mesh { get; }

		internal Texture Texture { get; set; }
	}
}
