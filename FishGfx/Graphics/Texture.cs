using OpenGL;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using GLPixelFormat = OpenGL.PixelFormat;
using IPixFormat = System.Drawing.Imaging.PixelFormat;

namespace FishGfx.Graphics {
	public enum TextureInternalFmt : int {
		DepthComponent = 6402,
		Red = 6403,
		Rgb = 6407,
		Rgba = 6408,
		R3G3B2 = 10768,
		Alpha4 = 32827,
		Alpha8 = 32828,
		Alpha12 = 32829,
		Alpha16 = 32830,
		Luminance4 = 32831,
		Luminance8 = 32832,
		Luminance12 = 32833,
		Luminance16 = 32834,
		Luminance4Alpha4 = 32835,
		Luminance6Alpha2 = 32836,
		Luminance8Alpha8 = 32837,
		Luminance12Alpha4 = 32838,
		Luminance12Alpha12 = 32839,
		Luminance16Alpha16 = 32840,
		Intensity = 32841,
		Intensity4 = 32842,
		Intensity8 = 32843,
		Intensity12 = 32844,
		Intensity16 = 32845,
		Rgb2Ext = 32846,
		Rgb4 = 32847,
		Rgb5 = 32848,
		Rgb8 = 32849,
		Rgb10 = 32850,
		Rgb12 = 32851,
		Rgb16 = 32852,
		Rgba4 = 32854,
		Rgb5A1 = 32855,
		Rgba8 = 32856,
		Rgb10A2 = 32857,
		Rgba12 = 32858,
		Rgba16 = 32859,
		DualAlpha4Sgis = 33040,
		DualAlpha8Sgis = 33041,
		DualAlpha12Sgis = 33042,
		DualAlpha16Sgis = 33043,
		DualLuminance4Sgis = 33044,
		DualLuminance8Sgis = 33045,
		DualLuminance12Sgis = 33046,
		DualLuminance16Sgis = 33047,
		DualIntensity4Sgis = 33048,
		DualIntensity8Sgis = 33049,
		DualIntensity12Sgis = 33050,
		DualIntensity16Sgis = 33051,
		DualLuminanceAlpha4Sgis = 33052,
		DualLuminanceAlpha8Sgis = 33053,
		QuadAlpha4Sgis = 33054,
		QuadAlpha8Sgis = 33055,
		QuadLuminance4Sgis = 33056,
		QuadLuminance8Sgis = 33057,
		QuadIntensity4Sgis = 33058,
		QuadIntensity8Sgis = 33059,
		DepthComponent16 = 33189,
		DepthComponent24 = 33190,
		DepthComponent32 = 33191,
		CompressedRed = 33317,
		CompressedRg = 33318,
		Rg = 33319,
		R8 = 33321,
		R16 = 33322,
		Rg8 = 33323,
		Rg16 = 33324,
		R16f = 33325,
		R32f = 33326,
		Rg16f = 33327,
		Rg32f = 33328,
		R8i = 33329,
		R8ui = 33330,
		R16i = 33331,
		R16ui = 33332,
		R32i = 33333,
		R32ui = 33334,
		Rg8i = 33335,
		Rg8ui = 33336,
		Rg16i = 33337,
		Rg16ui = 33338,
		Rg32i = 33339,
		Rg32ui = 33340,
		CompressedRgbS3tcDxt1Ext = 33776,
		CompressedRgbaS3tcDxt1Ext = 33777,
		CompressedRgbaS3tcDxt3Ext = 33778,
		CompressedRgbaS3tcDxt5Ext = 33779,
		CompressedRgb = 34029,
		CompressedRgba = 34030,
		DepthStencil = 34041,
		Rgba32f = 34836,
		Rgba16f = 34842,
		Rgb16f = 34843,
		Depth24Stencil8 = 35056,
		R11fG11fB10f = 35898,
		Rgb9E5 = 35901,
		Srgb = 35904,
		Srgb8 = 35905,
		SrgbAlpha = 35906,
		Srgb8Alpha8 = 35907,
		CompressedSrgb = 35912,
		CompressedSrgbAlpha = 35913,
		CompressedSrgbS3tcDxt1Ext = 35916,
		CompressedSrgbAlphaS3tcDxt1Ext = 35917,
		CompressedSrgbAlphaS3tcDxt3Ext = 35918,
		CompressedSrgbAlphaS3tcDxt5Ext = 35919,
		DepthComponent32f = 36012,
		Depth32fStencil8 = 36013,
		Rgba32ui = 36208,
		Rgb32ui = 36209,
		Rgba16ui = 36214,
		Rgb16ui = 36215,
		Rgba8ui = 36220,
		Rgb8ui = 36221,
		Rgba32i = 36226,
		Rgb32i = 36227,
		Rgba16i = 36232,
		Rgb16i = 36233,
		Rgba8i = 36238,
		Rgb8i = 36239,
		DepthComponent32fNv = 36267,
		Depth32fStencil8Nv = 36268,
		CompressedRedRgtc1 = 36283,
		CompressedSignedRedRgtc1 = 36284,
		CompressedRgRgtc2 = 36285,
		CompressedSignedRgRgtc2 = 36286,
		CompressedRgbaBptcUnorm = 36492,
		CompressedSrgbAlphaBptcUnorm = 36493,
		CompressedRgbBptcSignedFloat = 36494,
		CompressedRgbBptcUnsignedFloat = 36495,
		R8Snorm = 36756,
		Rg8Snorm = 36757,
		Rgb8Snorm = 36758,
		Rgba8Snorm = 36759,
		R16Snorm = 36760,
		Rg16Snorm = 36761,
		Rgb16Snorm = 36762,
		Rgb10A2ui = 36975,
		CompressedR11Eac = 37488,
		CompressedSignedR11Eac = 37489,
		CompressedRg11Eac = 37490,
		CompressedSignedRg11Eac = 37491,
		CompressedRgb8Etc2 = 37492,
		CompressedSrgb8Etc2 = 37493,
		CompressedRgb8PunchthroughAlpha1Etc2 = 37494,
		CompressedSrgb8PunchthroughAlpha1Etc2 = 37495,
		CompressedRgba8Etc2Eac = 37496,
		CompressedSrgb8Alpha8Etc2Eac = 37497
	}

	public enum TextureWrap : int {
		Repeat = Gl.REPEAT,
		MirroredRepeat = Gl.MIRRORED_REPEAT,
		ClampToEdge = Gl.CLAMP_TO_EDGE,
		ClampToBorder = Gl.CLAMP_TO_BORDER
	}

	public enum TextureFilter : int {
		Nearest = Gl.NEAREST,
		Linear = Gl.LINEAR,
		NearestMipmapNearest = Gl.NEAREST_MIPMAP_NEAREST,
		LinearMipmapNearest = Gl.LINEAR_MIPMAP_NEAREST,
		NearestMipmapLinear = Gl.NEAREST_MIPMAP_LINEAR,
		LinearMipmapLinear = Gl.LINEAR_MIPMAP_LINEAR
	}

	public enum PixelFmt {
		Rgb = GLPixelFormat.Rgb,
		Rgba = GLPixelFormat.Rgba,

		Abgr = GLPixelFormat.AbgrExt,
		Bgr = GLPixelFormat.Bgr,
		Bgra = GLPixelFormat.Bgra
	}

	public unsafe class Texture : GraphicsObject {
		internal const int RIGHT = 0;
		internal const int LEFT = 1;
		internal const int BOTTOM = 2;
		internal const int TOP = 3;
		internal const int FRONT = 4;
		internal const int BACK = 5;

		public int Width {
			get; private set;
		}
		public int Height {
			get; private set;
		}
		public int MipLevels {
			get; private set;
		}
		public bool Multisampled {
			get; private set;
		}
		public bool IsCubeMap {
			get; private set;
		}
		public int Multisamples {
			get; private set;
		}
		public Vector2 Size {
			get {
				return new Vector2(Width, Height);
			}
		}

		TextureTarget Target;
		InternalFormat InternalFormat;
		bool FixedSampleLocations;

		public Texture(int W, int H, TextureTarget Target = TextureTarget.Texture2d, int MipLevels = 1, TextureInternalFmt IntFormat = TextureInternalFmt.Rgba8, int Samples = 0, bool FixedSampleLocations = false) {
			this.Target = Target;

			if (Internal_OpenGL.Is45OrAbove)
				ID = Gl.CreateTexture(Target);
			else
				ID = Gl.GenTexture();

			Multisampled = Samples != 0;
			Multisamples = Samples;

			if (Target == TextureTarget.Texture2dMultisample && !Multisampled)
				throw new InvalidOperationException("Please specify sample size for multisampled textures");

			if (Target == TextureTarget.TextureCubeMap)
				IsCubeMap = true;

			if (!Multisampled) {
				SetWrap();
				SetFilter();
				SetMaxAnisotropy();
			}

			Storage2D(W, H, MipLevels, IntFormat, FixedSampleLocations);
		}

		private void TextureParam(TextureParameterName ParamName, object Val) {
			if (Val is int) {

				if (Internal_OpenGL.Is45OrAbove)
					Gl.TextureParameter(ID, ParamName, (int)Val);
				else {
					Bind();
					Gl.TexParameter(Target, ParamName, (int)Val);
					Unbind();
				}

			} else if (Val is float) {

				if (Internal_OpenGL.Is45OrAbove)
					Gl.TextureParameter(ID, ParamName, (float)Val);
				else {
					Bind();
					Gl.TexParameter(Target, ParamName, (int)Val);
					Unbind();
				}

			} else
				throw new NotImplementedException();
		}

		public void SetWrap(TextureWrap Wrap) {
			SetWrap((int)Wrap);
		}

		public void SetWrap(TextureWrap UWrap, TextureWrap VWrap) {
			TextureParam(TextureParameterName.TextureWrapS, (int)UWrap);
			TextureParam(TextureParameterName.TextureWrapT, (int)VWrap);
		}

		public void SetWrap(int Val = Gl.CLAMP_TO_EDGE) {
			TextureParam(TextureParameterName.TextureWrapS, Val);
			TextureParam(TextureParameterName.TextureWrapT, Val);
			TextureParam(TextureParameterName.TextureWrapR, Val);
		}

		public void SetFilter(int Min = Gl.NEAREST, int Mag = Gl.NEAREST) {
			TextureParam(TextureParameterName.TextureMinFilter, Min);
			TextureParam(TextureParameterName.TextureMagFilter, Mag);
		}

		public void SetFilter(TextureFilter Min, TextureFilter Mag) {
			SetFilter((int)Min, (int)Mag);
		}

		public void SetFilter(TextureFilter Filter) {
			SetFilter(Filter, Filter);
		}

		public void SetMinFilter(TextureFilter Filter) {
			TextureParam(TextureParameterName.TextureMinFilter, (int)Filter);
		}

		public void SetMagFilter(TextureFilter Filter) {
			TextureParam(TextureParameterName.TextureMagFilter, (int)Filter);
		}

		public void SetMaxAnisotropy() {
			if (!Internal_OpenGL.Is45OrAbove)
				return;

			Gl.Get(Gl.MAX_TEXTURE_MAX_ANISOTROPY, out float Max);
			SetAnisotropy(Max);
		}

		public void SetAnisotropy(float Max) {
			TextureParam((TextureParameterName)Gl.TEXTURE_MAX_ANISOTROPY, Max);
		}

		public void Storage2D(int W, int H, int Levels = 1, TextureInternalFmt IntFormat = TextureInternalFmt.Rgba, bool FixedSampleLocations = false) {
			Width = W;
			Height = H;
			MipLevels = Levels;
			InternalFormat = (InternalFormat)IntFormat;
			this.FixedSampleLocations = FixedSampleLocations;

			if (Multisampled) {
				if (Internal_OpenGL.Is45OrAbove)
					Gl.TextureStorage2DMultisample(ID, Multisamples, InternalFormat, W, H, FixedSampleLocations);
				else {
					Bind();
					Gl.TexStorage2DMultisample(Target, Multisamples, InternalFormat, W, H, FixedSampleLocations);
					Unbind();
				}
			} else {
				if (Internal_OpenGL.Is45OrAbove)
					Gl.TextureStorage2D(ID, Levels, InternalFormat, W, H);
				else {
					Bind();
					Gl.TexStorage2D(Target, Levels, InternalFormat, W, H);
					Unbind();
				}
			}
		}

		public void SubImage(IntPtr Pixels, int X, int Y, int Z, int W, int H, int D,
			GLPixelFormat PFormat = GLPixelFormat.Rgba, PixelType PType = PixelType.UnsignedByte, int Level = 0) {

			if (Z == 0 && D == 0) {
#if DEBUG
				if (IsCubeMap)
					throw new Exception("Invalid Z/D parameter for cubemap");
#endif

				if (Internal_OpenGL.Is45OrAbove)
					Gl.TextureSubImage2D(ID, Level, X, Y, W, H, PFormat, PType, Pixels);
				else {
					Bind();
					Gl.TexSubImage2D(Target, Level, X, Y, W, H, PFormat, PType, Pixels);
					Unbind();
				}

			} else {
				if (Internal_OpenGL.Is45OrAbove)
					Gl.TextureSubImage3D(ID, Level, X, Y, Z, W, H, D, PFormat, PType, Pixels);
				else {
					Bind();
					Gl.TexSubImage3D(Target, Level, X, Y, Z, W, H, D, PFormat, PType, Pixels);
					Unbind();
				}
			}

			if (MipLevels > 1)
				GenerateMipmap();
		}

		public void SetPixels2D_RGB8(IntPtr Data, int Width, int Height) {
			SubImage(Data, 0, 0, 0, Width, Height, 0, GLPixelFormat.Rgb);
		}

		public void SetPixels2D_BGR8(IntPtr Data, int Width, int Height) {
			SubImage(Data, 0, 0, 0, Width, Height, 0, GLPixelFormat.Bgr);
		}

		public void SetPixels2D_R16(IntPtr Data, int Width, int Height) {
			SubImage(Data, 0, 0, 0, Width, Height, 0, GLPixelFormat.Red, PixelType.UnsignedShort);
		}

		public Color[] GetPixels() {
			Color[] Clrs = new Color[Width * Height];

			// TODO: Older OpenGL way?
			fixed (Color* ClrsPtr = Clrs) {
				if (Internal_OpenGL.Is45OrAbove)
					Gl.GetTextureImage(ID, 0, GLPixelFormat.Rgba, PixelType.UnsignedByte, Clrs.Length * sizeof(Color), (IntPtr)ClrsPtr);
				else {
					Bind();
					Gl.GetTexImage(TextureTarget.Texture2d, 0, GLPixelFormat.Rgba, PixelType.UnsignedByte, (IntPtr)ClrsPtr);
					Unbind();
				}
			}

			return Clrs;
		}

		public Color GetPixel(int X, int Y) {
			return GetPixels()[(Height - Y - 1) * Width + X];
		}

		public Bitmap GetPixelsAsBitmap() {
			Bitmap Bmp = new Bitmap(Width, Height);
			BitmapData Data = Bmp.LockBits(new Rectangle(0, 0, Bmp.Width, Bmp.Height), ImageLockMode.WriteOnly, IPixFormat.Format32bppArgb);

			Color[] Pixels = GetPixels();
			for (int i = 0; i < Pixels.Length; i++) {
				int Offset = i * sizeof(Color);
				*(Color*)(Data.Scan0 + Offset) = new Color(Pixels[i].B, Pixels[i].G, Pixels[i].R, Pixels[i].A);
			}

			Bmp.UnlockBits(Data);
			Bmp.RotateFlip(RotateFlipType.RotateNoneFlipY);
			return Bmp;
		}

		public void SubRect2D(Image Img, int X, int Y) {
			GetImageData(Img, 0, 0, Img.Width, Img.Height, (Ptr) => SubImage(Ptr, X, Y, 0, Img.Width, Img.Height, 0, GLPixelFormat.Bgra));
		}

		public void SubImage2D(Image Img, int X = 0, int Y = 0, int W = -1, int H = -1, int Level = 0) {
			if (W == -1 || H == -1) {
				W = Img.Width;
				H = Img.Height;
			}

			GetImageData(Img, X, Y, W, H, (Ptr) => SubImage(Ptr, X, Y, 0, W, H, 0, GLPixelFormat.Bgra, Level: Level));
		}

		public void SubImage3D(Image Img, int X = 0, int Y = 0, int Z = 0, int W = -1, int H = -1, int D = 1, int Level = 0) {
			if (W == -1 || H == -1) {
				W = Img.Width;
				H = Img.Height;
			}

			GetImageData(Img, X, Y, W, H, (Ptr) => SubImage(Ptr, X, Y, Z, W, H, D, GLPixelFormat.Bgra, Level: Level));
		}

		[DebuggerStepThrough]
		void GetImageData(Image Img, int X, int Y, int W, int H, Action<IntPtr> A) {
			using (Bitmap Bmp = new Bitmap(Img)) {
				Bmp.RotateFlip(RotateFlipType.RotateNoneFlipY);

				BitmapData Data = Bmp.LockBits(new Rectangle(X, Y, W, H), ImageLockMode.ReadOnly, IPixFormat.Format32bppArgb);
				A(Data.Scan0);
				Bmp.UnlockBits(Data);
			}
		}

		public void BindTextureUnit(uint Unit = 0) {
			if (Internal_OpenGL.Is45OrAbove)
				Gl.BindTextureUnit(Unit, ID);
			else {
				Gl.ActiveTexture(TextureUnit.Texture0 + (int)Unit);
				Bind();
			}
		}

		public void UnbindTextureUnit(uint Unit = 0) {
			if (Internal_OpenGL.Is45OrAbove) {
				if (OpenGL_BODGES.INTEL_BIND_ZERO_TEXTURE_BUG) {
					// TODO: Do something?
				} else {
					try {
						Gl.BindTextureUnit(Unit, 0);
					} catch (GlException) {
						OpenGL_BODGES.INTEL_BIND_ZERO_TEXTURE_BUG = true;
					}
				}
			} else {
				Gl.ActiveTexture(TextureUnit.Texture0 + (int)Unit);
				Unbind();
			}
		}

		public override void Bind() {
			if (Internal_OpenGL.Is45OrAbove)
				throw new InvalidOperationException("This function is not used in OpenGL 4.5");

			Gl.BindTexture(TextureTarget.Texture2d, ID);
		}

		public override void Unbind() {
			if (Internal_OpenGL.Is45OrAbove)
				throw new InvalidOperationException("This function is not used in OpenGL 4.5");

			Gl.BindTexture(TextureTarget.Texture2d, 0);
		}

		public void GenerateMipmap() {
			if (Internal_OpenGL.Is45OrAbove)
				Gl.GenerateTextureMipmap(ID);
			else
				Gl.GenerateMipmap(Target);
		}

		public void GenerateMipmap(int NewMipLevels) {
			Storage2D(Width, Height, NewMipLevels, (TextureInternalFmt)InternalFormat, FixedSampleLocations);
			GenerateMipmap();
		}

		public override void GraphicsDispose() {
			Gl.DeleteTextures(new uint[] { ID });
		}

		// Static

		public static Texture Empty(int W, int H) {
			return new Texture(W, H);
		}

		public static void UpdateFromImage(Texture T, Image Img) {
			T.SubImage2D(Img);

			if (T.MipLevels != 1)
				T.GenerateMipmap();
		}

		public static Texture FromImage(Image Img, int MipmapLevels = 0) {
			bool GenerateMipmaps = MipmapLevels > 0;

			Texture Tex = null;
			if (Img.Size.Width == 1 || Img.Size.Height == 1)
				GenerateMipmaps = false;

			if (GenerateMipmaps)
				Tex = new Texture(Img.Width, Img.Height, MipLevels: MipmapLevels);
			else
				Tex = new Texture(Img.Width, Img.Height);

			Tex.SubImage2D(Img);

			if (GenerateMipmaps)
				Tex.GenerateMipmap();

			return Tex;
		}

		public static void UpdateFromFile(Texture T, string FileName, bool FlipY = false) {
			if (FlipY) {
				using (Bitmap Bmp = new Bitmap(FileName)) {
					Bmp.RotateFlip(RotateFlipType.RotateNoneFlipY);
					UpdateFromImage(T, Bmp);
					return;
				}
			}

			UpdateFromImage(T, Image.FromFile(FileName));
		}

		public static Texture FromFile(string FileName, bool FlipY = false, int MipmapLevels = 0) {
			if (FlipY) {
				using (Bitmap Bmp = new Bitmap(FileName)) {
					Bmp.RotateFlip(RotateFlipType.RotateNoneFlipY);
					return FromImage(Bmp, MipmapLevels);
				}
			}

			return FromImage(Image.FromFile(FileName), MipmapLevels);
		}

		public static Texture[] FromFileAtlas(string FileName, int TileWidth, int TileHeight, int MipmapLevels = 0) {
			List<Texture> AnimFrames = new List<Texture>();

			using (Bitmap Bmp = new Bitmap(FileName)) {
				int FramesX = Bmp.Width / TileWidth;
				int FramesY = Bmp.Height / TileHeight;

				for (int Y = 0; Y < FramesY; Y++)
					for (int X = 0; X < FramesX; X++) {
						Bitmap FrameBmp = Bmp.Clone(new Rectangle(X * TileWidth, Y * TileHeight, TileWidth, TileHeight), Bmp.PixelFormat);
						AnimFrames.Add(FromImage(FrameBmp, MipmapLevels: MipmapLevels));
					}

			}

			return AnimFrames.ToArray();
		}

		public static void CreateOrUpdateFromFile(ref Texture T, string FileName, bool FlipY = false) {
			if (T == null) {
				T = FromFile(FileName, FlipY);
				return;
			}

			UpdateFromFile(T, FileName, FlipY);
		}

		public static Texture FromPixels(int Width, int Height, IntPtr Data, PixelFmt Fmt = PixelFmt.Bgra) {
			Texture Tex = new Texture(Width, Height);
			Tex.SubImage(Data, 0, 0, 0, Width, Height, 0, (GLPixelFormat)Fmt);
			return Tex;
		}

		public static Texture FromPixels(int Width, int Height, byte[] Data, PixelFmt Fmt = PixelFmt.Bgra) {
			fixed (byte* DataPtr = Data)
				return FromPixels(Width, Height, (IntPtr)DataPtr, Fmt);
		}

		static bool EqualSize(Image A, Image B) {
			if (A.Width == B.Width && A.Height == B.Height)
				return true;

			return false;
		}

		public static Texture FromFileCubemap(string Left, string Front, string Right, string Back, string Bottom, string Top) {
			Image Lt = Image.FromFile(Left);
			Image Ft = Image.FromFile(Front);
			Image Rt = Image.FromFile(Right);
			Image Bk = Image.FromFile(Back);
			Image Bt = Image.FromFile(Bottom);
			Image Tp = Image.FromFile(Top);

			if (!(EqualSize(Lt, Ft) && EqualSize(Lt, Rt) && EqualSize(Lt, Bk) && EqualSize(Lt, Bt) && EqualSize(Lt, Tp)))
				throw new Exception("All cubemap image sizes need to be of equal size");

			int W = Lt.Width;
			int H = Lt.Height;

			Texture CubeTex = new Texture(W, H, TextureTarget.TextureCubeMap);
			CubeTex.SetFilter(TextureFilter.Linear);

			CubeTex.SubImage3D(Lt, Z: LEFT);
			CubeTex.SubImage3D(Ft, Z: FRONT);
			CubeTex.SubImage3D(Rt, Z: RIGHT);
			CubeTex.SubImage3D(Bk, Z: BACK);
			CubeTex.SubImage3D(Bt, Z: BOTTOM);
			CubeTex.SubImage3D(Tp, Z: TOP);
			return CubeTex;
		}

		public static Texture FromFileCubemap(string BaseName, string Extension = ".png") {
			return FromFileCubemap(BaseName + "_lt" + Extension, BaseName + "_ft" + Extension, BaseName + "_rt" + Extension, BaseName + "_bk" + Extension, BaseName + "_bt" + Extension, BaseName + "_tp" + Extension);
		}
	}
}
