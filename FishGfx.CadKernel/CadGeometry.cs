using System.Numerics;
using System.Text.Json.Serialization;

namespace FishGfx.Cad;

public readonly record struct CadPoint3(double X, double Y, double Z)
{
	public static CadPoint3 Zero => default;

	public double Length => Math.Sqrt(LengthSquared);

	public double LengthSquared => X * X + Y * Y + Z * Z;

	public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z);

	public CadPoint3 Normalized()
	{
		double length = Length;

		if (!double.IsFinite(length) || length <= 1e-12)
		{
			throw new InvalidOperationException("A zero or non-finite CAD vector cannot be normalized.");
		}

		return this / length;
	}

	public Vector3 ToVector3()
	{
		return new Vector3((float)X, (float)Y, (float)Z);
	}

	public static CadPoint3 FromVector3(Vector3 value)
	{
		return new CadPoint3(value.X, value.Y, value.Z);
	}

	public static double Dot(CadPoint3 left, CadPoint3 right)
	{
		return left.X * right.X + left.Y * right.Y + left.Z * right.Z;
	}

	public static CadPoint3 Cross(CadPoint3 left, CadPoint3 right)
	{
		return new CadPoint3(
			left.Y * right.Z - left.Z * right.Y,
			left.Z * right.X - left.X * right.Z,
			left.X * right.Y - left.Y * right.X
		);
	}

	public static CadPoint3 Lerp(CadPoint3 start, CadPoint3 end, double amount)
	{
		return start + (end - start) * amount;
	}

	public static CadPoint3 operator +(CadPoint3 left, CadPoint3 right)
	{
		return new CadPoint3(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
	}

	public static CadPoint3 operator -(CadPoint3 left, CadPoint3 right)
	{
		return new CadPoint3(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
	}

	public static CadPoint3 operator -(CadPoint3 value)
	{
		return new CadPoint3(-value.X, -value.Y, -value.Z);
	}

	public static CadPoint3 operator *(CadPoint3 value, double scale)
	{
		return new CadPoint3(value.X * scale, value.Y * scale, value.Z * scale);
	}

	public static CadPoint3 operator *(double scale, CadPoint3 value)
	{
		return value * scale;
	}

	public static CadPoint3 operator /(CadPoint3 value, double scale)
	{
		return new CadPoint3(value.X / scale, value.Y / scale, value.Z / scale);
	}
}

public readonly record struct CadQuaternion(double X, double Y, double Z, double W)
{
	public static CadQuaternion Identity => new(0, 0, 0, 1);

	public bool IsFinite => double.IsFinite(X)
		&& double.IsFinite(Y)
		&& double.IsFinite(Z)
		&& double.IsFinite(W);

	public CadQuaternion Normalized()
	{
		double length = Math.Sqrt(X * X + Y * Y + Z * Z + W * W);

		if (!double.IsFinite(length) || length <= 1e-12)
		{
			throw new InvalidOperationException("A zero or non-finite CAD quaternion cannot be normalized.");
		}

		return new CadQuaternion(X / length, Y / length, Z / length, W / length);
	}

	public static CadQuaternion FromEulerDegrees(CadPoint3 euler)
	{
		double radians = Math.PI / 180;
		Quaternion value = Quaternion.CreateFromYawPitchRoll(
			(float)(euler.Y * radians),
			(float)(euler.X * radians),
			(float)(euler.Z * radians)
		);

		return new CadQuaternion(value.X, value.Y, value.Z, value.W).Normalized();
	}

	public CadPoint3 Rotate(CadPoint3 value)
	{
		CadQuaternion rotation = Normalized();
		CadPoint3 axis = new(rotation.X, rotation.Y, rotation.Z);
		CadPoint3 tangent = 2 * CadPoint3.Cross(axis, value);

		return value + rotation.W * tangent + CadPoint3.Cross(axis, tangent);
	}
}

public readonly record struct CadTransform(CadPoint3 Translation, CadQuaternion Rotation)
{
	public static CadTransform Identity => new(CadPoint3.Zero, CadQuaternion.Identity);

	public bool IsFinite => Translation.IsFinite && Rotation.IsFinite;

	public CadPoint3 TransformPoint(CadPoint3 point)
	{
		return Rotation.Rotate(point) + Translation;
	}

	public CadPoint3 TransformDirection(CadPoint3 direction)
	{
		return Rotation.Rotate(direction);
	}
}

public readonly record struct CadFrame
{
	[JsonConstructor]
	public CadFrame(CadPoint3 origin, CadPoint3 tangent, CadPoint3 normal)
	{
		if (!origin.IsFinite || !tangent.IsFinite || !normal.IsFinite)
		{
			throw new ArgumentException("CAD frames require finite coordinates.");
		}

		Origin = origin;
		Tangent = tangent.Normalized();
		CadPoint3 rejected = normal - CadPoint3.Dot(normal, Tangent) * Tangent;

		if (rejected.LengthSquared <= 1e-12)
		{
			rejected = Perpendicular(Tangent);
		}

		Normal = rejected.Normalized();
		Binormal = CadPoint3.Cross(Tangent, Normal).Normalized();
	}

	public CadPoint3 Origin { get; }

	public CadPoint3 Tangent { get; }

	public CadPoint3 Normal { get; }

	public CadPoint3 Binormal { get; }

	public CadFrame Transformed(CadTransform transform)
	{
		return new CadFrame(
			transform.TransformPoint(Origin),
			transform.TransformDirection(Tangent),
			transform.TransformDirection(Normal)
		);
	}

	public CadFrame Flipped()
	{
		return new CadFrame(Origin, -Tangent, Normal);
	}

	public static CadPoint3 RotateAround(CadPoint3 value, CadPoint3 axis, double radians)
	{
		CadPoint3 normalizedAxis = axis.Normalized();
		double cosine = Math.Cos(radians);
		double sine = Math.Sin(radians);

		return value * cosine
			+ CadPoint3.Cross(normalizedAxis, value) * sine
			+ normalizedAxis * CadPoint3.Dot(normalizedAxis, value) * (1 - cosine);
	}

	private static CadPoint3 Perpendicular(CadPoint3 tangent)
	{
		CadPoint3 reference = Math.Abs(tangent.Z) < 0.9
			? new CadPoint3(0, 0, 1)
			: new CadPoint3(0, 1, 0);

		return CadPoint3.Cross(reference, tangent).Normalized();
	}
}
