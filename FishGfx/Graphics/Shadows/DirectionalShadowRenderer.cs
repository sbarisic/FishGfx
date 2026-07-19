using System;
using System.Collections.Generic;
using System.Numerics;

namespace FishGfx.Graphics.Shadows;

public sealed class DirectionalShadowRenderer : IDisposable
{
	private const float MinimumStrength = 0.01f;
	private const float SunDirectionThresholdRadians = 0.05f * MathF.PI / 180;
	private const float ReceiverExpansion = 16;

	private readonly GraphicsContext context;
	private readonly List<int> cascadesNeedingRender = new();
	private CascadeSlot[] slots = Array.Empty<CascadeSlot>();
	private DirectionalShadowCascade[] pending = Array.Empty<DirectionalShadowCascade>();
	private DirectionalShadowGpuTimer[] gpuTimers = Array.Empty<DirectionalShadowGpuTimer>();
	private DirectionalShadowFrame.Snapshot currentSnapshot;
	private DirectionalShadowOptions options;
	private Vector3 previousViewPosition;
	private Vector3 previousLightDirection;
	private long frameIndex;
	private long geometryRevision;
	private bool dynamicActorsChanged;
	private bool qualityChanged;
	private bool wasEnabled;
	private bool disposed;
	private float currentStrength;
	private DirectionalShadowDirtyReason frameDirtyReasons;

	public DirectionalShadowRenderer(
		GraphicsContext context,
		DirectionalShadowOptions options)
	{
		this.context = context ?? throw new ArgumentNullException(nameof(context));
		SetOptions(options);
	}

	public IReadOnlyList<int> CascadesNeedingRender => cascadesNeedingRender;

	public DirectionalShadowFrame CurrentFrame => CreateCurrentFrame();

	public DirectionalShadowDiagnostics Diagnostics { get; private set; }

	public bool GpuProfilingEnabled
	{
		get => gpuTimers.Length > 0 && gpuTimers[0].Enabled;
		set
		{
			ObjectDisposedException.ThrowIf(disposed, this);

			for (int index = 0; index < gpuTimers.Length; index++)
			{
				gpuTimers[index].Enabled = value;
			}
		}
	}

	public void SetOptions(DirectionalShadowOptions options)
	{
		ObjectDisposedException.ThrowIf(disposed, this);
		ArgumentNullException.ThrowIfNull(options);
		options.Validate();

		bool recreate = this.options == null
			|| this.options.CascadeCount != options.CascadeCount
			|| this.options.Resolution != options.Resolution;

		this.options = options;
		qualityChanged = true;

		if (recreate)
		{
			RecreateTargets();
		}
		else if (currentSnapshot != null)
		{
			currentSnapshot.BlendFraction = options.CascadeBlendFraction;
			currentSnapshot.Filter = options.Filter;
		}
	}

	public void InvalidateGeometry()
	{
		ObjectDisposedException.ThrowIf(disposed, this);
		geometryRevision++;
	}

	public void NotifyDynamicActorsChanged()
	{
		ObjectDisposedException.ThrowIf(disposed, this);
		dynamicActorsChanged = true;
	}

	public void InvalidateAll()
	{
		ObjectDisposedException.ThrowIf(disposed, this);
		wasEnabled = false;
		currentStrength = 0;
		qualityChanged = true;
		cascadesNeedingRender.Clear();

		for (int index = 0; index < slots.Length; index++)
		{
			slots[index].Valid = false;
			slots[index].PendingDirtyReasons = DirectionalShadowDirtyReason.None;
		}

		Diagnostics = default;
	}

	public void Prepare(
		Camera viewCamera,
		Vector3 lightDirection,
		float strength,
		long frameIndex)
	{
		ObjectDisposedException.ThrowIf(disposed, this);
		ArgumentNullException.ThrowIfNull(viewCamera);

		if (!float.IsFinite(strength))
		{
			throw new ArgumentOutOfRangeException(nameof(strength));
		}

		if (!IsFinite(lightDirection) || lightDirection.LengthSquared() < 1e-8f)
		{
			throw new ArgumentOutOfRangeException(nameof(lightDirection));
		}

		this.frameIndex = frameIndex;
		cascadesNeedingRender.Clear();
		frameDirtyReasons = DirectionalShadowDirtyReason.None;
		strength = Math.Clamp(strength, 0, 1);

		for (int index = 0; index < gpuTimers.Length; index++)
		{
			gpuTimers[index].Poll();
		}

		if (options.CascadeCount == 0 || options.MaximumDistance <= 0 || strength <= MinimumStrength)
		{
			wasEnabled = false;
			currentStrength = 0;
			Diagnostics = new DirectionalShadowDiagnostics(
				false,
				0,
				0,
				0,
				DirectionalShadowDirtyReason.None,
				0,
				0,
				0,
				0,
				Array.Empty<DirectionalShadowCascadeDiagnostics>()
			);

			return;
		}

		lightDirection = Vector3.Normalize(lightDirection);
		bool sunrise = !wasEnabled;
		bool teleport = wasEnabled
			&& Vector3.Distance(viewCamera.Position, previousViewPosition) > options.MaximumDistance * 0.5f;
		float[] splits = CalculateSplits(viewCamera.Near, options.MaximumDistance, options.CascadeCount, options.SplitLambda);

		for (int index = 0; index < options.CascadeCount; index++)
		{
			float nearDistance = index == 0 ? Math.Max(0.01f, viewCamera.Near) : splits[index - 1];
			pending[index] = BuildCascade(viewCamera, lightDirection, nearDistance, splits[index], index);
			CascadeSlot slot = slots[index];
			DirectionalShadowDirtyReason reason = DirectionalShadowDirtyReason.None;

			if (!slot.Valid)
			{
				reason |= DirectionalShadowDirtyReason.FirstUse;
			}

			if (qualityChanged)
			{
				reason |= DirectionalShadowDirtyReason.Quality;
			}

			if (sunrise)
			{
				reason |= DirectionalShadowDirtyReason.Sunrise;
			}

			if (teleport)
			{
				reason |= DirectionalShadowDirtyReason.Teleport;
			}

			if (!slot.Valid
				|| AngleBetween(slot.LightDirection, lightDirection) >= SunDirectionThresholdRadians)
			{
				reason |= DirectionalShadowDirtyReason.Sun;
			}

			if (slot.GeometryRevision != geometryRevision)
			{
				reason |= DirectionalShadowDirtyReason.VoxelGeometry;
			}

			if (dynamicActorsChanged)
			{
				reason |= DirectionalShadowDirtyReason.DynamicActor;
			}

			if (!MatricesNearlyEqual(slot.Cascade.ViewProjection, pending[index].ViewProjection))
			{
				reason |= DirectionalShadowDirtyReason.Camera;
			}

			bool forceAll = (reason & (DirectionalShadowDirtyReason.FirstUse
				| DirectionalShadowDirtyReason.Quality
				| DirectionalShadowDirtyReason.Teleport
				| DirectionalShadowDirtyReason.Sunrise)) != 0;
			bool due = forceAll || frameIndex - slot.LastRenderedFrame >= options.GetUpdateInterval(index);

			if (reason != DirectionalShadowDirtyReason.None && due)
			{
				slot.PendingDirtyReasons = reason;
				cascadesNeedingRender.Add(index);
				frameDirtyReasons |= reason;
			}
		}

		wasEnabled = true;
		currentStrength = strength;
		previousViewPosition = viewCamera.Position;
		previousLightDirection = lightDirection;
		dynamicActorsChanged = false;
		qualityChanged = false;
		RefreshDiagnostics(strength);
	}

	public DirectionalShadowCascade GetPendingCascade(int index)
	{
		ObjectDisposedException.ThrowIf(disposed, this);

		if (!cascadesNeedingRender.Contains(index))
		{
			throw new InvalidOperationException($"Cascade {index} is not pending rendering.");
		}

		return pending[index];
	}

	public RenderPass BeginCascadePass(RenderFrame frame, int index)
	{
		ObjectDisposedException.ThrowIf(disposed, this);
		ArgumentNullException.ThrowIfNull(frame);
		DirectionalShadowCascade cascade = GetPendingCascade(index);
		RenderState state = RenderState.Default with
		{
			BlendEnabled = false,
			ColorWriteMask = ColorWriteMask.None,
			CullMode = CullMode.Front,
			Winding = Winding.CounterClockwise,
			DepthBiasSlope = options.RasterSlopeBias,
			DepthBiasConstant = options.RasterConstantBias,
		};

		return frame.BeginPass(
			slots[index].Target,
			new RenderPassDescriptor
			{
				View = new RenderView(cascade.Camera),
				State = state,
				ColorLoadAction = RenderLoadAction.DontCare,
				DepthLoadAction = RenderLoadAction.Clear,
				ClearDepth = 1,
			}
		);
	}

	public IDisposable BeginCascadeTiming(RenderPass pass, int index)
	{
		ObjectDisposedException.ThrowIf(disposed, this);
		ArgumentNullException.ThrowIfNull(pass);
		GetPendingCascade(index);

		return gpuTimers[index].Begin(pass);
	}

	public void CompleteCascade(int index)
	{
		ObjectDisposedException.ThrowIf(disposed, this);

		if (!cascadesNeedingRender.Contains(index))
		{
			throw new InvalidOperationException($"Cascade {index} is not pending rendering.");
		}

		CascadeSlot slot = slots[index];
		slot.Cascade = pending[index];
		slot.LastRenderedFrame = frameIndex;
		slot.LastDirtyReasons = slot.PendingDirtyReasons;
		slot.GeometryRevision = geometryRevision;
		slot.LightDirection = previousLightDirection;
		slot.PendingDirtyReasons = DirectionalShadowDirtyReason.None;
		slot.Valid = true;
	}

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}

		disposed = true;

		for (int index = 0; index < slots.Length; index++)
		{
			slots[index].Target.Dispose();
			gpuTimers[index].Dispose();
		}

		slots = Array.Empty<CascadeSlot>();
		pending = Array.Empty<DirectionalShadowCascade>();
		gpuTimers = Array.Empty<DirectionalShadowGpuTimer>();
		cascadesNeedingRender.Clear();
	}

	public static float[] CalculateSplits(float nearDistance, float farDistance, int count, float lambda)
	{
		if (count <= 0)
		{
			return Array.Empty<float>();
		}

		nearDistance = Math.Max(0.01f, nearDistance);
		farDistance = Math.Max(nearDistance + 0.01f, farDistance);
		float[] splits = new float[count];

		for (int index = 1; index <= count; index++)
		{
			float ratio = (float)index / count;
			float logarithmic = nearDistance * MathF.Pow(farDistance / nearDistance, ratio);
			float uniform = nearDistance + (farDistance - nearDistance) * ratio;
			splits[index - 1] = uniform + (logarithmic - uniform) * lambda;
		}

		splits[^1] = farDistance;

		return splits;
	}

	private DirectionalShadowCascade BuildCascade(
		Camera viewCamera,
		Vector3 lightDirection,
		float nearDistance,
		float farDistance,
		int index)
	{
		Span<Vector3> corners = stackalloc Vector3[8];
		BuildFrustumSliceCorners(viewCamera, nearDistance, farDistance, corners);
		Vector3 center = Vector3.Zero;

		for (int cornerIndex = 0; cornerIndex < corners.Length; cornerIndex++)
		{
			center += corners[cornerIndex];
		}

		center /= corners.Length;
		float radius = 0;

		for (int cornerIndex = 0; cornerIndex < corners.Length; cornerIndex++)
		{
			radius = Math.Max(radius, Vector3.Distance(center, corners[cornerIndex]));
		}

		radius = MathF.Ceiling(radius * 16) / 16;
		float extent = radius + ReceiverExpansion;
		Vector3 up = MathF.Abs(Vector3.Dot(lightDirection, Vector3.UnitY)) > 0.98f
			? Vector3.UnitZ
			: Vector3.UnitY;
		Vector3 right = Vector3.Normalize(Vector3.Cross(lightDirection, up));
		Vector3 lightUp = Vector3.Normalize(Vector3.Cross(right, lightDirection));
		float texelSize = extent * 2 / options.Resolution;
		float rightCoordinate = Vector3.Dot(center, right);
		float upCoordinate = Vector3.Dot(center, lightUp);
		float snappedRight = MathF.Round(rightCoordinate / texelSize) * texelSize;
		float snappedUp = MathF.Round(upCoordinate / texelSize) * texelSize;
		center += right * (snappedRight - rightCoordinate);
		center += lightUp * (snappedUp - upCoordinate);
		float casterDepth = options.MaximumDistance + radius * 2;
		Camera camera = new Camera
		{
			CameraUpNormal = up,
			Position = center - lightDirection * (options.MaximumDistance + radius),
		};

		camera.LookAt(center);
		camera.SetOrthogonal(-extent, -extent, extent, extent, 0.1f, casterDepth);

		return new DirectionalShadowCascade(
			index,
			camera,
			camera.View * camera.Projection,
			nearDistance,
			farDistance,
			new Vector2(texelSize)
		);
	}

	private static void BuildFrustumSliceCorners(
		Camera camera,
		float nearDistance,
		float farDistance,
		Span<Vector3> corners)
	{
		Vector3 forward = camera.WorldForwardNormal;
		Vector3 right = camera.WorldRightNormal;
		Vector3 up = camera.WorldUpNormal;
		float verticalTangent = MathF.Tan(camera.VerticalFOV * 0.5f);
		float horizontalTangent = MathF.Tan(camera.HorizontalFOV * 0.5f);
		Vector3 nearCenter = camera.Position + forward * nearDistance;
		Vector3 farCenter = camera.Position + forward * farDistance;
		float nearHalfHeight = verticalTangent * nearDistance;
		float nearHalfWidth = horizontalTangent * nearDistance;
		float farHalfHeight = verticalTangent * farDistance;
		float farHalfWidth = horizontalTangent * farDistance;

		corners[0] = nearCenter - right * nearHalfWidth - up * nearHalfHeight;
		corners[1] = nearCenter + right * nearHalfWidth - up * nearHalfHeight;
		corners[2] = nearCenter - right * nearHalfWidth + up * nearHalfHeight;
		corners[3] = nearCenter + right * nearHalfWidth + up * nearHalfHeight;
		corners[4] = farCenter - right * farHalfWidth - up * farHalfHeight;
		corners[5] = farCenter + right * farHalfWidth - up * farHalfHeight;
		corners[6] = farCenter - right * farHalfWidth + up * farHalfHeight;
		corners[7] = farCenter + right * farHalfWidth + up * farHalfHeight;
	}

	private DirectionalShadowFrame CreateCurrentFrame()
	{
		if (!wasEnabled || options.CascadeCount == 0 || currentSnapshot == null)
		{
			return default;
		}

		for (int index = 0; index < options.CascadeCount; index++)
		{
			CascadeSlot slot = slots[index];

			if (!slot.Valid)
			{
				return default;
			}

			currentSnapshot.DepthTextures[index] = slot.Target.DepthStencilAttachment;
			currentSnapshot.Matrices[index] = slot.Cascade.ViewProjection;
			currentSnapshot.Splits[index] = slot.Cascade.FarDistance;
			currentSnapshot.DepthRanges[index] = slot.Cascade.FarDistance - slot.Cascade.NearDistance;
		}

		currentSnapshot.Strength = currentStrength;

		return new DirectionalShadowFrame(currentSnapshot);
	}

	private void RecreateTargets()
	{
		for (int index = 0; index < slots.Length; index++)
		{
			slots[index].Target.Dispose();
			gpuTimers[index].Dispose();
		}

		slots = new CascadeSlot[options.CascadeCount];
		pending = new DirectionalShadowCascade[options.CascadeCount];
		gpuTimers = new DirectionalShadowGpuTimer[options.CascadeCount];
		currentSnapshot = options.CascadeCount == 0
			? null
			: new DirectionalShadowFrame.Snapshot
			{
				CascadeCount = options.CascadeCount,
				Strength = 0,
				BlendFraction = options.CascadeBlendFraction,
				Filter = options.Filter,
				DepthTextures = new Texture[options.CascadeCount],
				Matrices = new Matrix4x4[options.CascadeCount],
				Splits = new float[options.CascadeCount],
				DepthRanges = new float[options.CascadeCount],
			};

		for (int index = 0; index < slots.Length; index++)
		{
			gpuTimers[index] = new DirectionalShadowGpuTimer(context);
			slots[index] = new CascadeSlot
			{
				Target = context.CreateRenderTarget(
					new RenderTargetDescriptor(
						options.Resolution,
						options.Resolution,
						Array.Empty<TextureFormat>(),
						TextureFormat.Depth24Unorm
					)
				),
			};
		}
	}

	private void RefreshDiagnostics(float strength)
	{
		DirectionalShadowCascadeDiagnostics[] cascadeDiagnostics = new DirectionalShadowCascadeDiagnostics[options.CascadeCount];

		for (int index = 0; index < options.CascadeCount; index++)
		{
			CascadeSlot slot = slots[index];
			DirectionalShadowCascade cascade = slot.Valid ? slot.Cascade : pending[index];
			cascadeDiagnostics[index] = new DirectionalShadowCascadeDiagnostics(
				index,
				cascade.NearDistance,
				cascade.FarDistance,
				slot.Valid ? frameIndex - slot.LastRenderedFrame : 0,
				slot.LastDirtyReasons,
				0,
				0,
				0,
				gpuTimers[index].LastMilliseconds
			);
		}

		Diagnostics = new DirectionalShadowDiagnostics(
			strength > MinimumStrength,
			options.MaximumDistance,
			options.CascadeCount,
			cascadesNeedingRender.Count,
			frameDirtyReasons,
			0,
			0,
			0,
			0,
			cascadeDiagnostics
		);
	}

	private static bool MatricesNearlyEqual(Matrix4x4 left, Matrix4x4 right)
	{
		const float epsilon = 1e-5f;
		return MathF.Abs(left.M11 - right.M11) <= epsilon
			&& MathF.Abs(left.M12 - right.M12) <= epsilon
			&& MathF.Abs(left.M13 - right.M13) <= epsilon
			&& MathF.Abs(left.M14 - right.M14) <= epsilon
			&& MathF.Abs(left.M21 - right.M21) <= epsilon
			&& MathF.Abs(left.M22 - right.M22) <= epsilon
			&& MathF.Abs(left.M23 - right.M23) <= epsilon
			&& MathF.Abs(left.M24 - right.M24) <= epsilon
			&& MathF.Abs(left.M31 - right.M31) <= epsilon
			&& MathF.Abs(left.M32 - right.M32) <= epsilon
			&& MathF.Abs(left.M33 - right.M33) <= epsilon
			&& MathF.Abs(left.M34 - right.M34) <= epsilon
			&& MathF.Abs(left.M41 - right.M41) <= epsilon
			&& MathF.Abs(left.M42 - right.M42) <= epsilon
			&& MathF.Abs(left.M43 - right.M43) <= epsilon
			&& MathF.Abs(left.M44 - right.M44) <= epsilon;
	}

	private static float AngleBetween(Vector3 left, Vector3 right)
	{
		return MathF.Acos(Math.Clamp(Vector3.Dot(left, right), -1, 1));
	}

	private static bool IsFinite(Vector3 value)
	{
		return float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
	}

	private sealed class CascadeSlot
	{
		public required RenderTarget Target { get; init; }

		public DirectionalShadowCascade Cascade { get; set; }

		public long LastRenderedFrame { get; set; }

		public DirectionalShadowDirtyReason LastDirtyReasons { get; set; }

		public DirectionalShadowDirtyReason PendingDirtyReasons { get; set; }

		public bool Valid { get; set; }

		public long GeometryRevision { get; set; } = long.MinValue;

		public Vector3 LightDirection { get; set; }
	}
}
