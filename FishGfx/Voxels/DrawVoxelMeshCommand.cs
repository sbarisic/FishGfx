using System;
using System.Numerics;
using System.Threading;
using FishGfx.Graphics;

namespace FishGfx.Voxels;

internal sealed class DrawVoxelMeshCommand : RenderCommand, IDisposable
{
	private readonly VoxelMesh mesh;
	private readonly Texture atlas;
	private readonly ShaderProgram shader;
	private readonly RenderState state;
	private readonly float alphaCutoff;
	private readonly VoxelSunSettings sunSettings;
	private readonly VoxelFogSettings fogSettings;
	private int disposed;

	internal DrawVoxelMeshCommand(
		VoxelMesh mesh,
		Texture atlas,
		ShaderProgram shader,
		RenderState state,
		VoxelSunSettings sunSettings,
		float alphaCutoff,
		VoxelFogSettings fogSettings
	)
	{
		ArgumentNullException.ThrowIfNull(mesh);
		this.atlas = atlas ?? throw new ArgumentNullException(nameof(atlas));
		this.shader = shader ?? throw new ArgumentNullException(nameof(shader));

		if (!float.IsFinite(alphaCutoff))
		{
			throw new ArgumentOutOfRangeException(nameof(alphaCutoff));
		}

		sunSettings.Validate(nameof(sunSettings));
		this.state = state;
		this.sunSettings = sunSettings;
		this.alphaCutoff = alphaCutoff;
		this.fogSettings = fogSettings;
		mesh.RetainReference();
		this.mesh = mesh;
	}

	~DrawVoxelMeshCommand()
	{
		ReleaseReference();
	}

	public override void Execute(RenderPass pass)
	{
		if (pass == null)
		{
			throw new ArgumentNullException(nameof(pass));
		}

		ThrowIfDisposed();

		bool shaderBound = false;
		bool textureBound = false;

		using IDisposable stateScope = pass.PushState(state);

		try
		{
			shader.SetUniform("LightDirection", sunSettings.Direction);
			shader.SetUniform("AmbientLight", sunSettings.AmbientLight);
			shader.SetUniform("SunColor", (Vector3)sunSettings.Color);
			shader.SetUniform("SunIntensity", sunSettings.Intensity);
			shader.SetUniform("AlphaCutoff", alphaCutoff);
			shader.SetUniform("FogEnabled", fogSettings.Enabled ? 1 : 0);
			shader.SetUniform("FogColor", (Vector3)fogSettings.Color);
			shader.SetUniform("FogDensity", fogSettings.Density);
			shader.SetUniform(
				"LightMultiplier",
				fogSettings.Enabled ? fogSettings.LightMultiplier : 1
			);
			shader.Bind(pass.Uniforms);
			shaderBound = true;
			atlas.BindTextureUnit();
			textureBound = true;
			mesh.DrawRetained();
		}
		finally
		{
			if (textureBound)
			{
				atlas.UnbindTextureUnit();
			}

			if (shaderBound)
			{
				shader.Unbind();
			}
		}
	}

	public void Dispose()
	{
		ReleaseReference();
		GC.SuppressFinalize(this);
	}

	private void ReleaseReference()
	{
		if (Interlocked.Exchange(ref disposed, 1) != 0)
		{
			return;
		}

		mesh?.ReleaseReference();
	}

	private void ThrowIfDisposed()
	{
		if (Volatile.Read(ref disposed) != 0)
		{
			throw new ObjectDisposedException(nameof(DrawVoxelMeshCommand));
		}
	}
}
