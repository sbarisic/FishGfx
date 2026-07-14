using System;
using System.Numerics;

namespace FishGfx.Graphics;

/// <summary>
/// Six normalized view-frustum planes extracted from a FishGfx camera.
/// </summary>
public readonly struct ViewFrustum
{
	private readonly Plane left;
	private readonly Plane right;
	private readonly Plane bottom;
	private readonly Plane top;
	private readonly Plane near;
	private readonly Plane far;

	private ViewFrustum(Plane left, Plane right, Plane bottom, Plane top, Plane near, Plane far)
	{
		this.left = Normalize(left);
		this.right = Normalize(right);
		this.bottom = Normalize(bottom);
		this.top = Normalize(top);
		this.near = Normalize(near);
		this.far = Normalize(far);
	}

	public static ViewFrustum FromCamera(Camera camera)
	{
		ArgumentNullException.ThrowIfNull(camera);

		return FromMatrix(camera.View * camera.Projection);
	}

	public static ViewFrustum FromMatrix(Matrix4x4 matrix)
	{
		return new ViewFrustum(
			new Plane(matrix.M14 + matrix.M11, matrix.M24 + matrix.M21, matrix.M34 + matrix.M31, matrix.M44 + matrix.M41),
			new Plane(matrix.M14 - matrix.M11, matrix.M24 - matrix.M21, matrix.M34 - matrix.M31, matrix.M44 - matrix.M41),
			new Plane(matrix.M14 + matrix.M12, matrix.M24 + matrix.M22, matrix.M34 + matrix.M32, matrix.M44 + matrix.M42),
			new Plane(matrix.M14 - matrix.M12, matrix.M24 - matrix.M22, matrix.M34 - matrix.M32, matrix.M44 - matrix.M42),
			new Plane(matrix.M13, matrix.M23, matrix.M33, matrix.M43),
			new Plane(matrix.M14 - matrix.M13, matrix.M24 - matrix.M23, matrix.M34 - matrix.M33, matrix.M44 - matrix.M43)
		);
	}

	public bool Intersects(AxisAlignedBoundingBox bounds)
	{
		if (bounds.IsEmpty)
		{
			return false;
		}

		return IsInside(left, bounds)
			&& IsInside(right, bounds)
			&& IsInside(bottom, bounds)
			&& IsInside(top, bounds)
			&& IsInside(near, bounds)
			&& IsInside(far, bounds);
	}

	private static bool IsInside(Plane plane, AxisAlignedBoundingBox bounds)
	{
		Vector3 positive = new Vector3(
			plane.Normal.X >= 0 ? bounds.Max.X : bounds.Min.X,
			plane.Normal.Y >= 0 ? bounds.Max.Y : bounds.Min.Y,
			plane.Normal.Z >= 0 ? bounds.Max.Z : bounds.Min.Z
		);

		return Plane.DotCoordinate(plane, positive) >= 0;
	}

	private static Plane Normalize(Plane plane)
	{
		float length = plane.Normal.Length();

		if (!float.IsFinite(length) || length <= 0)
		{
			throw new ArgumentException("The supplied matrix does not produce a valid frustum.");
		}

		return new Plane(plane.Normal / length, plane.D / length);
	}
}
