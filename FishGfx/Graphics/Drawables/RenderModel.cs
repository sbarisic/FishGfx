using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using FishGfx.Formats;
using FishGfx.Graphics;

namespace FishGfx.Graphics.Drawables
{
	public class RenderModel : IDrawable, IDisposable
	{
		class SubMesh
		{
			public string MaterialName;
			public Texture Texture;
			public Mesh3D Mesh;

			public SubMesh(string MatName, Texture Tex, Mesh3D Msh)
			{
				MaterialName = MatName;
				Texture = Tex;
				Mesh = Msh;
			}
		}

		SubMesh[] Meshes;

		public RenderModel(IEnumerable<GenericMesh> Meshes, bool HasUVs = true, bool HasColors = true)
		{
			if (Meshes == null) throw new ArgumentNullException(nameof(Meshes));
			GenericMesh[] GenericMeshes = Meshes.ToArray();
			this.Meshes = new SubMesh[GenericMeshes.Length];

			for (int i = 0; i < GenericMeshes.Length; i++)
				this.Meshes[i] = new SubMesh(
					GenericMeshes[i].MaterialName,
					null,
					new Mesh3D(GenericMeshes[i], HasUVs, HasColors)
				);
		}

		public void SetMaterialTexture(string MaterialName, Texture Tex)
		{
			foreach (var M in Meshes)
				if (M.MaterialName == MaterialName)
				{
					M.Texture = Tex;
					return;
				}

			throw new Exception("Material not found " + MaterialName);
		}

		public Mesh3D GetMaterialMesh(string MaterialName)
		{
			foreach (var M in Meshes)
				if (M.MaterialName == MaterialName)
					return M.Mesh;

			throw new Exception("Material mesh not found " + MaterialName);
		}

		public IEnumerable<string> GetMaterialNames()
		{
			foreach (var M in Meshes)
				yield return M.MaterialName;
		}

		public void Draw()
		{
			foreach (var M in Meshes)
			{
				bool textureBound = false;

				try
				{
					if (M.Texture != null)
					{
						M.Texture.BindTextureUnit();
						textureBound = true;
					}

					M.Mesh.Draw();
				}
				finally
				{
					if (textureBound)
						M.Texture.UnbindTextureUnit();
				}
			}
		}

		public void Draw(ShaderProgram Shader, ShaderUniforms Uniforms)
		{
			if (Shader == null) throw new ArgumentNullException(nameof(Shader));
			if (Uniforms == null) throw new ArgumentNullException(nameof(Uniforms));
			Shader.Bind(Uniforms);
			try { Draw(); }
			finally { Shader.Unbind(); }
		}

		public void Dispose()
		{
			foreach (SubMesh mesh in Meshes)
				mesh.Mesh.Dispose();
		}
	}
}
