using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using FishGfx.Graphics;
using FishGfx.Graphics.Shadows;

namespace FishGfx.Voxels;

public sealed partial class VoxelRenderer : IDisposable
{
	private readonly VoxelWorld world;
	private readonly VoxelLighting lighting;
	private Texture atlasTexture;
	private VoxelSurfaceTextureSet surfaceTextures;
	private readonly VoxelAtlasLayout atlasLayout;
	private readonly VoxelRendererOptions options;
	private readonly VoxelMeshingScheduler scheduler;
	private readonly Dictionary<ChunkCoordinate, GpuChunk> gpuChunks = new Dictionary<ChunkCoordinate, GpuChunk>();
	private readonly Dictionary<ChunkCoordinate, CompletedEmptyChunk> completedEmptyChunks =
		new Dictionary<ChunkCoordinate, CompletedEmptyChunk>();
	private readonly List<GpuChunk> orderedGpuChunks = new List<GpuChunk>();
	private readonly List<GpuChunk> activeGpuChunks = new List<GpuChunk>();
	private readonly HashSet<ChunkCoordinate> activeCoordinates = new HashSet<ChunkCoordinate>();
	private readonly List<VoxelPassEntry> visibleOpaque = new List<VoxelPassEntry>();
	private readonly List<VoxelPassEntry> visibleCutout = new List<VoxelPassEntry>();
	private readonly List<VoxelPassEntry> shadowOpaque = new List<VoxelPassEntry>();
	private readonly List<VoxelPassEntry> shadowCutout = new List<VoxelPassEntry>();
	private readonly List<VoxelPassEntry> shadowAlpha = new List<VoxelPassEntry>();
	private readonly List<VoxelMeshData> pendingUploads = new List<VoxelMeshData>();
	private readonly ConcurrentQueue<ChunkCoordinate> removedChunks = new ConcurrentQueue<ChunkCoordinate>();
	private readonly ShaderProgram voxelShader;
	private readonly ShaderProgram waveShader;
	private readonly ShaderProgram shadowOpaqueShader;
	private readonly ShaderProgram shadowAlphaShader;
	private readonly ShaderStage vertexShader;
	private readonly ShaderStage waveVertexShader;
	private readonly ShaderStage fragmentShader;
	private readonly ShaderStage shadowVertexShader;
	private readonly ShaderStage shadowOpaqueFragmentShader;
	private readonly ShaderStage shadowAlphaFragmentShader;
	private readonly RenderState opaqueState;
	private readonly RenderState transparentState;
	private readonly VoxelGeometryPagePool opaqueGeometry;
	private readonly VoxelGeometryPagePool cutoutGeometry;
	private readonly VoxelGeometryPagePool alphaShadowGeometry;
	private readonly VoxelTransparentGeometryStore transparentGeometry;
	private readonly VoxelTransparentOrderingScheduler transparentOrdering;
	private readonly VoxelTransparentIndexRing transparentIndexRing;
	private readonly GraphicsBuffer indirectBuffer;
	private readonly VoxelGpuTimer gpuTimer;
	private readonly VoxelGpuTimer transparentGpuTimer;
	private bool disposed;
	private int acceptedMeshes;
	private int discardedMeshes;
	private int visibleChunks;
	private int visibleTransparentFaces;
	private int visibleTransparentVertices;
	private int opaqueVertices;
	private int cutoutVertices;
	private long transparentGeometryRevision;
	private long activeSetGeneration;
	private long transparentRequestSequence;
	private VoxelTransparentOrderingSource transparentSource;
	private VoxelTransparentDrawSnapshot transparentSnapshot;
	private VoxelTransparentCacheKey transparentRequestKey;
	private bool hasTransparentRequestKey;
	private bool transparentSourceDirty = true;
	private int transparentStaleResults;
	private double transparentSourceBuildMilliseconds;
	private double transparentSortMilliseconds;
	private double transparentResultApplyMilliseconds;
	private double transparentIndexUploadMilliseconds;
	private int transparentIndexUploadBytes;
	private int transparentMainThreadAllocatedBytes;
	private int transparentWorkerAllocatedBytes;
	private VoxelTransparentInvalidationReason transparentLastRequestReason;
	private VoxelRendererFrameDiagnostics frameDiagnostics;
	private double meshSchedulingMilliseconds;
	private double meshUploadMilliseconds;
	private int scheduledMeshes;
	private int uploadedMeshes;
	private int fastCompletedMeshes;
	private bool cullingEnabled = true;
	private VoxelFogSettings fog = VoxelFogSettings.Disabled;
	private VoxelSunSettings sun;
	private bool activeSetDirty = true;
	private bool hasActiveSetAnchor;
	private Vector3 activeSetAnchor;
	private float activeSetRenderDistance;
	private int candidateChunks;
	private int inactiveCachedChunks;
	private double lastSubmissionMilliseconds;
	private int lastSubmissionAllocatedBytes;

	public VoxelRenderer(
		GraphicsContext graphics,
		VoxelWorld world,
		VoxelPalette palette,
		Texture atlas,
		VoxelAtlasLayout atlasLayout,
		VoxelLighting lighting,
		VoxelRendererOptions options = null
	)
	{
		Graphics = graphics ?? throw new ArgumentNullException(nameof(graphics));
		this.world = world ?? throw new ArgumentNullException(nameof(world));
		this.lighting = lighting ?? throw new ArgumentNullException(nameof(lighting));
		atlasTexture = atlas ?? throw new ArgumentNullException(nameof(atlas));
		surfaceTextures = new VoxelSurfaceTextureSet(atlasTexture);
		this.atlasLayout = atlasLayout;
		this.options = options ?? new VoxelRendererOptions();
		VoxelPalette resolvedPalette = palette ?? throw new ArgumentNullException(nameof(palette));

		ValidateOptions(this.options);

		if (!Graphics.Capabilities.SupportsMultiDrawIndirect
			|| !Graphics.Capabilities.SupportsVertexAttributeBinding)
		{
			throw new NotSupportedException(
				$"FishGfx voxel rendering requires OpenGL 4.3; current context is {Graphics.Capabilities.Version}."
			);
		}

		sun = this.options.Sun;
		atlasTexture.EnsureOwner(graphics);

		if (!lighting.IsCompatibleWith(world, resolvedPalette))
		{
			throw new ArgumentException(
				"Voxel lighting must belong to the supplied world and palette.",
				nameof(lighting)
			);
		}

		if (atlas.Width != atlasLayout.TextureWidth || atlas.Height != atlasLayout.TextureHeight)
		{
			throw new ArgumentException("Voxel atlas layout dimensions must match the supplied texture.", nameof(atlasLayout));
		}

		scheduler = new VoxelMeshingScheduler(
		world,
		resolvedPalette,
		atlasLayout,
		this.options.Meshing,
		this.options.WorkerCount,
		lighting,
		poolMeshVertexBuffers: true
	);
		world.ChunkRemoved += OnChunkRemoved;

		string shaderDirectory = Path.Combine(AppContext.BaseDirectory, "data", "shaders");
		vertexShader = graphics.LoadShaderStage(
			ShaderStageType.Vertex,
			Path.Combine(shaderDirectory, "voxel.vert")
		);
		waveVertexShader = graphics.LoadShaderStage(
			ShaderStageType.Vertex,
			Path.Combine(shaderDirectory, "voxel_wave.vert")
		);
		fragmentShader = graphics.LoadShaderStage(
			ShaderStageType.Fragment,
			Path.Combine(shaderDirectory, "voxel.frag")
		);
		shadowVertexShader = graphics.LoadShaderStage(
			ShaderStageType.Vertex,
			Path.Combine(shaderDirectory, "voxel_shadow.vert")
		);
		shadowOpaqueFragmentShader = graphics.LoadShaderStage(
			ShaderStageType.Fragment,
			Path.Combine(shaderDirectory, "voxel_shadow_opaque.frag")
		);
		shadowAlphaFragmentShader = graphics.LoadShaderStage(
			ShaderStageType.Fragment,
			Path.Combine(shaderDirectory, "voxel_shadow_alpha.frag")
		);
		voxelShader = graphics.CreateShaderProgram(vertexShader, fragmentShader);
		waveShader = graphics.CreateShaderProgram(waveVertexShader, fragmentShader);
		shadowOpaqueShader = graphics.CreateShaderProgram(
			shadowVertexShader,
			shadowOpaqueFragmentShader
		);
		shadowAlphaShader = graphics.CreateShaderProgram(
			shadowVertexShader,
			shadowAlphaFragmentShader
		);
		opaqueGeometry = new VoxelGeometryPagePool(graphics, this.options.GeometryPageSizeBytes);
		cutoutGeometry = new VoxelGeometryPagePool(graphics, this.options.GeometryPageSizeBytes);
		alphaShadowGeometry = new VoxelGeometryPagePool(graphics, this.options.GeometryPageSizeBytes);
		transparentGeometry = new VoxelTransparentGeometryStore(
			graphics,
			this.options.GeometryPageSizeBytes
		);
		transparentOrdering = new VoxelTransparentOrderingScheduler();
		transparentIndexRing = new VoxelTransparentIndexRing(graphics);
		indirectBuffer = graphics.CreateBuffer(new GraphicsBufferDescriptor(
			64 * 1024,
			BufferBindFlags.Indirect,
			BufferUsage.Stream
		));
		gpuTimer = new VoxelGpuTimer(graphics)
		{
			Enabled = this.options.GpuProfilingEnabled,
		};
		transparentGpuTimer = new VoxelGpuTimer(graphics)
		{
			Enabled = this.options.GpuProfilingEnabled,
		};

		opaqueState = CreateState(transparent: false);
		transparentState = CreateState(transparent: true);
	}

	public GraphicsContext Graphics { get; }

	public Texture AtlasTexture => atlasTexture;

	public VoxelSurfaceTextureSet SurfaceTextures => surfaceTextures;

	public void SetAtlasTexture(Texture atlas)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(atlas);
		atlas.EnsureOwner(Graphics);
		if (atlas.Width != atlasLayout.TextureWidth
			|| atlas.Height != atlasLayout.TextureHeight)
		{
			throw new ArgumentException(
				"Voxel atlas layout dimensions must match the supplied texture.",
				nameof(atlas)
			);
		}

		atlasTexture = atlas;
		surfaceTextures = surfaceTextures.WithBaseColor(atlas);
	}

	public void SetSurfaceTextures(VoxelSurfaceTextureSet textures)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(textures);
		textures.EnsureOwner(Graphics);

		if (textures.BaseColor.Width != atlasLayout.TextureWidth
			|| textures.BaseColor.Height != atlasLayout.TextureHeight)
		{
			throw new ArgumentException(
				"Voxel atlas layout dimensions must match the supplied texture set.",
				nameof(textures)
			);
		}

		surfaceTextures = textures;
		atlasTexture = textures.BaseColor;
	}

	public bool IsIdle => scheduler.PendingCount == 0
		&& pendingUploads.Count == 0
		&& transparentOrdering.IsIdle;

	public bool IsCullingEnabled
	{
		get => cullingEnabled;
		set
		{
			ThrowIfDisposed();

			if (cullingEnabled != value)
			{
				activeSetDirty = true;
			}

			cullingEnabled = value;
		}
	}

	public VoxelSunSettings SunSettings
	{
		get => sun;
		set
		{
			ThrowIfDisposed();
			value.Validate(nameof(value));

			if (sun == value)
			{
				return;
			}

			sun = value;
		}
	}

	public VoxelFogSettings FogSettings
	{
		get => fog;
		set
		{
			ThrowIfDisposed();

			if (fog == value)
			{
				return;
			}

			fog = value;
		}
	}

	public VoxelRendererFrameDiagnostics FrameDiagnostics => frameDiagnostics;

	public long GeometryRevision => transparentGeometryRevision;

	public bool GpuProfilingEnabled
	{
		get => gpuTimer.Enabled;
		set
		{
			ThrowIfDisposed();
			gpuTimer.Enabled = value;
			transparentGpuTimer.Enabled = value;
			options.GpuProfilingEnabled = value;
		}
	}

	public VoxelRendererStatistics Statistics => new VoxelRendererStatistics(
		world.LoadedChunkCount,
		gpuChunks.Count,
		visibleChunks,
		scheduler.PendingCount + pendingUploads.Count,
		acceptedMeshes,
		discardedMeshes,
		opaqueVertices,
		cutoutVertices,
		visibleTransparentFaces,
		visibleTransparentVertices,
		candidateChunks,
		activeGpuChunks.Count,
		inactiveCachedChunks,
		opaqueGeometry.PageCount,
		cutoutGeometry.PageCount
	);

	public bool HasValidTransparentOrdering => IsTransparentOrderingCurrent(
		transparentSnapshot != null,
		transparentSnapshot?.GeometryRevision ?? 0,
		transparentGeometryRevision,
		transparentSnapshot?.ActiveSetGeneration ?? 0,
		activeSetGeneration);

	internal static bool IsTransparentOrderingCurrent(
		bool hasSnapshot,
		long snapshotGeometryRevision,
		long currentGeometryRevision,
		long snapshotActiveSetGeneration,
		long currentActiveSetGeneration) =>
		hasSnapshot &&
		snapshotGeometryRevision == currentGeometryRevision &&
		snapshotActiveSetGeneration == currentActiveSetGeneration;

	public VoxelPresentationState GetPresentationState(ChunkCoordinate coordinate)
	{
		ThrowIfDisposed();
		if (!world.TryGetChunk(coordinate, out VoxelChunk chunk))
			return ResolvePresentationState(false, false, false, false);
		if (!lighting.TryGetChunkState(coordinate, out long lightGeneration, out long lightRevision))
			return ResolvePresentationState(true, false, false, false);
		bool residentMatches = gpuChunks.TryGetValue(coordinate, out GpuChunk gpuChunk) &&
			gpuChunk.WorldGeneration == chunk.Generation &&
			gpuChunk.Revision == chunk.Revision &&
			gpuChunk.LightGeneration == lightGeneration &&
			gpuChunk.LightRevision == lightRevision;
		bool emptyMatches = completedEmptyChunks.TryGetValue(coordinate, out CompletedEmptyChunk empty) &&
			empty.WorldGeneration == chunk.Generation &&
			empty.WorldRevision == chunk.Revision &&
			empty.LightGeneration == lightGeneration &&
			empty.LightRevision == lightRevision;
		return ResolvePresentationState(true, true, residentMatches, emptyMatches);
	}

	internal static VoxelPresentationState ResolvePresentationState(
		bool chunkExists,
		bool lightingPublished,
		bool residentMatches,
		bool emptyMatches)
	{
		if (!chunkExists)
			return VoxelPresentationState.Missing;
		if (!lightingPublished)
			return VoxelPresentationState.WaitingForLighting;
		if (residentMatches)
			return VoxelPresentationState.Resident;
		return emptyMatches ? VoxelPresentationState.EmptyComplete : VoxelPresentationState.Meshing;
	}

	public int UpdateMeshes(int? meshUploadBudget = null)
	{
		return UpdateMeshesCore(focus: null, meshUploadBudget);
	}

	public int UpdateMeshes(
		Camera camera,
		int? meshUploadBudget = null
	)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(camera);

		VoxelMeshingFocus focus = new VoxelMeshingFocus(
			camera,
			options.MaxRenderDistance,
			options.DeactivationMargin,
			cullingEnabled
		);

		int uploaded = UpdateMeshesCore(focus, meshUploadBudget);
		RefreshActiveSetIfNeeded(camera.Position, options.MaxRenderDistance);
		UpdateTransparentOrdering(camera, options.MaxRenderDistance);
		return uploaded;
	}

	private int UpdateMeshesCore(
		VoxelMeshingFocus? focus,
		int? meshUploadBudget
	)
	{
		ThrowIfDisposed();
		ProcessRemovedChunks();
		long schedulingStart = Stopwatch.GetTimestamp();
		scheduledMeshes = scheduler.SchedulePending(focus);
		meshSchedulingMilliseconds = Stopwatch.GetElapsedTime(
			schedulingStart
		).TotalMilliseconds;

		if (scheduler.TryDequeueFailure(out Exception failure))
		{
			throw failure;
		}

		int budget = meshUploadBudget ?? options.MeshUploadBudget;

		if (budget < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(meshUploadBudget));
		}

		while (scheduler.TryDequeue(out VoxelMeshData completed))
		{
			pendingUploads.Add(completed);
		}

		fastCompletedMeshes = RemoveMetadataOnlyResults();

		if (focus.HasValue && pendingUploads.Count > 1)
		{
			pendingUploads.Sort((left, right) => CompareReady(left, right, focus.Value));
		}

		double timeBudget = options.MeshUploadTimeBudgetMilliseconds;
		long uploadStart = Stopwatch.GetTimestamp();
		int uploaded = 0;

		while (uploaded < budget && uploaded < pendingUploads.Count)
		{
			if (uploaded > 0
				&& Stopwatch.GetElapsedTime(uploadStart).TotalMilliseconds >= timeBudget)
			{
				break;
			}

			VoxelMeshData result = pendingUploads[uploaded];

			try
			{
				Upload(result);
			}
			finally
			{
				result.ReleasePooledVertexBuffers();
			}

			acceptedMeshes++;
			uploaded++;
		}

		if (uploaded > 0)
		{
			pendingUploads.RemoveRange(0, uploaded);
		}

		meshUploadMilliseconds = Stopwatch.GetElapsedTime(uploadStart).TotalMilliseconds;
		uploadedMeshes = uploaded;

		if (scheduler.TryDequeueFailure(out failure))
		{
			throw failure;
		}

		return uploaded;
	}

	internal void RecordPageSubmission(
		double submissionMilliseconds,
		int allocatedBytes
	)
	{
		lastSubmissionMilliseconds = submissionMilliseconds;
		lastSubmissionAllocatedBytes = allocatedBytes;
	}

	private int RemoveMetadataOnlyResults()
	{
		int writeIndex = 0;
		int fastCompleted = 0;

		for (int readIndex = 0; readIndex < pendingUploads.Count; readIndex++)
		{
			VoxelMeshData result = pendingUploads[readIndex];

			if (!world.TryGetChunk(result.Coordinate, out VoxelChunk chunk))
			{
				RemoveGpuChunk(result.Coordinate);
				discardedMeshes++;
				result.ReleasePooledVertexBuffers();
				continue;
			}

			if (!IsMeshCurrent(result, chunk, lighting))
			{
				discardedMeshes++;
				scheduler.MarkDirty(result.Coordinate);
				result.ReleasePooledVertexBuffers();
				continue;
			}

			if (IsEmpty(result) && !gpuChunks.ContainsKey(result.Coordinate))
			{
				completedEmptyChunks[result.Coordinate] = new CompletedEmptyChunk(
					result.WorldGeneration,
					result.Revision,
					result.LightGeneration,
					result.LightRevision);
				acceptedMeshes++;
				fastCompleted++;
				result.ReleasePooledVertexBuffers();
				continue;
			}

			pendingUploads[writeIndex++] = result;
		}

		if (writeIndex < pendingUploads.Count)
		{
			pendingUploads.RemoveRange(writeIndex, pendingUploads.Count - writeIndex);
		}

		return fastCompleted;
	}

	private static bool IsEmpty(VoxelMeshData result)
	{
		return result.OpaqueVertexCount == 0
			&& result.CutoutVertexCount == 0
			&& result.AlphaShadowVertexCount == 0
			&& result.TransparentFaces.Length == 0;
	}

	private static int CompareReady(
		VoxelMeshData left,
		VoxelMeshData right,
		VoxelMeshingFocus focus
	)
	{
		int comparison = focus.GetPriority(left.Coordinate).CompareTo(
			focus.GetPriority(right.Coordinate)
		);

		return comparison != 0
			? comparison
			: right.Revision.CompareTo(left.Revision);
	}

	internal static bool IsMeshCurrent(
		VoxelMeshData result,
		VoxelChunk chunk,
		VoxelLighting lighting
	)
	{
		if (result == null)
		{
			throw new ArgumentNullException(nameof(result));
		}

		if (chunk == null)
		{
			throw new ArgumentNullException(nameof(chunk));
		}

		if (
			chunk.Coordinate != result.Coordinate
			|| chunk.Generation != result.WorldGeneration
			|| chunk.Revision != result.Revision
		)
		{
			return false;
		}

		if (lighting == null)
		{
			return result.LightGeneration == 0 && result.LightRevision == 0;
		}

		return lighting.TryGetChunkState(
			result.Coordinate,
			out long lightGeneration,
			out long lightRevision
		) && lightGeneration == result.LightGeneration
			&& lightRevision == result.LightRevision;
	}

}
