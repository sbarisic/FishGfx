using System;
using System.Collections.Generic;
using System.Numerics;
using FishGfx.Graphics;

namespace FishGfx.Voxels
{
	internal readonly struct VoxelPassEntry
	{
		internal VoxelPassEntry(VoxelMesh mesh, Matrix4x4 model, ChunkCoordinate coordinate, float depth)
		{
			Mesh = mesh ?? throw new ArgumentNullException(nameof(mesh));
			Model = model;
			Coordinate = coordinate;
			Depth = depth;
		}

		internal VoxelMesh Mesh { get; }
		internal Matrix4x4 Model { get; }
		internal ChunkCoordinate Coordinate { get; }
		internal float Depth { get; }
	}

	/// <summary>
	/// Draws one homogeneous voxel pass while binding its common resources only once.
	/// The renderer owns and updates the entry snapshot before queue submission.
	/// </summary>
	internal sealed class DrawVoxelPassCommand : GraphicsCommand
	{
		private readonly Texture atlas;
		private readonly ShaderProgram shader;
		private readonly RenderState state;
		private readonly Vector3 lightDirection;
		private readonly float ambientLight;
		private readonly float alphaCutoff;
		private VoxelPassEntry[] entries = Array.Empty<VoxelPassEntry>();
		private int count;

		internal DrawVoxelPassCommand(
			Texture atlas,
			ShaderProgram shader,
			RenderState state,
			Vector3 lightDirection,
			float ambientLight,
			float alphaCutoff
		)
		{
			this.atlas = atlas ?? throw new ArgumentNullException(nameof(atlas));
			this.shader = shader ?? throw new ArgumentNullException(nameof(shader));
			this.state = state;
			this.lightDirection = Vector3.Normalize(lightDirection);
			this.ambientLight = ambientLight;
			this.alphaCutoff = alphaCutoff;
		}

		internal int Count => count;
		internal VoxelFogSettings Fog { get; set; }

		internal void Update(IReadOnlyList<VoxelPassEntry> source)
		{
			if (source == null)
				throw new ArgumentNullException(nameof(source));

			if (entries.Length < source.Count)
				Array.Resize(ref entries, VoxelMesh.CalculateCapacity(entries.Length, source.Count));

			for (int i = 0; i < source.Count; i++)
				entries[i] = source[i];

			count = source.Count;
		}

		public override void Execute()
		{
			if (count == 0)
				return;

			ShaderUniforms uniforms = ShaderUniforms.Current;
			Matrix4x4 previousModel = uniforms.Model;
			bool statePushed = false;
			bool shaderBound = false;
			bool textureBound = false;

			try
			{
				Gfx.PushRenderState(state);
				statePushed = true;
				shader.Uniform3f("LightDirection", lightDirection);
				shader.Uniform1f("AmbientLight", ambientLight);
				shader.Uniform1f("AlphaCutoff", alphaCutoff);
				shader.Uniform1("FogEnabled", Fog.Enabled ? 1 : 0);
				shader.Uniform3f("FogColor", (Vector3)Fog.Color);
				shader.Uniform1f("FogDensity", Fog.Density);
				shader.Uniform1f("LightMultiplier", Fog.Enabled ? Fog.LightMultiplier : 1);
				shader.Bind(uniforms);
				shaderBound = true;
				atlas.BindTextureUnit();
				textureBound = true;

				for (int i = 0; i < count; i++)
				{
					uniforms.Model = entries[i].Model;
					shader.UniformMatrix4f("Model", entries[i].Model);
					entries[i].Mesh.Draw();
				}
			}
			finally
			{
				uniforms.Model = previousModel;

				if (textureBound)
					atlas.UnbindTextureUnit();
				if (shaderBound)
					shader.Unbind();
				if (statePushed)
					Gfx.PopRenderState();
			}
		}
	}
}
