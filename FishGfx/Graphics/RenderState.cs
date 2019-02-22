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

	public enum StencilFunction {
		Skip = -1,
		Never = 512,
		Less = 513,
		Equal = 514,
		Lequal = 515,
		Greater = 516,
		Notequal = 517,
		Gequal = 518,
		Always = 519
	}

	public enum StencilOperation {
		Skip = -1,
		Zero = Gl.ZERO,
		Invert = Gl.INVERT,
		Keep = Gl.KEEP,
		Replace = Gl.REPLACE,
		Incr = Gl.INCR,
		Decr = Gl.DECR,
		IncrWrap = Gl.INCR_WRAP,
		DecrWrap = Gl.DECR_WRAP,
	}

	public enum StencilFace {
		Front,
		Back
	}

	public struct RenderState {
		public CullFace CullFace;
		public DepthFunc DepthFunc;
		public FrontFace FrontFace;

		public BlendFactor BlendFunc_Src;
		public BlendFactor BlendFunc_Dst;

		//public uint StencilMask;

		public StencilFunction StencilFrontFunction;
		public int StencilFrontReference;
		public uint StencilFrontMask;

		public StencilFunction StencilBackFunction;
		public int StencilBackReference;
		public uint StencilBackMask;

		public StencilOperation StencilFrontSFail;
		public StencilOperation StencilFrontDPFail;
		public StencilOperation StencilFrontDPPass;

		public StencilOperation StencilBackSFail;
		public StencilOperation StencilBackDPFail;
		public StencilOperation StencilBackDPPass;

		public bool EnableCullFace;
		public bool EnableDepthTest;
		public bool EnableScissorTest;
		public bool EnableBlend;
		public bool EnableDepthClamp;
		public bool EnableStencilTest;

		/// <summary>
		/// True to enable writing to the depth buffer
		/// </summary>
		public bool EnableDepthMask;
		public bool EnableColorMaskR;
		public bool EnableColorMaskG;
		public bool EnableColorMaskB;
		public bool EnableColorMaskA;
		//public bool EnableTexture2d;

		public float PointSize;

		public AABB ScissorRegion;

		public void SetColorMask(bool R, bool G, bool B, bool A) {
			EnableColorMaskR = R;
			EnableColorMaskG = G;
			EnableColorMaskB = B;
			EnableColorMaskA = A;
		}

		public void SetColorMask(bool All) {
			SetColorMask(All, All, All, All);
		}

		public void StencilFunc(StencilFunction Func, int Ref, uint Mask) {
			StencilFrontFunction = StencilBackFunction = Func;
			StencilFrontReference = StencilBackReference = Ref;
			StencilFrontMask = StencilBackMask = Mask;
		}

		public void StencilOpSeparate(StencilFace F, StencilOperation SFail, StencilOperation DPFail, StencilOperation DPPass) {
			if (F == StencilFace.Front) {
				StencilFrontSFail = SFail;
				StencilFrontDPFail = DPFail;
				StencilFrontDPPass = DPPass;
			} else {
				StencilBackSFail = SFail;
				StencilBackDPFail = DPFail;
				StencilBackDPPass = DPPass;
			}
		}

		public void StencilOp(StencilOperation SFail, StencilOperation DPFail, StencilOperation DPPass) {
			StencilOpSeparate(StencilFace.Front, SFail, DPFail, DPPass);
			StencilOpSeparate(StencilFace.Back, SFail, DPFail, DPPass);
		}

		public void BlendFunc(BlendFactor Src, BlendFactor Dst) {
			BlendFunc_Src = Src;
			BlendFunc_Dst = Dst;
		}
	}
}
