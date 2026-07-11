using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using FishGfx.Graphics;

namespace FishGfx.Voxels
{
	public static class VoxelRenderBuckets
	{
		public static readonly RenderBucket Cutout = new RenderBucket("VoxelCutout");
	}

	public sealed class VoxelRendererOptions
	{
		public int MaxWorkers { get; set; } = Math.Max(1, Environment.ProcessorCount - 1);
		public int UploadBudget { get; set; } = 4;
		public float RenderDistance { get; set; } = 256;
		public float AlphaCutoff { get; set; } = 0.5f;
		public float AmbientLight { get; set; } = 0.35f;
		public Vector3 LightDirection { get; set; } = Vector3.Normalize(new Vector3(-0.45f, -1, -0.3f));
		public VoxelMeshingOptions Meshing { get; set; } = new VoxelMeshingOptions();
	}

	public readonly struct VoxelRendererStatistics
	{
		internal VoxelRendererStatistics(
			int loadedChunks,
			int gpuChunks,
			int visibleChunks,
			int pendingJobs,
			int acceptedMeshes,
			int discardedMeshes,
			int opaqueVertices,
			int cutoutVertices,
			int transparentFaces,
			int transparentVertices
		)
		{
			LoadedChunks = loadedChunks;
			GpuChunks = gpuChunks;
			VisibleChunks = visibleChunks;
			PendingJobs = pendingJobs;
			AcceptedMeshes = acceptedMeshes;
			DiscardedMeshes = discardedMeshes;
			OpaqueVertices = opaqueVertices;
			CutoutVertices = cutoutVertices;
			TransparentFaces = transparentFaces;
			TransparentVertices = transparentVertices;
		}

		public int LoadedChunks { get; }
		public int GpuChunks { get; }
		public int VisibleChunks { get; }
		public int PendingJobs { get; }
		public int AcceptedMeshes { get; }
		public int DiscardedMeshes { get; }
		public int OpaqueVertices { get; }
		public int CutoutVertices { get; }
		public int TransparentFaces { get; }
		public int TransparentVertices { get; }
	}

	public sealed class VoxelRenderer : IDisposable
	{
		private readonly VoxelWorld world;
		private readonly Texture atlasTexture;
		private readonly VoxelRendererOptions options;
		private readonly VoxelMeshingScheduler scheduler;
		private readonly Dictionary<ChunkCoordinate, GpuChunk> gpuChunks = new Dictionary<ChunkCoordinate, GpuChunk>();
		private readonly ConcurrentQueue<ChunkCoordinate> removedChunks = new ConcurrentQueue<ChunkCoordinate>();
		private readonly VoxelMesh transparentMesh;
		private readonly ShaderProgram voxelShader;
		private readonly ShaderStage vertexShader;
		private readonly ShaderStage fragmentShader;
		private GraphicsCommandBatch transparentBatch;
		private readonly RenderState opaqueState;
		private readonly RenderState transparentState;
		private bool disposed;
		private int acceptedMeshes;
		private int discardedMeshes;
		private int visibleChunks;
		private int visibleTransparentFaces;
		private int visibleTransparentVertices;
		private bool cullingEnabled = true;
		private VoxelFogSettings fog = VoxelFogSettings.Disabled;

		public VoxelRenderer(
			VoxelWorld world,
			VoxelPalette palette,
			Texture atlasTexture,
			VoxelAtlasLayout atlasLayout,
			VoxelRendererOptions options = null
		)
		{
			this.world = world ?? throw new ArgumentNullException(nameof(world));
			this.atlasTexture = atlasTexture ?? throw new ArgumentNullException(nameof(atlasTexture));
			this.options = options ?? new VoxelRendererOptions();

			ValidateOptions(this.options);

			if (atlasTexture.Width != atlasLayout.TextureWidth || atlasTexture.Height != atlasLayout.TextureHeight)
				throw new ArgumentException("Voxel atlas layout dimensions must match the supplied texture.", nameof(atlasLayout));

			scheduler = new VoxelMeshingScheduler(
				world,
				palette ?? throw new ArgumentNullException(nameof(palette)),
				atlasLayout,
				this.options.Meshing,
				this.options.MaxWorkers
			);
			world.ChunkRemoved += OnChunkRemoved;

			string shaderDirectory = Path.Combine(AppContext.BaseDirectory, "data", "shaders");
			vertexShader = new ShaderStage(ShaderType.VertexShader, Path.Combine(shaderDirectory, "voxel.vert"));
			fragmentShader = new ShaderStage(ShaderType.FragmentShader, Path.Combine(shaderDirectory, "voxel.frag"));
			voxelShader = new ShaderProgram(vertexShader, fragmentShader);
			transparentMesh = new VoxelMesh(BufferUsage.StreamDraw);

			opaqueState = CreateState(transparent: false);
			transparentState = CreateState(transparent: true);
			transparentBatch = CreateBatch(transparentMesh, transparentState, alphaCutoff: -1);
		}

		public bool IsIdle => scheduler.PendingCount == 0;

		public bool CullingEnabled
		{
			get => cullingEnabled;
			set
			{
				ThrowIfDisposed();
				cullingEnabled = value;
			}
		}

		public VoxelFogSettings Fog
		{
			get => fog;
			set
			{
				ThrowIfDisposed();

				if (fog == value)
					return;

				fog = value;
				RebuildCommandBatches();
			}
		}

		public VoxelRendererStatistics Statistics => new VoxelRendererStatistics(
			world.LoadedChunks.Count,
			gpuChunks.Count,
			visibleChunks,
			scheduler.PendingCount,
			acceptedMeshes,
			discardedMeshes,
			gpuChunks.Values.Sum(chunk => chunk.Opaque?.VertexCount ?? 0),
			gpuChunks.Values.Sum(chunk => chunk.Cutout?.VertexCount ?? 0),
			visibleTransparentFaces,
			visibleTransparentVertices
		);

		public int UpdateMeshing(int? uploadBudget = null)
		{
			ThrowIfDisposed();
			ProcessRemovedChunks();
			scheduler.SchedulePending();

			if (scheduler.TryDequeueFailure(out Exception failure))
				throw failure;

			int budget = uploadBudget ?? options.UploadBudget;

			if (budget < 0)
				throw new ArgumentOutOfRangeException(nameof(uploadBudget));

			int uploaded = 0;

			while (uploaded < budget && scheduler.TryDequeue(out VoxelMeshData result))
			{
				if (!world.TryGetChunk(result.Coordinate, out VoxelChunk chunk))
				{
					RemoveGpuChunk(result.Coordinate);
					discardedMeshes++;
					continue;
				}

				if (chunk.Revision != result.Revision)
				{
					discardedMeshes++;
					scheduler.MarkDirty(result.Coordinate);
					continue;
				}

				Upload(result);
				acceptedMeshes++;
				uploaded++;
			}

			scheduler.SchedulePending();

			if (scheduler.TryDequeueFailure(out failure))
				throw failure;

			return uploaded;
		}

		public void SubmitVisible(DeferredRenderQueue queue, Camera camera, float? renderDistance = null)
		{
			ThrowIfDisposed();

			if (queue == null)
				throw new ArgumentNullException(nameof(queue));
			if (camera == null)
				throw new ArgumentNullException(nameof(camera));

			float distance = renderDistance ?? options.RenderDistance;

			if (!float.IsFinite(distance) || distance <= 0)
				throw new ArgumentOutOfRangeException(nameof(renderDistance));

			ViewFrustum frustum = ViewFrustum.FromCamera(camera);
			float distanceSquared = distance * distance;
			List<VoxelTransparentFaceInstance> transparentFaces = new List<VoxelTransparentFaceInstance>();
			visibleChunks = 0;

			foreach (KeyValuePair<ChunkCoordinate, GpuChunk> pair in gpuChunks.OrderBy(pair => pair.Key.X).ThenBy(pair => pair.Key.Y).ThenBy(pair => pair.Key.Z))
			{
				GpuChunk chunk = pair.Value;

				if (chunk.Bounds.IsEmpty)
					continue;

				Vector3 origin = pair.Key.WorldOrigin;
				AABB worldBounds = chunk.Bounds + origin;
				Vector3 center = worldBounds.Center;

				if (cullingEnabled)
				{
					if (Vector3.DistanceSquared(camera.Position, center) > distanceSquared)
						continue;
					if (!frustum.Intersects(worldBounds))
						continue;
				}

				visibleChunks++;
				Matrix4x4 model = Matrix4x4.CreateTranslation(origin);

				if (chunk.OpaqueBatch != null && chunk.Opaque.VertexCount > 0)
					queue.SubmitOpaque(chunk.OpaqueBatch, model, center, sortKey: voxelShader.ID, tag: pair.Key);

				if (chunk.CutoutBatch != null && chunk.Cutout.VertexCount > 0)
					queue.Submit(VoxelRenderBuckets.Cutout, chunk.CutoutBatch, model, center, sortKey: voxelShader.ID, tag: pair.Key);

				for (int faceIndex = 0; faceIndex < chunk.TransparentFaces.Length; faceIndex++)
				{
					VoxelTransparentFace face = chunk.TransparentFaces[faceIndex];
					transparentFaces.Add(new VoxelTransparentFaceInstance(pair.Key, faceIndex, origin, face));
				}
			}

			BuildTransparentStream(camera, transparentFaces);

			if (transparentMesh.VertexCount > 0)
				queue.SubmitTransparent(transparentBatch, Matrix4x4.Identity, camera.Position, sortKey: voxelShader.ID, tag: this);
		}

		public void Dispose()
		{
			if (disposed)
				return;

			disposed = true;
			world.ChunkRemoved -= OnChunkRemoved;
			scheduler.Dispose();

			foreach (GpuChunk chunk in gpuChunks.Values)
				chunk.Dispose();

			gpuChunks.Clear();
			transparentMesh.Dispose();
			voxelShader.Dispose();
			vertexShader.Dispose();
			fragmentShader.Dispose();
		}

		private void Upload(VoxelMeshData result)
		{
			if (!gpuChunks.TryGetValue(result.Coordinate, out GpuChunk gpuChunk))
			{
				gpuChunk = new GpuChunk();
				gpuChunks.Add(result.Coordinate, gpuChunk);
			}

			gpuChunk.Revision = result.Revision;
			gpuChunk.Bounds = result.Bounds;
			gpuChunk.TransparentFaces = result.TransparentFaces;

			UpdateMesh(ref gpuChunk.Opaque, ref gpuChunk.OpaqueBatch, result.OpaqueVertices, opaqueState, -1);
			UpdateMesh(
				ref gpuChunk.Cutout,
				ref gpuChunk.CutoutBatch,
				result.CutoutVertices,
				opaqueState,
				options.AlphaCutoff
			);
		}

		private void UpdateMesh(
			ref VoxelMesh mesh,
			ref GraphicsCommandBatch batch,
			VoxelVertex[] vertices,
			RenderState state,
			float alphaCutoff
		)
		{
			if (mesh == null && vertices.Length > 0)
			{
				mesh = new VoxelMesh(BufferUsage.DynamicDraw);
				batch = CreateBatch(mesh, state, alphaCutoff);
			}

			mesh?.Update(vertices);
		}

		private GraphicsCommandBatch CreateBatch(VoxelMesh mesh, RenderState state, float alphaCutoff)
		{
			return new GraphicsCommandBatch(
				new GraphicsCommand[]
				{
					new PushRenderStateCommand(state),
					new DrawVoxelMeshCommand(
						mesh,
						atlasTexture,
						voxelShader,
						options.LightDirection,
						options.AmbientLight,
						alphaCutoff,
						fog
					),
					new PopRenderStateCommand(),
				}
			);
		}

		private void RebuildCommandBatches()
		{
			transparentBatch = CreateBatch(transparentMesh, transparentState, alphaCutoff: -1);

			foreach (GpuChunk chunk in gpuChunks.Values)
			{
				if (chunk.Opaque != null)
					chunk.OpaqueBatch = CreateBatch(chunk.Opaque, opaqueState, alphaCutoff: -1);

				if (chunk.Cutout != null)
					chunk.CutoutBatch = CreateBatch(chunk.Cutout, opaqueState, options.AlphaCutoff);
			}
		}

		private void BuildTransparentStream(Camera camera, List<VoxelTransparentFaceInstance> faces)
		{
			VoxelVertex[] vertices = VoxelTransparentStreamBuilder.Build(
				camera.Position,
				camera.WorldForwardNormal,
				faces
			);

			transparentMesh.Update(vertices);
			visibleTransparentFaces = faces.Count;
			visibleTransparentVertices = vertices.Length;
		}

		private void ProcessRemovedChunks()
		{
			while (removedChunks.TryDequeue(out ChunkCoordinate coordinate))
				RemoveGpuChunk(coordinate);
		}

		private void RemoveGpuChunk(ChunkCoordinate coordinate)
		{
			if (!gpuChunks.Remove(coordinate, out GpuChunk chunk))
				return;

			chunk.Dispose();
		}

		private void OnChunkRemoved(ChunkCoordinate coordinate)
		{
			removedChunks.Enqueue(coordinate);
		}

		private static RenderState CreateState(bool transparent)
		{
			RenderState state = Gfx.CreateDefaultRenderState();
			state.FrontFace = FrontFace.CounterClockwise;
			state.EnableDepthTest = true;
			state.EnableCullFace = true;
			state.EnableBlend = transparent;
			state.EnableDepthMask = !transparent;
			state.BlendFunc(BlendFactor.SrcAlpha, BlendFactor.OneMinusSrcAlpha);

			return state;
		}

		private static void ValidateOptions(VoxelRendererOptions options)
		{
			if (options.MaxWorkers <= 0)
				throw new ArgumentOutOfRangeException(nameof(options.MaxWorkers));
			if (options.UploadBudget < 0)
				throw new ArgumentOutOfRangeException(nameof(options.UploadBudget));
			if (!float.IsFinite(options.RenderDistance) || options.RenderDistance <= 0)
				throw new ArgumentOutOfRangeException(nameof(options.RenderDistance));
			if (!float.IsFinite(options.AlphaCutoff) || options.AlphaCutoff < 0 || options.AlphaCutoff > 1)
				throw new ArgumentOutOfRangeException(nameof(options.AlphaCutoff));
			if (!float.IsFinite(options.AmbientLight) || options.AmbientLight < 0 || options.AmbientLight > 1)
				throw new ArgumentOutOfRangeException(nameof(options.AmbientLight));
			if (!IsFinite(options.LightDirection) || options.LightDirection.LengthSquared() <= 0)
				throw new ArgumentOutOfRangeException(nameof(options.LightDirection));
			if (options.Meshing == null)
				throw new ArgumentNullException(nameof(options.Meshing));
		}

		private static bool IsFinite(Vector3 value)
		{
			return float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
		}

		private void ThrowIfDisposed()
		{
			if (disposed)
				throw new ObjectDisposedException(nameof(VoxelRenderer));
		}

		private sealed class GpuChunk : IDisposable
		{
			public long Revision;
			public AABB Bounds;
			public VoxelMesh Opaque;
			public VoxelMesh Cutout;
			public GraphicsCommandBatch OpaqueBatch;
			public GraphicsCommandBatch CutoutBatch;
			public VoxelTransparentFace[] TransparentFaces = Array.Empty<VoxelTransparentFace>();

			public void Dispose()
			{
				Opaque?.Dispose();
				Cutout?.Dispose();
			}
		}

	}
}
