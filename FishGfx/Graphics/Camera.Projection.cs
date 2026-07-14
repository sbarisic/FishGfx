using System;
using System.Numerics;

namespace FishGfx.Graphics;

public partial class Camera
{
	public Vector3 ProjectToViewport(
		Vector3 worldPosition,
		Matrix4x4 modelMatrix
	)
	{
		Vector4 clip = Vector4.Transform(
			new Vector4(worldPosition, 1),
			modelMatrix * View * Projection
		);

		if (MathF.Abs(clip.W) <= float.Epsilon)
		{
			throw new InvalidOperationException(
				"The projected point has an invalid homogeneous W component."
			);
		}

		Vector3 normalized = new Vector3(clip.X, clip.Y, clip.Z) / clip.W;

		return new Vector3(
			(normalized.X + 1) * 0.5f * ViewportSize.X,
			(1 - normalized.Y) * 0.5f * ViewportSize.Y,
			normalized.Z
		);
	}

	public Vector3 ProjectToViewport(Vector3 worldPosition)
	{
		return ProjectToViewport(worldPosition, Matrix4x4.Identity);
	}

	public bool TryUnproject(
		Vector3 screen,
		Matrix4x4 modelMatrix,
		out Vector3 worldPosition
	)
	{
		worldPosition = default;

		if (ViewportSize.X <= 0 || ViewportSize.Y <= 0)
		{
			return false;
		}

		if (!Matrix4x4.Invert(
			modelMatrix * View * Projection,
			out Matrix4x4 inverse
		))
		{
			return false;
		}

		Vector4 point = new(
			screen.X / ViewportSize.X * 2 - 1,
			1 - screen.Y / ViewportSize.Y * 2,
			screen.Z,
			1
		);
		point = Vector4.Transform(point, inverse);

		if (MathF.Abs(point.W) <= float.Epsilon)
		{
			return false;
		}

		worldPosition = new Vector3(point.X, point.Y, point.Z) / point.W;

		return true;
	}

	public bool TryUnproject(Vector3 screen, out Vector3 worldPosition)
	{
		return TryUnproject(
			screen,
			Matrix4x4.Identity,
			out worldPosition
		);
	}

	public PickingRay CreatePickingRay(
		Vector2 screen,
		Matrix4x4 modelMatrix
	)
	{
		bool hasNear = TryUnproject(
			new Vector3(screen, 0),
			modelMatrix,
			out Vector3 nearPoint
		);
		bool hasFar = TryUnproject(
			new Vector3(screen, 1),
			modelMatrix,
			out Vector3 farPoint
		);

		if (!hasNear || !hasFar)
		{
			throw new InvalidOperationException(
				"The camera transform cannot be unprojected."
			);
		}

		return new PickingRay(nearPoint, farPoint - nearPoint);
	}

	public PickingRay CreatePickingRay(Vector2 screen)
	{
		return CreatePickingRay(screen, Matrix4x4.Identity);
	}

	public Vector3 WorldToScreen(
		Vector3 worldPosition,
		Matrix4x4 modelMatrix
	)
	{
		return ProjectToViewport(worldPosition, modelMatrix);
	}

	public Vector3 WorldToScreen(Vector3 worldPosition)
	{
		return ProjectToViewport(worldPosition);
	}

	public Vector3 ScreenToWorld(
		Vector2 screen,
		Matrix4x4 modelMatrix
	)
	{
		if (!TryUnproject(
			new Vector3(screen, 0),
			modelMatrix,
			out Vector3 worldPosition
		))
		{
			throw new InvalidOperationException(
				"The camera transform cannot be unprojected."
			);
		}

		return worldPosition;
	}

	public Vector3 ScreenToWorld(Vector2 screen)
	{
		return ScreenToWorld(screen, Matrix4x4.Identity);
	}

	public Vector3 ScreenToWorldDirection(
		Vector2 screen,
		Matrix4x4 modelMatrix
	)
	{
		return CreatePickingRay(screen, modelMatrix).Direction;
	}

	public Vector3 ScreenToWorldDirection(Vector2 screen)
	{
		return CreatePickingRay(screen).Direction;
	}

	public Vector3[] GetFrustumPoints()
	{
		if (!Matrix4x4.Invert(View * Projection, out Matrix4x4 inverseClip))
		{
			throw new InvalidOperationException(
				"The camera view-projection matrix is not invertible."
			);
		}

		Vector4[] points =
		{
			Vector4.Transform(new Vector3(-1, -1, -1), inverseClip),
			Vector4.Transform(new Vector3(1, -1, -1), inverseClip),
			Vector4.Transform(new Vector3(1, 1, -1), inverseClip),
			Vector4.Transform(new Vector3(-1, 1, -1), inverseClip),
			Vector4.Transform(new Vector3(-1, -1, 1), inverseClip),
			Vector4.Transform(new Vector3(1, -1, 1), inverseClip),
			Vector4.Transform(new Vector3(1, 1, 1), inverseClip),
			Vector4.Transform(new Vector3(-1, 1, 1), inverseClip),
		};
		Vector3[] result = new Vector3[points.Length];

		for (int index = 0; index < result.Length; index++)
		{
			result[index] = points[index].XYZ() / points[index].W;
		}

		return result;
	}
}
