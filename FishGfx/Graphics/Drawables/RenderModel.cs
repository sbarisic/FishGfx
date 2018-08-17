using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FishGfx.Graphics;
using FishGfx.Formats;

namespace FishGfx.Graphics.Drawables {
	public class RenderModel : IDrawable {
		class SubMesh {
			public string MaterialName;
			public Texture Texture;
			public Mesh3D Mesh;

			public SubMesh(string MatName, Texture Tex, Mesh3D Msh) {
				MaterialName = MatName;
				Texture = Tex;
				Mesh = Msh;
			}
		}

		SubMesh[] Meshes;

		public RenderModel(IEnumerable<GenericMesh> Meshes, bool HasUVs = true, bool HasColors = true) {
			GenericMesh[] GenericMeshes = Meshes.ToArray();
			this.Meshes = new SubMesh[GenericMeshes.Length];

			for (int i = 0; i < GenericMeshes.Length; i++)
				this.Meshes[i] = new SubMesh(GenericMeshes[i].MaterialName, null, new Mesh3D(GenericMeshes[i], HasUVs, HasColors));
		}

		public void SetMaterialTexture(string MaterialName, Texture Tex) {
			foreach (var M in Meshes)
				if (M.MaterialName == MaterialName) {
					M.Texture = Tex;
					return;
				}

			throw new Exception("Material not found " + MaterialName);
		}

		public Mesh3D GetMaterialMesh(string MaterialName) {
			foreach (var M in Meshes)
				if (M.MaterialName == MaterialName)
					return M.Mesh;

			throw new Exception("Material mesh not found " + MaterialName);
		}

		public IEnumerable<string> GetMaterialNames() {
			foreach (var M in Meshes)
				yield return M.MaterialName;
		}

		public void Draw() {
			foreach (var M in Meshes) {
				if (M.Texture != null)
					M.Texture.BindTextureUnit();

				M.Mesh.Draw();

				if (M.Texture != null)
					M.Texture.UnbindTextureUnit();
			}
		}
	}
}
