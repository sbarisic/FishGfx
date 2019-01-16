using OpenGL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FishGfx.Graphics {
	public enum CullFace {
		Front = CullFaceMode.Front,
		Back = CullFaceMode.Back,
		FrontAndBack = CullFaceMode.FrontAndBack,
	}

	public enum DepthFunc {
		Never = DepthFunction.Never,
		Less = DepthFunction.Less,
		Equal = DepthFunction.Equal,
		LessOrEqual = DepthFunction.Lequal,
		Greater = DepthFunction.Greater,
		NotEqual = DepthFunction.Notequal,
		GreaterOrEqual = DepthFunction.Gequal,
		Always = DepthFunction.Always
	}

	public enum FrontFace {
		Clockwise = FrontFaceDirection.Cw,
		CounterClockwise = FrontFaceDirection.Ccw
	}

	public enum BlendFactor {
		Zero = BlendingFactor.Zero,
		One = BlendingFactor.One,
		SrcColor = BlendingFactor.SrcColor,
		OneMinusSrcColor = BlendingFactor.OneMinusSrcColor,
		SrcAlpha = BlendingFactor.SrcAlpha,
		OneMinusSrcAlpha = BlendingFactor.OneMinusSrcAlpha,
		DstAlpha = BlendingFactor.DstAlpha,
		OneMinusDstAlpha = BlendingFactor.OneMinusDstAlpha,
		DstColor = BlendingFactor.DstColor,
		OneMinusDstColor = BlendingFactor.OneMinusDstColor,
		SrcAlphaSaturate = BlendingFactor.SrcAlphaSaturate,
		ConstantColor = BlendingFactor.ConstantColor,
		OneMinusConstantColor = BlendingFactor.OneMinusConstantColor,
		ConstantAlpha = BlendingFactor.ConstantAlpha,
		OneMinusConstantAlpha = BlendingFactor.OneMinusConstantAlpha,
		Source1Alpha = BlendingFactor.Source1Alpha,
		Src1Color = BlendingFactor.Src1Color,
		OneMinusSrc1Color = BlendingFactor.OneMinusSrc1Color,
		OneMinusSrc1Alpha = BlendingFactor.OneMinusSrc1Alpha
	}

	public struct RenderState {
		public CullFace CullFace;
		public DepthFunc DepthFunc;
		public FrontFace FrontFace;

		public BlendFactor BlendFunc_Src;
		public BlendFactor BlendFunc_Dst;

		public bool EnableCullFace;
		public bool EnableDepthTest;
		public bool EnableScissorTest;
		public bool EnableBlend;
		//public bool EnableTexture2d;

		public float PointSize;

		public AABB ScissorRegion;
	}
}
