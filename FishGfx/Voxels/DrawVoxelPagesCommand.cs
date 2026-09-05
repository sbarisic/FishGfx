using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading;
using FishGfx.Graphics;
using FishGfx.Graphics.Shadows;

namespace FishGfx.Voxels;

internal readonly struct VoxelPassEntry
{
	internal VoxelPassEntry(
		VoxelGeometryAllocation allocation,
		ChunkCoordinate coordinate,
		float depth
	)
	{
		Allocation = allocation ?? throw new ArgumentNullException(nameof(allocation));
		Coordinate = coordinate;
		Depth = depth;
	}

	internal VoxelGeometryAllocation Allocation { get; }

	internal ChunkCoordinate Coordinate { get; }

	internal float Depth { get; }
}

internal sealed class DrawVoxelPagesCommand : RenderCommand, IDisposable
{
	private readonly VoxelSurfaceTextureSet textures;
	private readonly IDisposable textureReference;
	private readonly VoxelRendererPresentationMode presentationMode;
	private readonly long surfaceTextureGeneration;
	private readonly ShaderProgram shader;
	private readonly RenderState state;
	private readonly VoxelSunSettings sun;
	private readonly VoxelFogSettings fog;
	private readonly float cutoutAlphaCutoff;
	private readonly GraphicsBuffer indirectBuffer;
	private readonly VoxelGpuTimer gpuTimer;
	private readonly VoxelPageDrawGroup[] opaqueGroups;
	private readonly VoxelPageDrawGroup[] cutoutGroups;
	private readonly VoxelRenderer renderer;
	private readonly DirectionalShadowFrame? shadows;
	private int disposed;

	internal DrawVoxelPagesCommand(
		VoxelSurfaceTextureSet textures,
		VoxelRendererPresentationMode presentationMode,
		long surfaceTextureGeneration,
		ShaderProgram shader,
		RenderState state,
		VoxelSunSettings sunSettings,
		float cutoutAlphaCutoff,
		VoxelFogSettings fogSettings,
		GraphicsBuffer indirectBuffer,
		VoxelGpuTimer gpuTimer,
		IReadOnlyList<VoxelPassEntry> opaqueEntries,
		IReadOnlyList<VoxelPassEntry> cutoutEntries,
		VoxelRenderer renderer,
		DirectionalShadowFrame? shadows
	)
	{
		this.textures = textures ?? throw new ArgumentNullException(nameof(textures));
		this.presentationMode = presentationMode;
		this.surfaceTextureGeneration = surfaceTextureGeneration;
		this.shader = shader ?? throw new ArgumentNullException(nameof(shader));
		this.state = state;
		sunSettings.Validate(nameof(sunSettings));
		sun = sunSettings;
		fog = fogSettings;
		this.cutoutAlphaCutoff = cutoutAlphaCutoff;
		this.indirectBuffer = indirectBuffer ?? throw new ArgumentNullException(nameof(indirectBuffer));
		this.gpuTimer = gpuTimer ?? throw new ArgumentNullException(nameof(gpuTimer));
		this.renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
		this.shadows = shadows;
		opaqueGroups = CreateGroups(opaqueEntries);

		try
		{
			cutoutGroups = CreateGroups(cutoutEntries);
		}
		catch
		{
			ReleaseGroups(opaqueGroups);
			throw;
		}
		try
		{
			textureReference = textures.RetainForSubmission();
		}
		catch
		{
			ReleaseGroups(opaqueGroups);
			ReleaseGroups(cutoutGroups);
			throw;
		}
	}

	internal int OpaqueGroupCount => opaqueGroups.Length;

	internal int CutoutGroupCount => cutoutGroups.Length;

	internal int OpaqueCommandCount => CountCommands(opaqueGroups);

	internal int CutoutCommandCount => CountCommands(cutoutGroups);

	internal VoxelRendererPresentationMode PresentationMode => presentationMode;

	internal long SurfaceTextureGeneration => surfaceTextureGeneration;

	~DrawVoxelPagesCommand()
	{
		ReleaseReferences();
	}

	public override void Execute(RenderPass pass)
	{
		ArgumentNullException.ThrowIfNull(pass);
		ThrowIfDisposed();

		if (opaqueGroups.Length == 0 && cutoutGroups.Length == 0)
		{
			return;
		}

		long allocationStart = GC.GetAllocatedBytesForCurrentThread();
		long start = Stopwatch.GetTimestamp();
		bool shaderBound = false;
		IDisposable textureBindings = null;
		IDisposable shadowBindings = null;
		using IDisposable stateScope = pass.PushState(state);
		int queryIndex = gpuTimer.Begin(pass, out IDisposable queryScope);

		try
		{
			shader.SetUniform("LightDirection", sun.Direction);
			shader.SetUniform("AmbientLight", sun.AmbientLight);
			shader.SetUniform("SunColor", ColorSpace.SrgbToLinear(sun.Color));
			shader.SetUniform("SunIntensity", sun.Intensity);
			shader.SetUniform("FogEnabled", fog.Enabled ? 1 : 0);
			shader.SetUniform("FogColor", ColorSpace.SrgbToLinear(fog.Color));
			shader.SetUniform("FogDensity", fog.Density);
			shader.SetUniform("LightMultiplier", fog.Enabled ? fog.LightMultiplier : 1);
			shader.SetUniform("AlphaCutoff", -1f);
			shader.SetUniform("uShadowEnabled", 0);
			shader.SetUniform("VoxelPresentationMode", (int)presentationMode);
			shadowBindings = shadows?.Bind(shader, 3);
			textureBindings = textures.Bind(shader);
			shader.Bind(pass.Uniforms);
			shaderBound = true;

			DrawGroups(opaqueGroups);

			if (cutoutGroups.Length > 0)
			{
				shader.SetUniform("AlphaCutoff", cutoutAlphaCutoff);
				DrawGroups(cutoutGroups);
			}
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
				renderer.RecordPageSubmission(
					Stopwatch.GetElapsedTime(start).TotalMilliseconds,
					checked((int)(GC.GetAllocatedBytesForCurrentThread() - allocationStart))
				);
			}
		}
	}

	public void Dispose()
	{
		ReleaseReferences();
		GC.SuppressFinalize(this);
	}

	private void DrawGroups(VoxelPageDrawGroup[] groups)
	{
		for (int groupIndex = 0; groupIndex < groups.Length; groupIndex++)
		{
			VoxelPageDrawGroup group = groups[groupIndex];
			EnsureIndirectCapacity(group.Count);
			indirectBuffer.Write(group.Commands.AsSpan(0, group.Count));
			group.Page.Draw(indirectBuffer, group.Count);
		}
	}

	private void EnsureIndirectCapacity(int commandCount)
	{
		int required = checked(commandCount * 16);

		if (required <= indirectBuffer.SizeInBytes)
		{
			return;
		}

		int capacity = indirectBuffer.SizeInBytes;

		while (capacity < required)
		{
			capacity = checked(capacity * 2);
		}

		indirectBuffer.ResizeDiscard(capacity);
	}

	private void ReleaseReferences()
	{
		if (Interlocked.Exchange(ref disposed, 1) != 0)
		{
			return;
		}

		ReleaseGroups(opaqueGroups);
		ReleaseGroups(cutoutGroups);
		textureReference?.Dispose();
	}

	private static VoxelPageDrawGroup[] CreateGroups(IReadOnlyList<VoxelPassEntry> entries)
	{
		ArgumentNullException.ThrowIfNull(entries);
		if (entries.Count == 0) return Array.Empty<VoxelPageDrawGroup>();
		using VoxelPageGrouping grouped = new(entries);
		VoxelPageDrawGroup[] groups = new VoxelPageDrawGroup[grouped.Count];
		int created = 0;
		try
		{
			for (int group = 0; group < grouped.Count; group++)
			{
				int count = grouped.Counts[group];
				var commands = ArrayPool<DrawArraysIndirectCommand>.Shared.Rent(count);
				var allocations = ArrayPool<VoxelGeometryAllocation>.Shared.Rent(count);
				int retained = 0;
				try
				{
					for (int index = grouped.First[group]; index >= 0; index = grouped.Next[index])
					{
						var allocation = entries[index].Allocation;
						allocation.Retain(); allocations[retained] = allocation;
						commands[retained++] = allocation.CreateDrawCommand();
					}
					groups[group] = new VoxelPageDrawGroup(grouped.Pages[group], commands, allocations, retained);
					created++;
				}
				catch
				{
					for (int i = 0; i < retained; i++) allocations[i].ReleaseRetained();
					ArrayPool<DrawArraysIndirectCommand>.Shared.Return(commands);
					ArrayPool<VoxelGeometryAllocation>.Shared.Return(allocations, clearArray: true);
					throw;
				}
			}
			return groups;
		}
		catch
		{
			for (int i = 0; i < created; i++) groups[i].Release();
			throw;
		}
	}

	private static void ReleaseGroups(VoxelPageDrawGroup[] groups)
	{
		if (groups == null)
		{
			return;
		}

		foreach (VoxelPageDrawGroup group in groups)
		{
			group.Release();
		}
	}

	private static int CountCommands(VoxelPageDrawGroup[] groups)
	{
		int count = 0;

		foreach (VoxelPageDrawGroup group in groups)
		{
			count += group.Count;
		}

		return count;
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
	}

	private readonly struct VoxelPageDrawGroup
	{
		internal VoxelPageDrawGroup(
			VoxelGeometryPage page,
			DrawArraysIndirectCommand[] commands,
			VoxelGeometryAllocation[] allocations,
			int count
		)
		{
			Page = page;
			Commands = commands;
			Allocations = allocations;
			Count = count;
		}

		internal VoxelGeometryPage Page { get; }

		internal DrawArraysIndirectCommand[] Commands { get; }

		internal VoxelGeometryAllocation[] Allocations { get; }

		internal int Count { get; }

		internal void Release()
		{
			if (Commands == null || Allocations == null)
			{
				return;
			}

			for (int index = 0; index < Count; index++)
			{
				Allocations[index].ReleaseRetained();
			}

			ArrayPool<DrawArraysIndirectCommand>.Shared.Return(Commands);
			ArrayPool<VoxelGeometryAllocation>.Shared.Return(Allocations, clearArray: true);
		}
	}
}
