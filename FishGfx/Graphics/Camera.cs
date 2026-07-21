using System;
using System.Numerics;

namespace FishGfx.Graphics;

public partial class Camera
{
	private Matrix4x4 view;
	private Matrix4x4 world;
	private Vector3 position;
	private Quaternion rotation;
	private Vector3 worldForwardNormal;
	private Vector3 worldRightNormal;
	private Vector3 worldUpNormal;
	private float yaw;
	private float pitch;
	private bool dirty;

	public Camera()
	{
		view = Matrix4x4.Identity;
		world = Matrix4x4.Identity;
		Projection = Matrix4x4.Identity;
		position = Vector3.Zero;
		rotation = Quaternion.Identity;
		CameraUpNormal = UpNormal;
		dirty = true;
	}

	public float Near { get; private set; }

	public float Far { get; private set; }

	public float VerticalFOV { get; private set; }

	public float HorizontalFOV { get; private set; }

	public Matrix4x4 View
	{
		get
		{
			Refresh();

			return view;
		}
	}

	public Matrix4x4 World
	{
		get
		{
			Refresh();

			return world;
		}
	}

	public Matrix4x4 Projection { get; private set; }

	public Vector3 Position
	{
		get => position;
		set
		{
			position = value;
			dirty = true;
		}
	}

	public Quaternion Rotation
	{
		get => rotation;
		set
		{
			rotation = value;
			dirty = true;
		}
	}

	public Vector2 ViewportSize { get; private set; }

	public Vector3 ForwardNormal => -Vector3.UnitZ;

	public Vector3 RightNormal => Vector3.UnitX;

	public Vector3 UpNormal => Vector3.UnitY;

	public Vector3 WorldForwardNormal
	{
		get
		{
			Refresh();

			return worldForwardNormal;
		}
	}

	public Vector3 WorldRightNormal
	{
		get
		{
			Refresh();

			return worldRightNormal;
		}
	}

	public Vector3 WorldUpNormal
	{
		get
		{
			Refresh();

			return worldUpNormal;
		}
	}

	public bool MouseMovement { get; set; }

	public Vector3 CameraUpNormal { get; set; }

	public Vector2 PitchClamp { get; set; } = new(-90, 90);

	public void LookAtFitToScreen(Vector3 target, float radius)
	{
		if (!float.IsFinite(radius) || radius <= 0)
			throw new ArgumentOutOfRangeException(nameof(radius));
		Vector3 eyeOffset = Position - target;
		Vector3 toEye = eyeOffset.LengthSquared() > 1e-12f
			? Vector3.Normalize(eyeOffset)
			: -WorldForwardNormal;
		float tangent = MathF.Tan(Math.Min(HorizontalFOV, VerticalFOV) * 0.5f);
		if (!float.IsFinite(tangent) || tangent <= 0)
			throw new InvalidOperationException("The camera field of view cannot fit a target on screen.");
		float distance = radius / tangent;
		Position = target + distance * toEye;
		LookAt(target);
	}

	public void SetOrthogonal(
		float left,
		float bottom,
		float right,
		float top,
		float nearPlane = 1,
		float farPlane = 10000
	)
	{
		Projection = Matrix4x4.CreateOrthographicOffCenter(
			left,
			right,
			bottom,
			top,
			nearPlane,
			farPlane
		);
		ViewportSize = new Vector2(
			MathF.Abs(left - right),
			MathF.Abs(bottom - top)
		);
		Near = nearPlane;
		Far = farPlane;
	}

	public void SetPerspective(
		float width,
		float height,
		float horizontalFov = 1.5708f,
		float nearPlane = 1,
		float farPlane = 7500
	)
	{
		HorizontalFOV = horizontalFov;
		VerticalFOV = VerticalFOVFromHorizontal(horizontalFov, width, height);
		Projection = Matrix4x4.CreatePerspectiveFieldOfView(
			VerticalFOV,
			width / height,
			nearPlane,
			farPlane
		);
		ViewportSize = new Vector2(width, height);
		Near = nearPlane;
		Far = farPlane;
	}

	public void SetPerspective(
		Vector2 viewport,
		float horizontalFov = 1.5708f,
		float nearPlane = 1,
		float farPlane = 7500
	)
	{
		SetPerspective(
			viewport.X,
			viewport.Y,
			horizontalFov,
			nearPlane,
			farPlane
		);
	}

	public void SetPerspectiveOffCenter(
		float left,
		float bottom,
		float right,
		float top,
		float nearPlane = 1,
		float farPlane = 10000
	)
	{
		Projection = Matrix4x4.CreatePerspectiveOffCenter(
			left,
			right,
			bottom,
			top,
			nearPlane,
			farPlane
		);
		ViewportSize = new Vector2(
			MathF.Abs(left - right),
			MathF.Abs(bottom - top)
		);
		Near = nearPlane;
		Far = farPlane;
	}

	public void LookAt(Vector3 target)
	{
		Matrix4x4 viewLookAt = Matrix4x4.CreateLookAt(
			Position,
			target,
			CameraUpNormal
		);

		if (!Matrix4x4.Invert(viewLookAt, out Matrix4x4 worldLookAt))
		{
			throw new InvalidOperationException("The look-at transform is not invertible.");
		}

		if (!Matrix4x4.Decompose(
			worldLookAt,
			out _,
			out Quaternion worldRotation,
			out Vector3 worldTranslation
		))
		{
			throw new InvalidOperationException("The look-at transform cannot be decomposed.");
		}

		Position = worldTranslation;
		Rotation = worldRotation;
		Rotation.Deconstruct(out pitch, out yaw, out _);
		PerformClamps();
	}

	public Vector3 ToWorld(Vector3 value)
	{
		return Vector4.Transform(new Vector4(value, 0), World).XYZ();
	}

	public Vector3 ToWorldNormal(Vector3 value)
	{
		Vector3 result = ToWorld(value);

		if (result == Vector3.Zero)
		{
			return Vector3.Zero;
		}

		return Vector3.Normalize(result);
	}

	public void Update(Vector2 mouseDelta)
	{
		const float mouseScale = 1f / 5f;
		const float maximumAngle = 360;

		if (!MouseMovement || mouseDelta == Vector2.Zero)
		{
			return;
		}

		yaw -= mouseDelta.X * mouseScale;

		while (yaw > maximumAngle)
		{
			yaw -= maximumAngle;
		}

		while (yaw < 0)
		{
			yaw += maximumAngle;
		}

		pitch -= mouseDelta.Y * mouseScale;
		PerformClamps();
	}

	public static Camera Create(Func<Camera> factory)
	{
		ArgumentNullException.ThrowIfNull(factory);

		return factory();
	}

	public static float VerticalFOVFromHorizontal(
		float horizontalFov,
		float width,
		float height
	)
	{
		return 2 * MathF.Atan(MathF.Tan(horizontalFov / 2) * height / width);
	}

	public static float HorizontalFOVFromVertical(
		float verticalFov,
		float width,
		float height
	)
	{
		return 2 * MathF.Atan(MathF.Tan(verticalFov / 2) * width / height);
	}

	public static Matrix4x4 CreateModel(
		Vector3 position,
		Vector3 scale,
		Quaternion rotation
	)
	{
		return Matrix4x4.CreateScale(scale)
			* Matrix4x4.CreateFromQuaternion(rotation)
			* Matrix4x4.CreateTranslation(position);
	}

	private void Refresh()
	{
		if (!dirty)
		{
			return;
		}

		dirty = false;
		world = CreateModel(position, Vector3.One, rotation);
		worldForwardNormal = ToWorldNormal(ForwardNormal);
		worldRightNormal = ToWorldNormal(RightNormal);
		worldUpNormal = ToWorldNormal(UpNormal);

		if (!Matrix4x4.Invert(world, out view))
		{
			throw new InvalidOperationException("The camera world matrix is not invertible.");
		}
	}

	private void PerformClamps()
	{
		pitch = pitch.Clamp(PitchClamp.X, PitchClamp.Y);
		float degreesToRadians = MathF.PI / 180;
		rotation = Quaternion.CreateFromYawPitchRoll(
			yaw * degreesToRadians,
			pitch * degreesToRadians,
			0
		);
		dirty = true;
	}
}
