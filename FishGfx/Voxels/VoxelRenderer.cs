using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using FishGfx.Graphics;

namespace FishGfx.Voxels;

public sealed partial class VoxelRenderer : IDisposable
{
	private readonly VoxelWorld world;
	private readonly VoxelLighting lighting;
	private Texture atlasTexture;
	private readonly VoxelAtlasLayout atlasLayout;
	private readonly VoxelRendererOptions options;
	private readonly VoxelMeshingScheduler scheduler;
	private readonly Dictionary<ChunkCoordinate, GpuChunk> gpuChunks = new Dictionary<ChunkCoordinate, GpuChunk>();
	private readonly List<GpuChunk> orderedGpuChunks = new List<GpuChunk>();
	private readonly List<GpuChunk> activeGpuChunks = new List<GpuChunk>();
	private readonly HashSet<ChunkCoordinate> activeCoordinates = new HashSet<ChunkCoordinate>();
	private readonly List<VoxelPassEntry> visibleOpaque = new List<VoxelPassEntry>();
	private readonly List<VoxelPassEntry> visibleCutout = new List<VoxelPassEntry>();
	private readonly List<GpuChunk> visibleTransparentChunks = new List<GpuChunk>();
	private readonly List<VoxelMeshData> pendingUploads = new List<VoxelMeshData>();
	private readonly List<VoxelTransparentFaceInstance> transparentFaces =
		new List<VoxelTransparentFaceInstance>();
	private readonly ConcurrentQueue<ChunkCoordinate> removedChunks = new ConcurrentQueue<ChunkCoordinate>();
	private VoxelMesh transparentMesh;
	private readonly ShaderProgram voxelShader;
	private readonly ShaderProgram waveShader;
	private readonly ShaderStage vertexShader;
	private readonly ShaderStage waveVertexShader;
	private readonly ShaderStage fragmentShader;
	private readonly RenderState opaqueState;
	private readonly RenderState transparentState;
	private readonly VoxelGeometryPagePool opaqueGeometry;
	private readonly VoxelGeometryPagePool cutoutGeometry;
	private readonly GraphicsBuffer indirectBuffer;
	private readonly VoxelGpuTimer gpuTimer;
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
		voxelShader = graphics.CreateShaderProgram(vertexShader, fragmentShader);
		waveShader = graphics.CreateShaderProgram(waveVertexShader, fragmentShader);
		transparentMesh = new VoxelMesh(graphics, BufferUsage.Stream);
		opaqueGeometry = new VoxelGeometryPagePool(graphics, this.options.GeometryPageSizeBytes);
		cutoutGeometry = new VoxelGeometryPagePool(graphics, this.options.GeometryPageSizeBytes);
		indirectBuffer = graphics.CreateBuffer(new GraphicsBufferDescriptor(
			64 * 1024,
			BufferBindFlags.Indirect,
			BufferUsage.Stream
		));
		gpuTimer = new VoxelGpuTimer(graphics)
		{
			Enabled = this.options.GpuProfilingEnabled,
		};

		opaqueState = CreateState(transparent: false);
		transparentState = CreateState(transparent: true);
	}

	public GraphicsContext Graphics { get; }

	public Texture AtlasTexture => atlasTexture;

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
	}

	public bool IsIdle => scheduler.PendingCount == 0 && pendingUploads.Count == 0;

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

	public bool GpuProfilingEnabled
	{
		get => gpuTimer.Enabled;
		set
		{
			ThrowIfDisposed();
			gpuTimer.Enabled = value;
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

		return UpdateMeshesCore(focus, meshUploadBudget);
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
