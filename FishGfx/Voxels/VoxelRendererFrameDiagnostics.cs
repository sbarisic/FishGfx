namespace FishGfx.Voxels;

public readonly struct VoxelRendererFrameDiagnostics
{
	internal VoxelRendererFrameDiagnostics(
		double cullingMilliseconds,
		double transparentBuildMilliseconds,
		double meshSchedulingMilliseconds,
		double meshUploadMilliseconds,
		int scheduledMeshes,
		int uploadedMeshes,
		int fastCompletedMeshes,
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
		MeshSchedulingMilliseconds = meshSchedulingMilliseconds;
		MeshUploadMilliseconds = meshUploadMilliseconds;
		ScheduledMeshes = scheduledMeshes;
		UploadedMeshes = uploadedMeshes;
		FastCompletedMeshes = fastCompletedMeshes;
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

	public double MeshSchedulingMilliseconds { get; }

	public double MeshUploadMilliseconds { get; }

	public int ScheduledMeshes { get; }

	public int UploadedMeshes { get; }

	public int FastCompletedMeshes { get; }

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
