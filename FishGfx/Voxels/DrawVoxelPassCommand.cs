using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using FishGfx.Graphics;

namespace FishGfx.Voxels;

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
/// Each command owns an immutable entry snapshot until its render queue is cleared.
/// </summary>
internal sealed class DrawVoxelPassCommand : RenderCommand, IDisposable
{
	private readonly Texture atlas;
	private readonly ShaderProgram shader;
	private readonly RenderState state;
	private readonly VoxelSunSettings sun;
	private readonly VoxelFogSettings fog;
	private readonly float alphaCutoff;
	private readonly VoxelPassDrawEntry[] entries;
	private int disposed;

	internal DrawVoxelPassCommand(
		Texture atlas,
		ShaderProgram shader,
		RenderState state,
		VoxelSunSettings sunSettings,
		float alphaCutoff,
		VoxelFogSettings fogSettings,
		IReadOnlyList<VoxelPassEntry> source
	)
	{
		this.atlas = atlas ?? throw new ArgumentNullException(nameof(atlas));
		this.shader = shader ?? throw new ArgumentNullException(nameof(shader));
		this.state = state;
		sunSettings.Validate(nameof(sunSettings));
		sun = sunSettings;
		fog = fogSettings;
		this.alphaCutoff = alphaCutoff;
		ArgumentNullException.ThrowIfNull(source);
		entries = new VoxelPassDrawEntry[source.Count];
		int retained = 0;

		try
		{
			for (int i = 0; i < source.Count; i++)
			{
				VoxelPassEntry entry = source[i];
				entry.Mesh.RetainReference();
				entries[i] = new VoxelPassDrawEntry(
					entry.Mesh,
					entry.Model,
					entry.Coordinate
				);
				retained++;
			}
		}
		catch
		{
			for (int i = 0; i < retained; i++)
			{
				entries[i].Mesh.ReleaseReference();
			}

			Volatile.Write(ref disposed, 1);
			throw;
		}
	}

	internal int Count => entries.Length;

	~DrawVoxelPassCommand()
	{
		ReleaseReferences();
	}

	public override void Execute(RenderPass pass)
	{
		if (pass == null)
		{
			throw new ArgumentNullException(nameof(pass));
		}

		ThrowIfDisposed();

		if (entries.Length == 0)
		{
			return;
		}

		bool shaderBound = false;
		bool textureBound = false;

		using IDisposable stateScope = pass.PushState(state);

		try
		{
			shader.SetUniform("LightDirection", sun.Direction);
			shader.SetUniform("AmbientLight", sun.AmbientLight);
			shader.SetUniform("SunColor", (Vector3)sun.Color);
			shader.SetUniform("SunIntensity", sun.Intensity);
			shader.SetUniform("AlphaCutoff", alphaCutoff);
			shader.SetUniform("FogEnabled", fog.Enabled ? 1 : 0);
			shader.SetUniform("FogColor", (Vector3)fog.Color);
			shader.SetUniform("FogDensity", fog.Density);
			shader.SetUniform(
				"LightMultiplier",
				fog.Enabled ? fog.LightMultiplier : 1
			);
			shader.Bind(pass.Uniforms);
			shaderBound = true;
			atlas.BindTextureUnit();
			textureBound = true;

			for (int i = 0; i < entries.Length; i++)
			{
				using IDisposable modelScope = pass.PushModel(entries[i].Model);
				shader.SetUniform(RenderUniformState.ModelUniformName, entries[i].Model);
				entries[i].Mesh.DrawRetained();
			}
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
		ReleaseReferences();
		GC.SuppressFinalize(this);
	}

	private void ReleaseReferences()
	{
		if (Interlocked.Exchange(ref disposed, 1) != 0)
		{
			return;
		}

		if (entries == null)
		{
			return;
		}

		foreach (VoxelPassDrawEntry entry in entries)
		{
			entry.Mesh?.ReleaseReference();
		}
	}

	private void ThrowIfDisposed()
	{
		if (Volatile.Read(ref disposed) != 0)
		{
			throw new ObjectDisposedException(nameof(DrawVoxelPassCommand));
		}
	}

	private readonly struct VoxelPassDrawEntry
	{
		internal VoxelPassDrawEntry(
			VoxelMesh mesh,
			Matrix4x4 model,
			ChunkCoordinate coordinate
		)
		{
			Mesh = mesh ?? throw new ArgumentNullException(nameof(mesh));
			Model = model;
			Coordinate = coordinate;
		}

		internal VoxelMesh Mesh { get; }
		internal Matrix4x4 Model { get; }
		internal ChunkCoordinate Coordinate { get; }
	}
}
