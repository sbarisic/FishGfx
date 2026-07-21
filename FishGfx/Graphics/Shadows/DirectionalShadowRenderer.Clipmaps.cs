using System;
using System.Numerics;

namespace FishGfx.Graphics.Shadows;

public sealed partial class DirectionalShadowRenderer
{
	public static float[] CalculateSplits(float nearDistance, float farDistance, int count, float lambda)
	{
		if (count <= 0)
		{
			return Array.Empty<float>();
		}

		nearDistance = Math.Max(0.01f, nearDistance);
		farDistance = Math.Max(nearDistance + 0.01f, farDistance);
		float[] splits = new float[count];
		FillSplits(nearDistance, farDistance, count, lambda, splits);
		return splits;
	}

	private static void FillSplits(
		float nearDistance,
		float farDistance,
		int count,
		float lambda,
		Span<float> splits)
	{
		if (count <= 0)
			return;
		if (splits.Length < count)
			throw new ArgumentException("The split destination is too small.", nameof(splits));
		nearDistance = Math.Max(0.01f, nearDistance);
		farDistance = Math.Max(nearDistance + 0.01f, farDistance);

		for (int index = 1; index <= count; index++)
		{
			float ratio = (float)index / count;
			float logarithmic = nearDistance * MathF.Pow(farDistance / nearDistance, ratio);
			float uniform = nearDistance + (farDistance - nearDistance) * ratio;
			splits[index - 1] = uniform + (logarithmic - uniform) * lambda;
		}

		splits[count - 1] = farDistance;
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

}

