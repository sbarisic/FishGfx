using System;
using System.Buffers;
using System.Collections.Generic;
using FishGfx.Graphics;

namespace FishGfx.Voxels;

internal sealed class DrawVoxelShadowPagesCommand : IDisposable
{
	private readonly VoxelSurfaceTextureSet textures;
	private readonly ShaderProgram opaqueShader;
	private readonly ShaderProgram alphaShader;
	private readonly GraphicsBuffer indirectBuffer;
	private readonly float cutoutAlphaCutoff;
	private readonly ShadowGroup[] opaqueGroups;
	private readonly ShadowGroup[] cutoutGroups;
	private readonly ShadowGroup[] alphaGroups;
	private readonly RenderState opaqueState;
	private readonly RenderState alphaState;
	private bool disposed;

	internal DrawVoxelShadowPagesCommand(
		VoxelSurfaceTextureSet textures,
		ShaderProgram opaqueShader,
		ShaderProgram alphaShader,
		GraphicsBuffer indirectBuffer,
		float cutoutAlphaCutoff,
		RenderState baseState,
		IReadOnlyList<VoxelPassEntry> opaqueEntries,
		IReadOnlyList<VoxelPassEntry> cutoutEntries,
		IReadOnlyList<VoxelPassEntry> alphaEntries)
	{
		this.textures = textures ?? throw new ArgumentNullException(nameof(textures));
		this.opaqueShader = opaqueShader ?? throw new ArgumentNullException(nameof(opaqueShader));
		this.alphaShader = alphaShader ?? throw new ArgumentNullException(nameof(alphaShader));
		this.indirectBuffer = indirectBuffer ?? throw new ArgumentNullException(nameof(indirectBuffer));
		this.cutoutAlphaCutoff = cutoutAlphaCutoff;
		opaqueState = baseState with { CullMode = CullMode.Back };
		alphaState = CreateAlphaTestState(baseState);
		opaqueGroups = CreateGroups(opaqueEntries);

		try
		{
			cutoutGroups = CreateGroups(cutoutEntries);
			alphaGroups = CreateGroups(alphaEntries);
		}
		catch
		{
			ReleaseGroups(opaqueGroups);
			ReleaseGroups(cutoutGroups);
			throw;
		}
	}

	internal int DriverDrawCount => opaqueGroups.Length + cutoutGroups.Length + alphaGroups.Length;

	internal int LogicalCommandCount => CountCommands(opaqueGroups)
		+ CountCommands(cutoutGroups)
		+ CountCommands(alphaGroups);

	internal static RenderState CreateAlphaTestState(RenderState baseState)
	{
		return baseState with { CullMode = CullMode.Back };
	}

	public void Execute(RenderPass pass)
	{
		ObjectDisposedException.ThrowIf(disposed, this);
		ArgumentNullException.ThrowIfNull(pass);

		if (opaqueGroups.Length > 0)
		{
			using IDisposable stateScope = pass.PushState(opaqueState);
			opaqueShader.Bind(pass.Uniforms);

			try
			{
				DrawGroups(opaqueGroups);
			}
			finally
			{
				opaqueShader.Unbind();
			}
		}

		if (cutoutGroups.Length == 0 && alphaGroups.Length == 0)
		{
			return;
		}

		using IDisposable alphaStateScope = pass.PushState(alphaState);
		alphaShader.SetUniform("AlphaCutoff", cutoutAlphaCutoff);
		alphaShader.SetUniform("CubeBaseColor", 0);
		alphaShader.SetUniform("ModelAtlas", 2);
		alphaShader.Bind(pass.Uniforms);
		IDisposable cubeBinding = null;
		IDisposable modelBinding = null;

		try
		{
			cubeBinding = textures.CubeBaseColor.Bind(0);
			modelBinding = textures.ModelAtlas.Bind(2);

			if (cutoutGroups.Length > 0)
			{
				alphaShader.SetUniform("UseVertexAlphaCutoff", 0);
				DrawGroups(cutoutGroups);
			}

			if (alphaGroups.Length > 0)
			{
				alphaShader.SetUniform("UseVertexAlphaCutoff", 1);
				DrawGroups(alphaGroups);
			}
		}
		finally
		{
			modelBinding?.Dispose();
			cubeBinding?.Dispose();
			alphaShader.Unbind();
		}
	}

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}

		disposed = true;
		ReleaseGroups(opaqueGroups);
		ReleaseGroups(cutoutGroups);
		ReleaseGroups(alphaGroups);
	}

	private void DrawGroups(ShadowGroup[] groups)
	{
		for (int index = 0; index < groups.Length; index++)
		{
			ShadowGroup group = groups[index];
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

	private static ShadowGroup[] CreateGroups(IReadOnlyList<VoxelPassEntry> entries)
	{
		ArgumentNullException.ThrowIfNull(entries);
		if (entries.Count == 0) return Array.Empty<ShadowGroup>();
		using VoxelPageGrouping grouped = new(entries);
		ShadowGroup[] groups = new ShadowGroup[grouped.Count];
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
					groups[group] = new ShadowGroup(grouped.Pages[group], commands, allocations, retained);
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
			ReleaseGroups(groups.AsSpan(0, created));
			throw;
		}
	}

	private static int CountCommands(ShadowGroup[] groups)
	{
		int count = 0;

		for (int index = 0; index < groups.Length; index++)
		{
			count += groups[index].Count;
		}

		return count;
	}

	private static void ReleaseGroups(ShadowGroup[] groups)
	{
		ReleaseGroups(groups.AsSpan());
	}

	private static void ReleaseGroups(ReadOnlySpan<ShadowGroup> groups)
	{
		for (int groupIndex = 0; groupIndex < groups.Length; groupIndex++)
		{
			ShadowGroup group = groups[groupIndex];

			for (int index = 0; index < group.Count; index++)
			{
				group.Allocations[index].ReleaseRetained();
			}

			ArrayPool<DrawArraysIndirectCommand>.Shared.Return(group.Commands);
			ArrayPool<VoxelGeometryAllocation>.Shared.Return(group.Allocations, clearArray: true);
		}
	}

	private readonly record struct ShadowGroup(
		VoxelGeometryPage Page,
		DrawArraysIndirectCommand[] Commands,
		VoxelGeometryAllocation[] Allocations,
		int Count);
}
