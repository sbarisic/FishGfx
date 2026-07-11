using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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

	public readonly struct VoxelRendererFrameDiagnostics
	{
		internal VoxelRendererFrameDiagnostics(
			double cullingMilliseconds,
			double transparentBuildMilliseconds,
			int opaqueDrawCalls,
			int cutoutDrawCalls,
			int transparentDrawCalls,
			int shaderBinds,
			int textureBinds,
			int passSubmissions,
			int transparentUploadBytes,
			bool transparentCacheHit
		)
		{
			CullingMilliseconds = cullingMilliseconds;
			TransparentBuildMilliseconds = transparentBuildMilliseconds;
			OpaqueDrawCalls = opaqueDrawCalls;
			CutoutDrawCalls = cutoutDrawCalls;
			TransparentDrawCalls = transparentDrawCalls;
			ShaderBinds = shaderBinds;
			TextureBinds = textureBinds;
			PassSubmissions = passSubmissions;
			TransparentUploadBytes = transparentUploadBytes;
			TransparentCacheHit = transparentCacheHit;
		}

		public double CullingMilliseconds { get; }
		public double TransparentBuildMilliseconds { get; }
		public int OpaqueDrawCalls { get; }
		public int CutoutDrawCalls { get; }
		public int TransparentDrawCalls { get; }
		public int DrawCalls => OpaqueDrawCalls + CutoutDrawCalls + TransparentDrawCalls;
		public int ShaderBinds { get; }
		public int TextureBinds { get; }
		public int PassSubmissions { get; }
		public int TransparentUploadBytes { get; }
		public bool TransparentCacheHit { get; }
	}

	internal readonly struct VoxelTransparentCacheKey : IEquatable<VoxelTransparentCacheKey>
	{
		internal VoxelTransparentCacheKey(long geometryRevision, ulong visibleSignature, Matrix4x4 view)
		{
			GeometryRevision = geometryRevision;
			VisibleSignature = visibleSignature;
			View = view;
		}

		internal long GeometryRevision { get; }
		internal ulong VisibleSignature { get; }
		internal Matrix4x4 View { get; }

		public bool Equals(VoxelTransparentCacheKey other)
		{
			return GeometryRevision == other.GeometryRevision
				&& VisibleSignature == other.VisibleSignature
				&& View == other.View;
		}

		public override bool Equals(object obj) => obj is VoxelTransparentCacheKey other && Equals(other);
		public override int GetHashCode() => HashCode.Combine(GeometryRevision, VisibleSignature, View);
	}

	public sealed class VoxelRenderer : IDisposable
	{
		private readonly VoxelWorld world;
		private readonly Texture atlasTexture;
		private readonly VoxelRendererOptions options;
		private readonly VoxelMeshingScheduler scheduler;
		private readonly Dictionary<ChunkCoordinate, GpuChunk> gpuChunks = new Dictionary<ChunkCoordinate, GpuChunk>();
		private readonly List<GpuChunk> orderedGpuChunks = new List<GpuChunk>();
		private readonly List<VoxelPassEntry> visibleOpaque = new List<VoxelPassEntry>();
		private readonly List<VoxelPassEntry> visibleCutout = new List<VoxelPassEntry>();
		private readonly List<GpuChunk> visibleTransparentChunks = new List<GpuChunk>();
		private readonly List<VoxelTransparentFaceInstance> transparentFaces =
			new List<VoxelTransparentFaceInstance>();
		private readonly ConcurrentQueue<ChunkCoordinate> removedChunks = new ConcurrentQueue<ChunkCoordinate>();
		private readonly VoxelMesh transparentMesh;
		private readonly ShaderProgram voxelShader;
		private readonly ShaderStage vertexShader;
		private readonly ShaderStage fragmentShader;
		private GraphicsCommandBatch transparentBatch;
		private readonly DrawVoxelPassCommand opaquePassCommand;
		private readonly DrawVoxelPassCommand cutoutPassCommand;
		private readonly GraphicsCommandBatch opaquePassBatch;
		private readonly GraphicsCommandBatch cutoutPassBatch;
		private readonly RenderState opaqueState;
		private readonly RenderState transparentState;
		private bool disposed;
		private int acceptedMeshes;
		private int discardedMeshes;
		private int visibleChunks;
		private int visibleTransparentFaces;
		private int visibleTransparentVertices;
		private int opaqueVertices;
		private int cutoutVertices;
		private long transparentGeometryRevision;
		private VoxelTransparentCacheKey transparentCacheKey;
		private bool hasTransparentCache;
		private VoxelVertex[] transparentVertexBuffer = Array.Empty<VoxelVertex>();
		private VoxelRendererFrameDiagnostics frameDiagnostics;
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
			opaquePassCommand = new DrawVoxelPassCommand(
				atlasTexture,
				voxelShader,
				opaqueState,
				this.options.LightDirection,
				this.options.AmbientLight,
				alphaCutoff: -1
			);
			cutoutPassCommand = new DrawVoxelPassCommand(
				atlasTexture,
				voxelShader,
				opaqueState,
				this.options.LightDirection,
				this.options.AmbientLight,
				this.options.AlphaCutoff
			);
			opaquePassBatch = new GraphicsCommandBatch(new GraphicsCommand[] { opaquePassCommand });
			cutoutPassBatch = new GraphicsCommandBatch(new GraphicsCommand[] { cutoutPassCommand });
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
				opaquePassCommand.Fog = value;
				cutoutPassCommand.Fog = value;
				transparentBatch = CreateBatch(transparentMesh, transparentState, alphaCutoff: -1);
			}
		}

		public VoxelRendererFrameDiagnostics FrameDiagnostics => frameDiagnostics;

		public VoxelRendererStatistics Statistics => new VoxelRendererStatistics(
			world.LoadedChunkCount,
			gpuChunks.Count,
			visibleChunks,
			scheduler.PendingCount,
			acceptedMeshes,
			discardedMeshes,
			opaqueVertices,
			cutoutVertices,
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

			long cullingStart = Stopwatch.GetTimestamp();
			ViewFrustum frustum = ViewFrustum.FromCamera(camera);
			float distanceSquared = distance * distance;
			Vector3 cameraForward = camera.WorldForwardNormal;
			visibleOpaque.Clear();
			visibleCutout.Clear();
			visibleTransparentChunks.Clear();
			visibleChunks = 0;
			ulong transparentSignature = 14695981039346656037UL;

			for (int chunkIndex = 0; chunkIndex < orderedGpuChunks.Count; chunkIndex++)
			{
				GpuChunk chunk = orderedGpuChunks[chunkIndex];

				if (chunk.Bounds.IsEmpty)
					continue;

				Vector3 origin = chunk.Coordinate.WorldOrigin;
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
				float depth = Vector3.Dot(center - camera.Position, cameraForward);

				if (chunk.Opaque?.VertexCount > 0)
					visibleOpaque.Add(new VoxelPassEntry(chunk.Opaque, model, chunk.Coordinate, depth));

				if (chunk.Cutout?.VertexCount > 0)
					visibleCutout.Add(new VoxelPassEntry(chunk.Cutout, model, chunk.Coordinate, depth));

				if (chunk.TransparentFaces.Length > 0)
				{
					visibleTransparentChunks.Add(chunk);
					transparentSignature = AddSignature(transparentSignature, chunk.Coordinate);
					transparentSignature = AddSignature(transparentSignature, chunk.Revision);
				}
			}

			visibleOpaque.Sort(ComparePassEntries);
			visibleCutout.Sort(ComparePassEntries);
			opaquePassCommand.Update(visibleOpaque);
			cutoutPassCommand.Update(visibleCutout);

			int passSubmissions = 0;

			if (visibleOpaque.Count > 0)
			{
				queue.SubmitOpaque(
					opaquePassBatch,
					Matrix4x4.Identity,
					camera.Position,
					sortKey: voxelShader.ID,
					tag: this
				);
				passSubmissions++;
			}

			if (visibleCutout.Count > 0)
			{
				queue.Submit(
					VoxelRenderBuckets.Cutout,
					cutoutPassBatch,
					Matrix4x4.Identity,
					camera.Position,
					sortKey: voxelShader.ID,
					tag: this
				);
				passSubmissions++;
			}

			double cullingMilliseconds = Stopwatch.GetElapsedTime(cullingStart).TotalMilliseconds;
			VoxelTransparentCacheKey currentCacheKey = new VoxelTransparentCacheKey(
				transparentGeometryRevision,
				transparentSignature,
				camera.View
			);
			bool transparentCacheHit = hasTransparentCache && transparentCacheKey.Equals(currentCacheKey);
			double transparentBuildMilliseconds = 0;
			int transparentUploadBytes = 0;

			if (!transparentCacheHit)
			{
				long transparentStart = Stopwatch.GetTimestamp();
				transparentUploadBytes = BuildTransparentStream(camera);
				transparentBuildMilliseconds = Stopwatch.GetElapsedTime(transparentStart).TotalMilliseconds;
				transparentCacheKey = currentCacheKey;
				hasTransparentCache = true;
			}

			if (transparentMesh.VertexCount > 0)
			{
				queue.SubmitTransparent(transparentBatch, Matrix4x4.Identity, camera.Position, sortKey: voxelShader.ID, tag: this);
				passSubmissions++;
			}

			int transparentDrawCalls = transparentMesh.VertexCount > 0 ? 1 : 0;
			frameDiagnostics = new VoxelRendererFrameDiagnostics(
				cullingMilliseconds,
				transparentBuildMilliseconds,
				visibleOpaque.Count,
				visibleCutout.Count,
				transparentDrawCalls,
				passSubmissions,
				passSubmissions,
				passSubmissions,
				transparentUploadBytes,
				transparentCacheHit
			);
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
			orderedGpuChunks.Clear();
			transparentMesh.Dispose();
			voxelShader.Dispose();
			vertexShader.Dispose();
			fragmentShader.Dispose();
		}

		private void Upload(VoxelMeshData result)
		{
			if (
				result.OpaqueVertices.Length == 0
				&& result.CutoutVertices.Length == 0
				&& result.TransparentFaces.Length == 0
			)
			{
				RemoveGpuChunk(result.Coordinate);
				return;
			}

			if (!gpuChunks.TryGetValue(result.Coordinate, out GpuChunk gpuChunk))
			{
				gpuChunk = new GpuChunk(result.Coordinate);
				gpuChunks.Add(result.Coordinate, gpuChunk);
				InsertOrderedGpuChunk(gpuChunk);
			}

			opaqueVertices -= gpuChunk.Opaque?.VertexCount ?? 0;
			cutoutVertices -= gpuChunk.Cutout?.VertexCount ?? 0;

			gpuChunk.Revision = result.Revision;
			gpuChunk.Bounds = result.Bounds;
			gpuChunk.TransparentFaces = result.TransparentFaces;

			UpdateMesh(ref gpuChunk.Opaque, result.OpaqueVertices);
			UpdateMesh(ref gpuChunk.Cutout, result.CutoutVertices);
			opaqueVertices += gpuChunk.Opaque?.VertexCount ?? 0;
			cutoutVertices += gpuChunk.Cutout?.VertexCount ?? 0;
			transparentGeometryRevision++;
		}

		private static void UpdateMesh(ref VoxelMesh mesh, VoxelVertex[] vertices)
		{
			if (mesh == null && vertices.Length > 0)
				mesh = new VoxelMesh(BufferUsage.DynamicDraw);

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

		private int BuildTransparentStream(Camera camera)
		{
			transparentFaces.Clear();
			Vector3 cameraForward = camera.WorldForwardNormal;

			for (int chunkIndex = 0; chunkIndex < visibleTransparentChunks.Count; chunkIndex++)
			{
				GpuChunk chunk = visibleTransparentChunks[chunkIndex];
				Vector3 origin = chunk.Coordinate.WorldOrigin;

				for (int faceIndex = 0; faceIndex < chunk.TransparentFaces.Length; faceIndex++)
				{
					VoxelTransparentFace face = chunk.TransparentFaces[faceIndex];
					float depth = Vector3.Dot(face.Center + origin - camera.Position, cameraForward);
					transparentFaces.Add(
						new VoxelTransparentFaceInstance(
							chunk.Coordinate,
							faceIndex,
							origin,
							face,
							depth
						)
					);
				}
			}

			int required = VoxelTransparentStreamBuilder.CountVertices(transparentFaces);

			if (transparentVertexBuffer.Length < required)
				Array.Resize(
					ref transparentVertexBuffer,
					VoxelMesh.CalculateCapacity(transparentVertexBuffer.Length, required)
				);

			int vertexCount = VoxelTransparentStreamBuilder.BuildSorted(
				transparentFaces,
				transparentVertexBuffer
			);
			transparentMesh.Update(transparentVertexBuffer, vertexCount);
			visibleTransparentFaces = transparentFaces.Count;
			visibleTransparentVertices = vertexCount;

			return checked(vertexCount * System.Runtime.InteropServices.Marshal.SizeOf<VoxelVertex>());
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

			opaqueVertices -= chunk.Opaque?.VertexCount ?? 0;
			cutoutVertices -= chunk.Cutout?.VertexCount ?? 0;
			orderedGpuChunks.Remove(chunk);
			transparentGeometryRevision++;
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

		private static int CompareGpuChunks(GpuChunk left, GpuChunk right)
		{
			return CompareCoordinates(left.Coordinate, right.Coordinate);
		}

		private void InsertOrderedGpuChunk(GpuChunk chunk)
		{
			int index = orderedGpuChunks.BinarySearch(chunk, GpuChunkComparer.Instance);

			if (index < 0)
				index = ~index;

			orderedGpuChunks.Insert(index, chunk);
		}

		internal static int ComparePassEntries(VoxelPassEntry left, VoxelPassEntry right)
		{
			int result = left.Depth.CompareTo(right.Depth);

			return result != 0 ? result : CompareCoordinates(left.Coordinate, right.Coordinate);
		}

		private static int CompareCoordinates(ChunkCoordinate left, ChunkCoordinate right)
		{
			int result = left.X.CompareTo(right.X);

			if (result == 0)
				result = left.Y.CompareTo(right.Y);
			if (result == 0)
				result = left.Z.CompareTo(right.Z);

			return result;
		}

		private static ulong AddSignature(ulong signature, ChunkCoordinate coordinate)
		{
			signature = AddSignature(signature, coordinate.X);
			signature = AddSignature(signature, coordinate.Y);
			return AddSignature(signature, coordinate.Z);
		}

		private static ulong AddSignature(ulong signature, long value)
		{
			unchecked
			{
				signature ^= (ulong)value;
				return signature * 1099511628211UL;
			}
		}

		private void ThrowIfDisposed()
		{
			if (disposed)
				throw new ObjectDisposedException(nameof(VoxelRenderer));
		}

		private sealed class GpuChunk : IDisposable
		{
			internal GpuChunk(ChunkCoordinate coordinate)
			{
				Coordinate = coordinate;
			}

			public ChunkCoordinate Coordinate { get; }
			public long Revision;
			public AABB Bounds;
			public VoxelMesh Opaque;
			public VoxelMesh Cutout;
			public VoxelTransparentFace[] TransparentFaces = Array.Empty<VoxelTransparentFace>();

			public void Dispose()
			{
				Opaque?.Dispose();
				Cutout?.Dispose();
			}
		}

		private sealed class GpuChunkComparer : IComparer<GpuChunk>
		{
			internal static readonly GpuChunkComparer Instance = new GpuChunkComparer();

			public int Compare(GpuChunk left, GpuChunk right) => CompareGpuChunks(left, right);
		}

	}
}
