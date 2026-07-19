using System;
using Silk.NET.OpenGL;

namespace FishGfx.Graphics;

internal sealed class OpenGlRenderStateApplier
{
	private RenderState? appliedState;

	internal void Apply(RenderState state)
	{
		state.Validate();

		RenderState previous = appliedState.GetValueOrDefault();
		bool first = !appliedState.HasValue;

		ApplyCullState(previous, state, first);
		ApplyDepthState(previous, state, first);
		ApplyColorState(previous, state, first);
		ApplyScissorState(previous, state, first);
		ApplyStencilState(previous, state, first);
		ApplyBlendState(previous, state, first);
		ApplyRasterState(previous, state, first);

		appliedState = state;
	}

	internal void Invalidate()
	{
		appliedState = null;
	}

	private static void ApplyCullState(RenderState previous, RenderState state, bool first)
	{
		bool enabled = state.CullMode != CullMode.None;
		bool wasEnabled = previous.CullMode != CullMode.None;

		if (first || enabled != wasEnabled)
		{
			SetEnabled(EnableCap.CullFace, enabled);
		}

		if (enabled && (first || !wasEnabled || previous.CullMode != state.CullMode))
		{
			Internal_OpenGL.GL.CullFace(ToOpenGl(state.CullMode));
		}

		if (first || previous.Winding != state.Winding)
		{
			Internal_OpenGL.GL.FrontFace(ToOpenGl(state.Winding));
		}
	}

	private static void ApplyDepthState(RenderState previous, RenderState state, bool first)
	{
		if (first || previous.DepthTestEnabled != state.DepthTestEnabled)
		{
			SetEnabled(EnableCap.DepthTest, state.DepthTestEnabled);
		}

		if (state.DepthTestEnabled
			&& (first || !previous.DepthTestEnabled || previous.DepthCompare != state.DepthCompare))
		{
			Internal_OpenGL.GL.DepthFunc(ToOpenGlCompare(state.DepthCompare));
		}

		if (first || previous.DepthWriteEnabled != state.DepthWriteEnabled)
		{
			Internal_OpenGL.GL.DepthMask(state.DepthWriteEnabled);
		}
	}

	private static void ApplyColorState(RenderState previous, RenderState state, bool first)
	{
		if (!first && previous.ColorWriteMask == state.ColorWriteMask)
		{
			return;
		}

		Internal_OpenGL.GL.ColorMask(
			(state.ColorWriteMask & ColorWriteMask.Red) != 0,
			(state.ColorWriteMask & ColorWriteMask.Green) != 0,
			(state.ColorWriteMask & ColorWriteMask.Blue) != 0,
			(state.ColorWriteMask & ColorWriteMask.Alpha) != 0
		);
	}

	private static void ApplyScissorState(RenderState previous, RenderState state, bool first)
	{
		bool enabled = state.ScissorRectangle.HasValue;
		bool wasEnabled = previous.ScissorRectangle.HasValue;

		if (first || enabled != wasEnabled)
		{
			SetEnabled(EnableCap.ScissorTest, enabled);
		}

		if (!enabled || (!first && state.ScissorRectangle == previous.ScissorRectangle))
		{
			return;
		}

		AxisAlignedBoundingBox rectangle = state.ScissorRectangle.Value;
		Internal_OpenGL.GL.Scissor(
			(int)rectangle.Min.X,
			(int)rectangle.Min.Y,
			(uint)rectangle.Size.X,
			(uint)rectangle.Size.Y
		);
	}

	private static void ApplyStencilState(RenderState previous, RenderState state, bool first)
	{
		bool enabled = state.Stencil.HasValue;
		bool wasEnabled = previous.Stencil.HasValue;

		if (first || enabled != wasEnabled)
		{
			SetEnabled(EnableCap.StencilTest, enabled);
		}

		if (!enabled || (!first && state.Stencil == previous.Stencil))
		{
			return;
		}

		StencilState stencil = state.Stencil.Value;
		ApplyStencilFace(TriangleFace.Front, stencil.Front);
		ApplyStencilFace(TriangleFace.Back, stencil.Back);
	}

	private static void ApplyStencilFace(TriangleFace face, StencilFaceState state)
	{
		Internal_OpenGL.GL.StencilMaskSeparate(face, state.WriteMask);
		Internal_OpenGL.GL.StencilFuncSeparate(
			face,
			(GLEnum)ToOpenGlCompare(state.Function),
			state.Reference,
			state.CompareMask
		);
		Internal_OpenGL.GL.StencilOpSeparate(
			face,
			ToOpenGl(state.StencilFail),
			ToOpenGl(state.DepthFail),
			ToOpenGl(state.Pass)
		);
	}

	private static void ApplyBlendState(RenderState previous, RenderState state, bool first)
	{
		if (first || previous.BlendEnabled != state.BlendEnabled)
		{
			SetEnabled(EnableCap.Blend, state.BlendEnabled);
		}

		if (state.BlendEnabled
			&& (first
				|| !previous.BlendEnabled
				|| previous.SourceBlend != state.SourceBlend
				|| previous.DestinationBlend != state.DestinationBlend))
		{
			Internal_OpenGL.GL.BlendFunc(
				ToOpenGl(state.SourceBlend),
				ToOpenGl(state.DestinationBlend)
			);
		}
	}

	private static void ApplyRasterState(RenderState previous, RenderState state, bool first)
	{
		if (first || previous.PointSize != state.PointSize)
		{
			Internal_OpenGL.GL.PointSize(state.PointSize);
		}

		if (first || previous.DepthClampEnabled != state.DepthClampEnabled)
		{
			SetEnabled((EnableCap)0x864F, state.DepthClampEnabled);
		}

		bool depthBiasEnabled = state.DepthBiasSlope != 0 || state.DepthBiasConstant != 0;
		bool depthBiasWasEnabled = previous.DepthBiasSlope != 0 || previous.DepthBiasConstant != 0;

		if (first || depthBiasEnabled != depthBiasWasEnabled)
		{
			SetEnabled(EnableCap.PolygonOffsetFill, depthBiasEnabled);
		}

		if (depthBiasEnabled
			&& (first
				|| !depthBiasWasEnabled
				|| previous.DepthBiasSlope != state.DepthBiasSlope
				|| previous.DepthBiasConstant != state.DepthBiasConstant))
		{
			Internal_OpenGL.GL.PolygonOffset(
				state.DepthBiasSlope,
				state.DepthBiasConstant
			);
		}
	}

	private static void SetEnabled(EnableCap capability, bool enabled)
	{
		if (enabled)
		{
			Internal_OpenGL.GL.Enable(capability);
		}
		else
		{
			Internal_OpenGL.GL.Disable(capability);
		}
	}

	private static TriangleFace ToOpenGl(CullMode mode)
	{
		return mode switch
		{
			CullMode.Front => TriangleFace.Front,
			CullMode.Back => TriangleFace.Back,
			CullMode.FrontAndBack => TriangleFace.FrontAndBack,
			_ => throw new ArgumentOutOfRangeException(nameof(mode)),
		};
	}

	private static DepthFunction ToOpenGlCompare(CompareFunction function)
	{
		return function switch
		{
			CompareFunction.Never => DepthFunction.Never,
			CompareFunction.Less => DepthFunction.Less,
			CompareFunction.Equal => DepthFunction.Equal,
			CompareFunction.LessOrEqual => DepthFunction.Lequal,
			CompareFunction.Greater => DepthFunction.Greater,
			CompareFunction.NotEqual => DepthFunction.Notequal,
			CompareFunction.GreaterOrEqual => DepthFunction.Gequal,
			CompareFunction.Always => DepthFunction.Always,
			_ => throw new ArgumentOutOfRangeException(nameof(function)),
		};
	}

	private static GLEnum ToOpenGl(StencilOperation operation)
	{
		return operation switch
		{
			StencilOperation.Zero => GLEnum.Zero,
			StencilOperation.Invert => GLEnum.Invert,
			StencilOperation.Keep => GLEnum.Keep,
			StencilOperation.Replace => GLEnum.Replace,
			StencilOperation.Increment => GLEnum.Incr,
			StencilOperation.Decrement => GLEnum.Decr,
			StencilOperation.IncrementWrap => GLEnum.IncrWrap,
			StencilOperation.DecrementWrap => GLEnum.DecrWrap,
			_ => throw new ArgumentOutOfRangeException(nameof(operation)),
		};
	}

	private static FrontFaceDirection ToOpenGl(Winding winding)
	{
		return winding switch
		{
			Winding.Clockwise => FrontFaceDirection.CW,
			Winding.CounterClockwise => FrontFaceDirection.Ccw,
			_ => throw new ArgumentOutOfRangeException(nameof(winding)),
		};
	}

	private static BlendingFactor ToOpenGl(BlendFactor factor)
	{
		return factor switch
		{
			BlendFactor.Zero => BlendingFactor.Zero,
			BlendFactor.One => BlendingFactor.One,
			BlendFactor.SourceColor => BlendingFactor.SrcColor,
			BlendFactor.OneMinusSourceColor => BlendingFactor.OneMinusSrcColor,
			BlendFactor.SourceAlpha => BlendingFactor.SrcAlpha,
			BlendFactor.OneMinusSourceAlpha => BlendingFactor.OneMinusSrcAlpha,
			BlendFactor.DestinationAlpha => BlendingFactor.DstAlpha,
			BlendFactor.OneMinusDestinationAlpha => BlendingFactor.OneMinusDstAlpha,
			BlendFactor.DestinationColor => BlendingFactor.DstColor,
			BlendFactor.OneMinusDestinationColor => BlendingFactor.OneMinusDstColor,
			BlendFactor.SourceAlphaSaturate => BlendingFactor.SrcAlphaSaturate,
			BlendFactor.ConstantColor => BlendingFactor.ConstantColor,
			BlendFactor.OneMinusConstantColor => BlendingFactor.OneMinusConstantColor,
			BlendFactor.ConstantAlpha => BlendingFactor.ConstantAlpha,
			BlendFactor.OneMinusConstantAlpha => BlendingFactor.OneMinusConstantAlpha,
			BlendFactor.Source1Alpha => BlendingFactor.Src1Alpha,
			BlendFactor.Source1Color => BlendingFactor.Src1Color,
			BlendFactor.OneMinusSource1Color => BlendingFactor.OneMinusSrc1Color,
			BlendFactor.OneMinusSource1Alpha => BlendingFactor.OneMinusSrc1Alpha,
			_ => throw new ArgumentOutOfRangeException(nameof(factor)),
		};
	}
}
