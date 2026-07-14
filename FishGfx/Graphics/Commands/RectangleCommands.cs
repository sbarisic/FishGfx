using System;
using System.Numerics;

namespace FishGfx.Graphics;

public sealed class RectangleCommand : RenderCommand
{
	public RectangleCommand(
		float x,
		float y,
		float width,
		float height,
		float thickness = 1,
		Color? color = null
	)
	{
		X = x;
		Y = y;
		Width = width;
		Height = height;
		Thickness = thickness;
		Color = color;
	}

	public float X { get; }

	public float Y { get; }

	public float Width { get; }

	public float Height { get; }

	public float Thickness { get; }

	public Color? Color { get; }

	public override void Execute(RenderPass pass)
	{
		ArgumentNullException.ThrowIfNull(pass);
		pass.DrawRectangle(X, Y, Width, Height, Thickness, Color);
	}
}

public sealed class FillRectangleCommand : RenderCommand
{
	public FillRectangleCommand(float x, float y, float width, float height, Color? color = null)
	{
		X = x;
		Y = y;
		Width = width;
		Height = height;
		Color = color;
	}

	public float X { get; }

	public float Y { get; }

	public float Width { get; }

	public float Height { get; }

	public Color? Color { get; }

	public override void Execute(RenderPass pass)
	{
		ArgumentNullException.ThrowIfNull(pass);
		pass.FillRectangle(X, Y, Width, Height, Color);
	}
}

public sealed class TexturedRectangleCommand : RenderCommand
{
	public TexturedRectangleCommand(
		float x,
		float y,
		float width,
		float height,
		float u0 = 0,
		float v0 = 0,
		float u1 = 1,
		float v1 = 1,
		Color? color = null,
		Texture texture = null,
		ShaderProgram shader = null
	)
	{
		X = x;
		Y = y;
		Width = width;
		Height = height;
		U0 = u0;
		V0 = v0;
		U1 = u1;
		V1 = v1;
		Color = color;
		Texture = texture;
		Shader = shader;
	}

	public float X { get; }

	public float Y { get; }

	public float Width { get; }

	public float Height { get; }

	public float U0 { get; }

	public float V0 { get; }

	public float U1 { get; }

	public float V1 { get; }

	public Color? Color { get; }

	public Texture Texture { get; }

	public ShaderProgram Shader { get; }

	public override void Execute(RenderPass pass)
	{
		ArgumentNullException.ThrowIfNull(pass);
		pass.DrawTexturedRectangle(
			X,
			Y,
			Width,
			Height,
			U0,
			V0,
			U1,
			V1,
			Color,
			Texture,
			Shader
		);
	}
}

public sealed class NinePatchCommand : RenderCommand
{
	public NinePatchCommand(
		Vector2 position,
		Vector2 size,
		Texture texture,
		NinePatchInsets insets,
		Color? color = null,
		ShaderProgram shader = null
	)
	{
		Texture = texture ?? throw new ArgumentNullException(nameof(texture));
		Position = position;
		Size = size;
		Insets = insets;
		Color = color;
		Shader = shader;
	}

	public Vector2 Position { get; }

	public Vector2 Size { get; }

	public Texture Texture { get; }

	public NinePatchInsets Insets { get; }

	public Color? Color { get; }

	public ShaderProgram Shader { get; }

	public override void Execute(RenderPass pass)
	{
		ArgumentNullException.ThrowIfNull(pass);
		pass.DrawNinePatch(Position, Size, Texture, Insets, Color, Shader);
	}
}

public sealed class RoundedRectangleCommand : RenderCommand
{
	public RoundedRectangleCommand(
		Vector2 position,
		Vector2 size,
		CornerRadii radii,
		float thickness = 1,
		Color? color = null,
		int cornerSegments = 0
	)
	{
		Position = position;
		Size = size;
		Radii = radii;
		Thickness = thickness;
		Color = color;
		CornerSegments = cornerSegments;
	}

	public Vector2 Position { get; }

	public Vector2 Size { get; }

	public CornerRadii Radii { get; }

	public float Thickness { get; }

	public Color? Color { get; }

	public int CornerSegments { get; }

	public override void Execute(RenderPass pass)
	{
		ArgumentNullException.ThrowIfNull(pass);
		pass.DrawRoundedRectangle(Position, Size, Radii, Thickness, Color, CornerSegments);
	}
}

public sealed class FillRoundedRectangleCommand : RenderCommand
{
	public FillRoundedRectangleCommand(
		Vector2 position,
		Vector2 size,
		CornerRadii radii,
		Color? color = null,
		int cornerSegments = 0
	)
	{
		Position = position;
		Size = size;
		Radii = radii;
		Color = color;
		CornerSegments = cornerSegments;
	}

	public Vector2 Position { get; }

	public Vector2 Size { get; }

	public CornerRadii Radii { get; }

	public Color? Color { get; }

	public int CornerSegments { get; }

	public override void Execute(RenderPass pass)
	{
		ArgumentNullException.ThrowIfNull(pass);
		pass.FillRoundedRectangle(Position, Size, Radii, Color, CornerSegments);
	}
}

public sealed class TexturedRoundedRectangleCommand : RenderCommand
{
	public TexturedRoundedRectangleCommand(
		Vector2 position,
		Vector2 size,
		CornerRadii radii,
		Texture texture,
		Vector2 uvMinimum,
		Vector2 uvMaximum,
		Color? color = null,
		ShaderProgram shader = null,
		int cornerSegments = 0
	)
	{
		Texture = texture ?? throw new ArgumentNullException(nameof(texture));
		Position = position;
		Size = size;
		Radii = radii;
		UvMinimum = uvMinimum;
		UvMaximum = uvMaximum;
		Color = color;
		Shader = shader;
		CornerSegments = cornerSegments;
	}

	public Vector2 Position { get; }

	public Vector2 Size { get; }

	public CornerRadii Radii { get; }

	public Texture Texture { get; }

	public Vector2 UvMinimum { get; }

	public Vector2 UvMaximum { get; }

	public Color? Color { get; }

	public ShaderProgram Shader { get; }

	public int CornerSegments { get; }

	public override void Execute(RenderPass pass)
	{
		ArgumentNullException.ThrowIfNull(pass);
		pass.DrawTexturedRoundedRectangle(
			Position,
			Size,
			Radii,
			Texture,
			UvMinimum,
			UvMaximum,
			Color,
			Shader,
			CornerSegments
		);
	}
}
