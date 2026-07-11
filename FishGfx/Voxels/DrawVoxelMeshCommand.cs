using System;
using System.Numerics;
using FishGfx.Graphics;

namespace FishGfx.Voxels
{
	public sealed class DrawVoxelMeshCommand : GraphicsCommand
	{
		public DrawVoxelMeshCommand(
			VoxelMesh mesh,
			Texture atlas,
			ShaderProgram shader,
			Vector3 lightDirection,
			float ambientLight,
			float alphaCutoff
		)
		{
			Mesh = mesh ?? throw new ArgumentNullException(nameof(mesh));
			Atlas = atlas ?? throw new ArgumentNullException(nameof(atlas));
			Shader = shader ?? throw new ArgumentNullException(nameof(shader));

			if (!IsFinite(lightDirection) || lightDirection.LengthSquared() <= 0)
				throw new ArgumentOutOfRangeException(nameof(lightDirection));
			if (!float.IsFinite(ambientLight) || ambientLight < 0 || ambientLight > 1)
				throw new ArgumentOutOfRangeException(nameof(ambientLight));
			if (!float.IsFinite(alphaCutoff))
				throw new ArgumentOutOfRangeException(nameof(alphaCutoff));

			LightDirection = Vector3.Normalize(lightDirection);
			AmbientLight = ambientLight;
			AlphaCutoff = alphaCutoff;
		}

		public VoxelMesh Mesh { get; }
		public Texture Atlas { get; }
		public ShaderProgram Shader { get; }
		public Vector3 LightDirection { get; }
		public float AmbientLight { get; }
		public float AlphaCutoff { get; }

		public override void Execute()
		{
			bool shaderBound = false;
			bool textureBound = false;

			try
			{
				Shader.Uniform3f("LightDirection", LightDirection);
				Shader.Uniform1f("AmbientLight", AmbientLight);
				Shader.Uniform1f("AlphaCutoff", AlphaCutoff);
				Shader.Bind(ShaderUniforms.Current);
				shaderBound = true;
				Atlas.BindTextureUnit();
				textureBound = true;
				Mesh.Draw();
			}
			finally
			{
				if (textureBound)
					Atlas.UnbindTextureUnit();
				if (shaderBound)
					Shader.Unbind();
			}
		}

		private static bool IsFinite(Vector3 value)
		{
			return float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
		}
	}
}
