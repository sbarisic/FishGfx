using System;

namespace FishGfx.Graphics;

public enum CullMode
{
	None,
	Front,
	Back,
	FrontAndBack,
}

public enum CompareFunction
{
	Never,
	Less,
	Equal,
	LessOrEqual,
	Greater,
	NotEqual,
	GreaterOrEqual,
	Always,
}

public enum Winding
{
	Clockwise,
	CounterClockwise,
}

public enum BlendFactor
{
	Zero,
	One,
	SourceColor,
	OneMinusSourceColor,
	SourceAlpha,
	OneMinusSourceAlpha,
	DestinationAlpha,
	OneMinusDestinationAlpha,
	DestinationColor,
	OneMinusDestinationColor,
	SourceAlphaSaturate,
	ConstantColor,
	OneMinusConstantColor,
	ConstantAlpha,
	OneMinusConstantAlpha,
	Source1Alpha,
	Source1Color,
	OneMinusSource1Color,
	OneMinusSource1Alpha,
}

[Flags]
public enum ColorWriteMask
{
	None = 0,
	Red = 1 << 0,
	Green = 1 << 1,
	Blue = 1 << 2,
	Alpha = 1 << 3,
	All = Red | Green | Blue | Alpha,
}

public enum StencilOperation
{
	Zero,
	Invert,
	Keep,
	Replace,
	Increment,
	Decrement,
	IncrementWrap,
	DecrementWrap,
}

public readonly record struct StencilFaceState
{
	public CompareFunction Function { get; init; }

	public int Reference { get; init; }

	public uint CompareMask { get; init; }

	public uint WriteMask { get; init; }

	public StencilOperation StencilFail { get; init; }

	public StencilOperation DepthFail { get; init; }

	public StencilOperation Pass { get; init; }

	public static StencilFaceState Default => new StencilFaceState
	{
		Function = CompareFunction.Always,
		CompareMask = uint.MaxValue,
		WriteMask = uint.MaxValue,
		StencilFail = StencilOperation.Keep,
		DepthFail = StencilOperation.Keep,
		Pass = StencilOperation.Keep,
	};
}

public readonly record struct StencilState
{
	public StencilFaceState Front { get; init; }

	public StencilFaceState Back { get; init; }

	public static StencilState Default => new StencilState
	{
		Front = StencilFaceState.Default,
		Back = StencilFaceState.Default,
	};
}

public readonly record struct RenderState
{
	public CullMode CullMode { get; init; }

	public CompareFunction DepthCompare { get; init; }

	public Winding Winding { get; init; }

	public BlendFactor SourceBlend { get; init; }

	public BlendFactor DestinationBlend { get; init; }

	public bool DepthTestEnabled { get; init; }

	public bool DepthWriteEnabled { get; init; }

	public bool BlendEnabled { get; init; }

	public bool DepthClampEnabled { get; init; }

	public ColorWriteMask ColorWriteMask { get; init; }

	public float PointSize { get; init; }

	public AxisAlignedBoundingBox? ScissorRectangle { get; init; }

	public StencilState? Stencil { get; init; }

	public static RenderState Default => new RenderState
	{
		CullMode = CullMode.Back,
		DepthCompare = CompareFunction.Less,
		Winding = Winding.Clockwise,
		SourceBlend = BlendFactor.SourceAlpha,
		DestinationBlend = BlendFactor.OneMinusSourceAlpha,
		DepthTestEnabled = true,
		DepthWriteEnabled = true,
		BlendEnabled = true,
		DepthClampEnabled = true,
		ColorWriteMask = ColorWriteMask.All,
		PointSize = 1,
	};

	internal void Validate()
	{
		if (!Enum.IsDefined(CullMode))
		{
			throw new ArgumentOutOfRangeException(nameof(CullMode));
		}

		if (!Enum.IsDefined(DepthCompare))
		{
			throw new ArgumentOutOfRangeException(nameof(DepthCompare));
		}

		if (!Enum.IsDefined(Winding))
		{
			throw new ArgumentOutOfRangeException(nameof(Winding));
		}

		if (!Enum.IsDefined(SourceBlend) || !Enum.IsDefined(DestinationBlend))
		{
			throw new ArgumentOutOfRangeException(nameof(SourceBlend));
		}

		if ((ColorWriteMask & ~ColorWriteMask.All) != 0)
		{
			throw new ArgumentOutOfRangeException(nameof(ColorWriteMask));
		}

		if (!float.IsFinite(PointSize) || PointSize <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(PointSize));
		}

		if (ScissorRectangle is AxisAlignedBoundingBox scissor)
		{
			ValidateScissor(scissor);
		}

		if (Stencil is StencilState stencil)
		{
			ValidateStencilFace(stencil.Front);
			ValidateStencilFace(stencil.Back);
		}
	}

	private static void ValidateScissor(AxisAlignedBoundingBox scissor)
	{
		if (scissor.IsEmpty)
		{
			throw new ArgumentOutOfRangeException(nameof(ScissorRectangle), "The scissor rectangle cannot be empty.");
		}

		if (!float.IsFinite(scissor.Min.X)
			|| !float.IsFinite(scissor.Min.Y)
			|| !float.IsFinite(scissor.Max.X)
			|| !float.IsFinite(scissor.Max.Y))
		{
			throw new ArgumentOutOfRangeException(nameof(ScissorRectangle), "Scissor coordinates must be finite.");
		}
	}

	private static void ValidateStencilFace(StencilFaceState state)
	{
		if (!Enum.IsDefined(state.Function)
			|| !Enum.IsDefined(state.StencilFail)
			|| !Enum.IsDefined(state.DepthFail)
			|| !Enum.IsDefined(state.Pass))
		{
			throw new ArgumentOutOfRangeException(nameof(Stencil));
		}
	}
}
