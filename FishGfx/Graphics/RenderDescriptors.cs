using System;
using System.Numerics;

namespace FishGfx.Graphics;

public readonly record struct RenderView
{
	public RenderView(
		Matrix4x4 view,
		Matrix4x4 projection,
		Vector3 position,
		Vector2 viewportSize,
		float near,
		float far
	)
	{
		if (!float.IsFinite(viewportSize.X)
			|| !float.IsFinite(viewportSize.Y)
			|| viewportSize.X < 0
			|| viewportSize.Y < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(viewportSize));
		}

		if (!float.IsFinite(near) || !float.IsFinite(far))
		{
			throw new ArgumentOutOfRangeException(nameof(near));
		}

		View = view;
		Projection = projection;
		Position = position;
		ViewportSize = viewportSize;
		Near = near;
		Far = far;
	}

	public RenderView(Camera camera)
		: this(
			(camera ?? throw new ArgumentNullException(nameof(camera))).View,
			camera.Projection,
			camera.Position,
			camera.ViewportSize,
			camera.Near,
			camera.Far
		)
	{
	}

	public Matrix4x4 View { get; }

	public Matrix4x4 Projection { get; }

	public Vector3 Position { get; }

	public Vector2 ViewportSize { get; }

	public float Near { get; }

	public float Far { get; }
}

public enum RenderLoadAction
{
	Load,
	Clear,
	DontCare,
}

public sealed class RenderPassDescriptor
{
	private float time;

	public RenderView View { get; init; }

	public RenderState State { get; init; } = RenderState.Default;

	public RenderLoadAction ColorLoadAction { get; init; } = RenderLoadAction.Load;

	public RenderLoadAction DepthLoadAction { get; init; } = RenderLoadAction.Load;

	public RenderLoadAction StencilLoadAction { get; init; } = RenderLoadAction.Load;

	public Color ClearColor { get; init; } = Color.Black;

	public float ClearDepth { get; init; } = 1;

	public int ClearStencil { get; init; }

	public float Time
	{
		get => time;
		init
		{
			if (!float.IsFinite(value))
			{
				throw new ArgumentOutOfRangeException(nameof(Time));
			}

			time = value;
		}
	}
}
