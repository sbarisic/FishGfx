using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading;
using FishGfx.Graphics;

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
	private readonly Texture atlas;
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
	private int disposed;

	internal DrawVoxelPagesCommand(
		Texture atlas,
		ShaderProgram shader,
		RenderState state,
		VoxelSunSettings sunSettings,
		float cutoutAlphaCutoff,
		VoxelFogSettings fogSettings,
		GraphicsBuffer indirectBuffer,
		VoxelGpuTimer gpuTimer,
		IReadOnlyList<VoxelPassEntry> opaqueEntries,
		IReadOnlyList<VoxelPassEntry> cutoutEntries,
		VoxelRenderer renderer
	)
	{
		this.atlas = atlas ?? throw new ArgumentNullException(nameof(atlas));
		this.shader = shader ?? throw new ArgumentNullException(nameof(shader));
		this.state = state;
		sunSettings.Validate(nameof(sunSettings));
		sun = sunSettings;
		fog = fogSettings;
		this.cutoutAlphaCutoff = cutoutAlphaCutoff;
		this.indirectBuffer = indirectBuffer ?? throw new ArgumentNullException(nameof(indirectBuffer));
		this.gpuTimer = gpuTimer ?? throw new ArgumentNullException(nameof(gpuTimer));
		this.renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
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
	}

	internal int OpaqueGroupCount => opaqueGroups.Length;

	internal int CutoutGroupCount => cutoutGroups.Length;

	internal int OpaqueCommandCount => CountCommands(opaqueGroups);

	internal int CutoutCommandCount => CountCommands(cutoutGroups);

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
		bool textureBound = false;
		using IDisposable stateScope = pass.PushState(state);
		int queryIndex = gpuTimer.Begin(pass, out IDisposable queryScope);

		try
		{
			shader.SetUniform("LightDirection", sun.Direction);
			shader.SetUniform("AmbientLight", sun.AmbientLight);
			shader.SetUniform("SunColor", (Vector3)sun.Color);
			shader.SetUniform("SunIntensity", sun.Intensity);
			shader.SetUniform("FogEnabled", fog.Enabled ? 1 : 0);
			shader.SetUniform("FogColor", (Vector3)fog.Color);
			shader.SetUniform("FogDensity", fog.Density);
			shader.SetUniform("LightMultiplier", fog.Enabled ? fog.LightMultiplier : 1);
			shader.SetUniform("AlphaCutoff", -1f);
			shader.Bind(pass.Uniforms);
			shaderBound = true;
			atlas.BindTextureUnit();
			textureBound = true;

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
				if (textureBound)
				{
					atlas.UnbindTextureUnit();
				}

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
	}

	private static VoxelPageDrawGroup[] CreateGroups(IReadOnlyList<VoxelPassEntry> entries)
	{
		ArgumentNullException.ThrowIfNull(entries);

		if (entries.Count == 0)
		{
			return Array.Empty<VoxelPageDrawGroup>();
		}

		VoxelGeometryPage[] pages = ArrayPool<VoxelGeometryPage>.Shared.Rent(entries.Count);
		int[] counts = ArrayPool<int>.Shared.Rent(entries.Count);
		int pageCount = 0;

		try
		{
			for (int index = 0; index < entries.Count; index++)
			{
				VoxelGeometryPage page = entries[index].Allocation.Page;
				int groupIndex = FindPage(pages, pageCount, page);

				if (groupIndex < 0)
				{
					groupIndex = pageCount++;
					pages[groupIndex] = page;
					counts[groupIndex] = 0;
				}

				counts[groupIndex]++;
			}

			VoxelPageDrawGroup[] groups = new VoxelPageDrawGroup[pageCount];
			int created = 0;

			try
			{
				for (int groupIndex = 0; groupIndex < pageCount; groupIndex++)
				{
					int count = counts[groupIndex];
					DrawArraysIndirectCommand[] commands = ArrayPool<DrawArraysIndirectCommand>.Shared.Rent(count);
					VoxelGeometryAllocation[] allocations = ArrayPool<VoxelGeometryAllocation>.Shared.Rent(count);
					int retained = 0;

					try
					{
						for (int index = 0; index < entries.Count; index++)
						{
							VoxelGeometryAllocation allocation = entries[index].Allocation;

							if (!ReferenceEquals(allocation.Page, pages[groupIndex]))
							{
								continue;
							}

							allocation.Retain();
							allocations[retained] = allocation;
							commands[retained] = allocation.CreateDrawCommand();
							retained++;
						}
					}
					catch
					{
						for (int index = 0; index < retained; index++)
						{
							allocations[index].ReleaseRetained();
						}

						ArrayPool<DrawArraysIndirectCommand>.Shared.Return(commands);
						ArrayPool<VoxelGeometryAllocation>.Shared.Return(allocations, clearArray: true);
						throw;
					}

					groups[groupIndex] = new VoxelPageDrawGroup(
						pages[groupIndex],
						commands,
						allocations,
						retained
					);
					created++;
				}

				return groups;
			}
			catch
			{
				for (int index = 0; index < created; index++)
				{
					groups[index].Release();
				}

				throw;
			}
		}
		finally
		{
			ArrayPool<VoxelGeometryPage>.Shared.Return(pages, clearArray: true);
			ArrayPool<int>.Shared.Return(counts);
		}
	}

	private static int FindPage(
		VoxelGeometryPage[] pages,
		int pageCount,
		VoxelGeometryPage page
	)
	{
		for (int index = 0; index < pageCount; index++)
		{
			if (ReferenceEquals(pages[index], page))
			{
				return index;
			}
		}

		return -1;
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
