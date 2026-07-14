using System;
using System.Numerics;
using FishGfx.Graphics;

namespace FishGfx.Voxels
{
	public sealed class DrawVoxelMeshCommand : GraphicsCommand
	{
		private VoxelSunSettings sun;

		public DrawVoxelMeshCommand(
			VoxelMesh mesh,
			Texture atlas,
			ShaderProgram shader,
			Vector3 lightDirection,
			float ambientLight,
			float alphaCutoff
		)
			: this(
				mesh,
				atlas,
				shader,
				lightDirection,
				ambientLight,
				alphaCutoff,
				VoxelFogSettings.Disabled
			)
		{
		}

		public DrawVoxelMeshCommand(
			VoxelMesh mesh,
			Texture atlas,
			ShaderProgram shader,
			Vector3 lightDirection,
			float ambientLight,
			float alphaCutoff,
			VoxelFogSettings fog
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

			sun = new VoxelSunSettings(lightDirection, Color.White, 1, ambientLight);
			AlphaCutoff = alphaCutoff;
			Fog = fog;
		}

		public VoxelMesh Mesh { get; }
		public Texture Atlas { get; }
		public ShaderProgram Shader { get; }
		public Vector3 LightDirection => sun.Direction;
		public float AmbientLight => sun.AmbientLight;
		public float AlphaCutoff { get; }
		public VoxelFogSettings Fog { get; internal set; }
		internal VoxelSunSettings Sun
		{
			get => sun;
			set
			{
				value.Validate(nameof(value));
				sun = value;
			}
		}

		public override void Execute()
		{
			bool shaderBound = false;
			bool textureBound = false;

			try
			{
				Shader.Uniform3f("LightDirection", LightDirection);
				Shader.Uniform1f("AmbientLight", AmbientLight);
				Shader.Uniform3f("SunColor", (Vector3)sun.Color);
				Shader.Uniform1f("SunIntensity", sun.Intensity);
				Shader.Uniform1f("AlphaCutoff", AlphaCutoff);
				Shader.Uniform1("FogEnabled", Fog.Enabled ? 1 : 0);
				Shader.Uniform3f("FogColor", (Vector3)Fog.Color);
				Shader.Uniform1f("FogDensity", Fog.Density);
				Shader.Uniform1f("LightMultiplier", Fog.Enabled ? Fog.LightMultiplier : 1);
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
