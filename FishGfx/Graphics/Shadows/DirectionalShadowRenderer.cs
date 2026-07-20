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
	private readonly List<int> dynamicCascadesNeedingRender = new();
	private readonly List<AxisAlignedBoundingBox> staticInvalidations = new();
	private CascadeSlot[] slots = Array.Empty<CascadeSlot>();
	private DirectionalShadowCascade[] pending = Array.Empty<DirectionalShadowCascade>();
	private Vector3[] pendingAnchors = Array.Empty<Vector3>();
	private float[] pendingVerticalFovs = Array.Empty<float>();
	private float[] pendingHorizontalFovs = Array.Empty<float>();
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

	public IReadOnlyList<int> DynamicCascadesNeedingRender => dynamicCascadesNeedingRender;

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
		for (int index = 0; index < slots.Length; index++)
		{
			slots[index].StaticDirty = true;
		}
	}

	public void InvalidateStaticCaster(in AxisAlignedBoundingBox bounds)
	{
		ObjectDisposedException.ThrowIf(disposed, this);
		if (!bounds.IsEmpty)
		{
			staticInvalidations.Add(bounds);
		}
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
		dynamicCascadesNeedingRender.Clear();

		for (int index = 0; index < slots.Length; index++)
		{
			slots[index].Valid = false;
			slots[index].DynamicValid = false;
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
		dynamicCascadesNeedingRender.Clear();
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
		for (int invalidationIndex = 0; invalidationIndex < staticInvalidations.Count; invalidationIndex++)
		{
			AxisAlignedBoundingBox dirty = staticInvalidations[invalidationIndex];
			for (int cascadeIndex = 0; cascadeIndex < options.CascadeCount; cascadeIndex++)
			{
				float radius = CalculateClipmapExtent(viewCamera, splits[cascadeIndex]);
				AxisAlignedBoundingBox bounds = AxisAlignedBoundingBox.FromPositionAndSize(
					viewCamera.Position - new Vector3(radius),
					new Vector3(radius * 2));
				if (bounds.Intersects(dirty))
				{
					slots[cascadeIndex].StaticDirty = true;
				}
			}
		}
		staticInvalidations.Clear();
		int ordinaryRefreshBudget = 1;
		int dynamicRefreshBudget = 2;

		for (int index = 0; index < options.CascadeCount; index++)
		{
			float nearDistance = index == 0 ? Math.Max(0.01f, viewCamera.Near) : splits[index - 1];
			pending[index] = BuildCascade(viewCamera, lightDirection, nearDistance, splits[index], index);
			pendingAnchors[index] = viewCamera.Position;
			pendingVerticalFovs[index] = viewCamera.VerticalFOV;
			pendingHorizontalFovs[index] = viewCamera.HorizontalFOV;
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

			if (slot.StaticDirty)
			{
				reason |= DirectionalShadowDirtyReason.VoxelGeometry;
			}

			float anchorThreshold = Math.Max(0.5f, splits[index] * 0.05f);
			bool projectionChanged = slot.Valid
				&& (MathF.Abs(slot.VerticalFov - viewCamera.VerticalFOV) > 1e-5f
					|| MathF.Abs(slot.HorizontalFov - viewCamera.HorizontalFOV) > 1e-5f);
			if (!slot.Valid
				|| projectionChanged
				|| Vector3.Distance(slot.AnchorPosition, viewCamera.Position) >= anchorThreshold)
			{
				reason |= DirectionalShadowDirtyReason.Camera;
			}

			bool forceAll = (reason & (DirectionalShadowDirtyReason.FirstUse
				| DirectionalShadowDirtyReason.Quality
				| DirectionalShadowDirtyReason.Teleport
				| DirectionalShadowDirtyReason.Sunrise)) != 0;
			bool due = forceAll || frameIndex - slot.LastRenderedFrame >= options.GetUpdateInterval(index);

			bool scheduled = reason != DirectionalShadowDirtyReason.None
				&& due
				&& (forceAll || ordinaryRefreshBudget > 0);
			if (scheduled)
			{
				slot.PendingDirtyReasons = reason;
				cascadesNeedingRender.Add(index);
				frameDirtyReasons |= reason;
				if (!forceAll)
				{
					ordinaryRefreshBudget--;
				}
			}

			DirectionalShadowDirtyReason dynamicReason = DirectionalShadowDirtyReason.None;
			if (!slot.DynamicValid)
			{
				dynamicReason |= DirectionalShadowDirtyReason.FirstUse;
			}
			if (qualityChanged)
			{
				dynamicReason |= DirectionalShadowDirtyReason.Quality;
			}
			if (sunrise)
			{
				dynamicReason |= DirectionalShadowDirtyReason.Sunrise;
			}
			if (teleport)
			{
				dynamicReason |= DirectionalShadowDirtyReason.Teleport;
			}
			if (dynamicActorsChanged)
			{
				dynamicReason |= DirectionalShadowDirtyReason.DynamicActor;
			}
			if (scheduled)
			{
				dynamicReason |= reason & ~DirectionalShadowDirtyReason.VoxelGeometry;
			}

			bool forceDynamic = (dynamicReason & (DirectionalShadowDirtyReason.FirstUse
				| DirectionalShadowDirtyReason.Quality
				| DirectionalShadowDirtyReason.Teleport
				| DirectionalShadowDirtyReason.Sunrise)) != 0;
			bool dynamicDue = forceDynamic
				|| frameIndex - slot.DynamicLastRenderedFrame >= options.GetUpdateInterval(index);
			if (dynamicReason != DirectionalShadowDirtyReason.None
				&& dynamicDue
				&& (forceDynamic || dynamicRefreshBudget > 0))
			{
				dynamicCascadesNeedingRender.Add(index);
				if (!forceDynamic)
				{
					dynamicRefreshBudget--;
				}
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

	public DirectionalShadowCascade GetDynamicCascade(int index)
	{
		ObjectDisposedException.ThrowIf(disposed, this);
		if (!dynamicCascadesNeedingRender.Contains(index))
		{
			throw new InvalidOperationException($"Dynamic cascade {index} is not pending rendering.");
		}

		return cascadesNeedingRender.Contains(index) ? pending[index] : slots[index].Cascade;
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
			CullMode = CullMode.Back,
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

	public RenderPass BeginDynamicCascadePass(RenderFrame frame, int index)
	{
		ObjectDisposedException.ThrowIf(disposed, this);
		ArgumentNullException.ThrowIfNull(frame);
		if (!dynamicCascadesNeedingRender.Contains(index))
		{
			throw new InvalidOperationException($"Dynamic cascade {index} is not pending rendering.");
		}

		DirectionalShadowCascade cascade = cascadesNeedingRender.Contains(index)
			? pending[index]
			: slots[index].Cascade;
		RenderState state = RenderState.Default with
		{
			BlendEnabled = false,
			ColorWriteMask = ColorWriteMask.None,
			CullMode = CullMode.Back,
			Winding = Winding.CounterClockwise,
			DepthBiasSlope = options.RasterSlopeBias,
			DepthBiasConstant = options.RasterConstantBias,
		};

		return frame.BeginPass(
			slots[index].DynamicTarget,
			new RenderPassDescriptor
			{
				View = new RenderView(cascade.Camera),
				State = state,
				ColorLoadAction = RenderLoadAction.DontCare,
				DepthLoadAction = RenderLoadAction.Clear,
				ClearDepth = 1,
			});
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
		slot.StaticDirty = false;
		slot.LightDirection = previousLightDirection;
		slot.AnchorPosition = pendingAnchors[index];
		slot.VerticalFov = pendingVerticalFovs[index];
		slot.HorizontalFov = pendingHorizontalFovs[index];
		slot.PendingDirtyReasons = DirectionalShadowDirtyReason.None;
		slot.Valid = true;
	}

	public void CompleteDynamicCascade(int index)
	{
		ObjectDisposedException.ThrowIf(disposed, this);
		if (!dynamicCascadesNeedingRender.Contains(index))
		{
			throw new InvalidOperationException($"Dynamic cascade {index} is not pending rendering.");
		}

		CascadeSlot slot = slots[index];
		slot.DynamicLastRenderedFrame = frameIndex;
		slot.DynamicValid = slot.Valid;
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
			slots[index].DynamicTarget.Dispose();
			gpuTimers[index].Dispose();
		}

		slots = Array.Empty<CascadeSlot>();
		pending = Array.Empty<DirectionalShadowCascade>();
		pendingAnchors = Array.Empty<Vector3>();
		pendingVerticalFovs = Array.Empty<float>();
		pendingHorizontalFovs = Array.Empty<float>();
		gpuTimers = Array.Empty<DirectionalShadowGpuTimer>();
		cascadesNeedingRender.Clear();
		dynamicCascadesNeedingRender.Clear();
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
		return BuildStableClipmap(
			viewCamera,
			lightDirection,
			nearDistance,
			farDistance,
			index,
			options.Resolution,
			options.MaximumDistance
		);
	}

	private static float CalculateClipmapExtent(Camera viewCamera, float farDistance)
	{
		float verticalTangent = MathF.Tan(viewCamera.VerticalFOV * 0.5f);
		float horizontalTangent = MathF.Tan(viewCamera.HorizontalFOV * 0.5f);
		float radius = farDistance * MathF.Sqrt(
			1 + verticalTangent * verticalTangent
				+ horizontalTangent * horizontalTangent);
		return MathF.Ceiling(radius * 16) / 16 + ReceiverExpansion;
	}

	internal static DirectionalShadowCascade BuildStableClipmap(
		Camera viewCamera,
		Vector3 lightDirection,
		float nearDistance,
		float farDistance,
		int index,
		int resolution,
		float maximumDistance)
	{
		ArgumentNullException.ThrowIfNull(viewCamera);
		if (resolution <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(resolution));
		}
		if (!float.IsFinite(maximumDistance) || maximumDistance <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(maximumDistance));
		}

		// A camera-position-centred sphere makes the clipmap independent of view
		// yaw and pitch. Rotation therefore never changes its matrix or dirties it.
		float verticalTangent = MathF.Tan(viewCamera.VerticalFOV * 0.5f);
		float horizontalTangent = MathF.Tan(viewCamera.HorizontalFOV * 0.5f);
		float radius = farDistance * MathF.Sqrt(
			1 + verticalTangent * verticalTangent
				+ horizontalTangent * horizontalTangent
		);
		radius = MathF.Ceiling(radius * 16) / 16;
		float extent = radius + ReceiverExpansion;
		Vector3 center = viewCamera.Position;
		Vector3 up = MathF.Abs(Vector3.Dot(lightDirection, Vector3.UnitY)) > 0.98f
			? Vector3.UnitZ
			: Vector3.UnitY;
		Vector3 right = Vector3.Normalize(Vector3.Cross(lightDirection, up));
		Vector3 lightUp = Vector3.Normalize(Vector3.Cross(right, lightDirection));
		float texelSize = extent * 2 / resolution;
		float rightCoordinate = Vector3.Dot(center, right);
		float upCoordinate = Vector3.Dot(center, lightUp);
		float snappedRight = MathF.Round(rightCoordinate / texelSize) * texelSize;
		float snappedUp = MathF.Round(upCoordinate / texelSize) * texelSize;
		center += right * (snappedRight - rightCoordinate);
		center += lightUp * (snappedUp - upCoordinate);
		float casterDepth = maximumDistance + radius * 2;
		Camera camera = new Camera
		{
			CameraUpNormal = up,
			Position = center - lightDirection * (maximumDistance + radius),
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

	private DirectionalShadowFrame CreateCurrentFrame()
	{
		if (!wasEnabled || options.CascadeCount == 0 || currentSnapshot == null)
		{
			return default;
		}

		for (int index = 0; index < options.CascadeCount; index++)
		{
			CascadeSlot slot = slots[index];

			if (!slot.Valid || !slot.DynamicValid)
			{
				return default;
			}

			currentSnapshot.DepthTextures[index] = slot.Target.DepthStencilAttachment;
			currentSnapshot.DynamicDepthTextures[index] = slot.DynamicTarget.DepthStencilAttachment;
			currentSnapshot.Matrices[index] = slot.Cascade.ViewProjection;
			currentSnapshot.Splits[index] = slot.Cascade.FarDistance;
			currentSnapshot.DepthRanges[index] = slot.Cascade.FarDistance - slot.Cascade.NearDistance;
			currentSnapshot.MapDepthRanges[index] =
				slot.Cascade.Camera.Far - slot.Cascade.Camera.Near;
			currentSnapshot.WorldTexelSizes[index] = Math.Max(
				slot.Cascade.TexelWorldSize.X,
				slot.Cascade.TexelWorldSize.Y
			);
		}

		currentSnapshot.Strength = currentStrength;

		return new DirectionalShadowFrame(currentSnapshot);
	}

	private void RecreateTargets()
	{
		for (int index = 0; index < slots.Length; index++)
		{
			slots[index].Target.Dispose();
			slots[index].DynamicTarget.Dispose();
			gpuTimers[index].Dispose();
		}

		slots = new CascadeSlot[options.CascadeCount];
		pending = new DirectionalShadowCascade[options.CascadeCount];
		pendingAnchors = new Vector3[options.CascadeCount];
		pendingVerticalFovs = new float[options.CascadeCount];
		pendingHorizontalFovs = new float[options.CascadeCount];
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
				DynamicDepthTextures = new Texture[options.CascadeCount],
				Matrices = new Matrix4x4[options.CascadeCount],
				Splits = new float[options.CascadeCount],
				DepthRanges = new float[options.CascadeCount],
				MapDepthRanges = new float[options.CascadeCount],
				WorldTexelSizes = new float[options.CascadeCount],
			};

		for (int index = 0; index < slots.Length; index++)
		{
			gpuTimers[index] = new DirectionalShadowGpuTimer(context);
			RenderTarget staticTarget = context.CreateRenderTarget(
				new RenderTargetDescriptor(
					options.Resolution,
					options.Resolution,
					Array.Empty<TextureFormat>(),
					TextureFormat.Depth24Unorm));
			RenderTarget dynamicTarget = context.CreateRenderTarget(
				new RenderTargetDescriptor(
					Math.Max(1, options.Resolution / 2),
					Math.Max(1, options.Resolution / 2),
					Array.Empty<TextureFormat>(),
					TextureFormat.Depth24Unorm));
			TextureSamplingState shadowSampling = new(
				TextureFilter.Linear,
				TextureFilter.Linear,
				TextureWrap.ClampToEdge,
				TextureWrap.ClampToEdge,
				comparison: TextureComparison.LessOrEqual);
			staticTarget.DepthStencilAttachment.SetSampling(shadowSampling);
			dynamicTarget.DepthStencilAttachment.SetSampling(shadowSampling);
			slots[index] = new CascadeSlot
			{
				Target = staticTarget,
				DynamicTarget = dynamicTarget,
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

		public required RenderTarget DynamicTarget { get; init; }

		public DirectionalShadowCascade Cascade { get; set; }

		public long LastRenderedFrame { get; set; }

		public DirectionalShadowDirtyReason LastDirtyReasons { get; set; }

		public DirectionalShadowDirtyReason PendingDirtyReasons { get; set; }

		public bool Valid { get; set; }

		public bool DynamicValid { get; set; }

		public long DynamicLastRenderedFrame { get; set; }

		public long GeometryRevision { get; set; } = long.MinValue;

		public bool StaticDirty { get; set; }

		public Vector3 LightDirection { get; set; }

		public Vector3 AnchorPosition { get; set; }

		public float VerticalFov { get; set; }

		public float HorizontalFov { get; set; }
	}
}
