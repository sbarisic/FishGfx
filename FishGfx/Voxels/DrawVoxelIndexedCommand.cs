using System;
using System.Numerics;
using System.Threading;
using FishGfx.Graphics;
using FishGfx.Graphics.Shadows;

namespace FishGfx.Voxels;

internal sealed class DrawVoxelIndexedCommand : RenderCommand, IDisposable
{
	private readonly VoxelTransparentDrawSnapshot snapshot;
	private readonly VoxelSurfaceTextureSet textures;
	private readonly ShaderProgram shader;
	private readonly RenderState state;
	private readonly VoxelSunSettings sun;
	private readonly VoxelFogSettings fog;
	private readonly VoxelGpuTimer gpuTimer;
	private readonly DirectionalShadowFrame? shadows;
	private int disposed;

	internal DrawVoxelIndexedCommand(
		VoxelTransparentDrawSnapshot snapshot,
		VoxelSurfaceTextureSet textures,
		ShaderProgram shader,
		RenderState state,
		VoxelSunSettings sun,
		VoxelFogSettings fog,
		VoxelGpuTimer gpuTimer,
		DirectionalShadowFrame? shadows
	)
	{
		this.snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
		this.textures = textures ?? throw new ArgumentNullException(nameof(textures));
		this.shader = shader ?? throw new ArgumentNullException(nameof(shader));
		this.state = state;
		sun.Validate(nameof(sun));
		this.sun = sun;
		this.fog = fog;
		this.gpuTimer = gpuTimer ?? throw new ArgumentNullException(nameof(gpuTimer));
		this.shadows = shadows;
		snapshot.RetainReference();
	}

	~DrawVoxelIndexedCommand()
	{
		ReleaseReference();
	}

	public override void Execute(RenderPass pass)
	{
		ArgumentNullException.ThrowIfNull(pass);
		ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
		bool shaderBound = false;
		IDisposable textureBindings = null;
		IDisposable shadowBindings = null;
		using IDisposable stateScope = pass.PushState(state);
		int queryIndex = gpuTimer.Begin(pass, out IDisposable queryScope);

		try
		{
			shader.SetUniform("LightDirection", sun.Direction);
			shader.SetUniform("AmbientLight", sun.AmbientLight);
			shader.SetUniform("SunColor", (Vector3)sun.Color);
			shader.SetUniform("SunIntensity", sun.Intensity);
			shader.SetUniform("AlphaCutoff", -1f);
			shader.SetUniform("FogEnabled", fog.Enabled ? 1 : 0);
			shader.SetUniform("FogColor", (Vector3)fog.Color);
			shader.SetUniform("FogDensity", fog.Density);
			shader.SetUniform("LightMultiplier", fog.Enabled ? fog.LightMultiplier : 1);
			shader.SetUniform("uShadowEnabled", 0);
			shadowBindings = shadows?.Bind(shader, 4);
			textureBindings = textures.Bind(shader);
			shader.Bind(pass.Uniforms);
			shaderBound = true;
			snapshot.DrawRetained();
		}
		finally
		{
			try
			{
				shadowBindings?.Dispose();

				textureBindings?.Dispose();

				if (shaderBound)
				{
					shader.Unbind();
				}
			}
			finally
			{
				gpuTimer.End(queryIndex, queryScope);
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
		if (Interlocked.Exchange(ref disposed, 1) == 0)
		{
			snapshot?.ReleaseReference();
		}
	}
}
