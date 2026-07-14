using System;
using System.Numerics;

namespace FishGfx;

public static class VectorExtensions
{
	public static Vector3 XYZ(this Vector4 value)
	{
		return new Vector3(value.X, value.Y, value.Z);
	}

	public static Vector2 XY(this Vector3 value)
	{
		return new Vector2(value.X, value.Y);
	}

	public static void GetEulerAngles(
		this Quaternion quaternion,
		out float pitch,
		out float yaw,
		out float roll
	)
	{
		if (quaternion.LengthSquared() == 0)
		{
			throw new ArgumentException("The quaternion cannot be zero.", nameof(quaternion));
		}

		Quaternion normalized = Quaternion.Normalize(quaternion);
		double sinPitch = 2 * (normalized.W * normalized.X - normalized.Y * normalized.Z);
		double sinYaw = 2 * (normalized.W * normalized.Y + normalized.X * normalized.Z);
		double cosYaw = 1 - 2 * (normalized.X * normalized.X + normalized.Y * normalized.Y);
		double sinRoll = 2 * (normalized.W * normalized.Z + normalized.X * normalized.Y);
		double cosRoll = 1 - 2 * (normalized.X * normalized.X + normalized.Z * normalized.Z);

		pitch = RadiansToDegrees(Math.Asin(Math.Clamp(sinPitch, -1, 1)));
		yaw = RadiansToDegrees(Math.Atan2(sinYaw, cosYaw));
		roll = RadiansToDegrees(Math.Atan2(sinRoll, cosRoll));
	}

	public static void Deconstruct(this Vector3 value, out float x, out float y, out float z)
	{
		x = value.X;
		y = value.Y;
		z = value.Z;
	}

	public static void Deconstruct(
		this Quaternion quaternion,
		out float pitch,
		out float yaw,
		out float roll
	)
	{
		quaternion.GetEulerAngles(out pitch, out yaw, out roll);
	}

	public static void Deconstruct(
		this Quaternion quaternion,
		out float w,
		out float x,
		out float y,
		out float z
	)
	{
		w = quaternion.W;
		x = quaternion.X;
		y = quaternion.Y;
		z = quaternion.Z;
	}

	private static float RadiansToDegrees(double radians)
	{
		return (float)(radians * 180 / Math.PI);
	}
}
