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
		internal const int Multisamples = 4;

		internal const int RIGHT = 0;
		internal const int LEFT = 1;
		internal const int BOTTOM = 2;
		internal const int TOP = 3;
		internal const int FRONT = 4;
		internal const int BACK = 5;

		public int Width { get; private set; }
		public int Height { get; private set; }
		public int MipLevels { get; private set; }
		public bool Multisampled { get; private set; }
		public bool IsCubeMap { get; private set; }

		public Vector2 Size { get { return new Vector2(Width, Height); } }

		TextureTarget Target;

		internal Texture(int W, int H, TextureTarget Target = TextureTarget.Texture2d, int MipLevels = 1, InternalFormat IntFormat = InternalFormat.Rgba8) {
			this.Target = Target;

			if (Internal_OpenGL.Is45OrAbove)
				ID = Gl.CreateTexture(Target);
			else
				ID = Gl.GenTexture();

			if (Target == TextureTarget.Texture2dMultisample)
				Multisampled = true;
			else if (Target == TextureTarget.TextureCubeMap)
				IsCubeMap = true;

			if (!Multisampled) {
				SetWrap();
				SetFilter();
				SetMaxAnisotropy();
			}

			Storage2D(W, H, MipLevels, IntFormat);
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

			} else throw new NotImplementedException();
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

		public void SetMaxAnisotropy() {
			if (!Internal_OpenGL.Is45OrAbove)
				return;

			Gl.Get(Gl.MAX_TEXTURE_MAX_ANISOTROPY, out float Max);
			SetAnisotropy(Max);
		}

		public void SetAnisotropy(float Max) {
			TextureParam((TextureParameterName)Gl.TEXTURE_MAX_ANISOTROPY, Max);
		}

		public void Storage2D(int W, int H, int Levels = 1, InternalFormat IntFormat = InternalFormat.Rgba) {
			Width = W;
			Height = H;
			MipLevels = Levels;

			if (Multisampled) {
				if (Internal_OpenGL.Is45OrAbove)
					Gl.TextureStorage2DMultisample(ID, Multisamples, IntFormat, W, H, false);
				else {
					Bind();
					Gl.TexStorage2DMultisample(Target, Multisamples, IntFormat, W, H, false);
					Unbind();
				}
			} else {
				if (Internal_OpenGL.Is45OrAbove)
					Gl.TextureStorage2D(ID, Levels, IntFormat, W, H);
				else {
					Bind();
					Gl.TexStorage2D(Target, Levels, IntFormat, W, H);
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

		public override void GraphicsDispose() {
			Gl.DeleteTextures(new uint[] { ID });
		}

		// Static

		public static Texture Empty(int W, int H) {
			return new Texture(W, H);
		}

		public static void UpdateFromImage(Texture T, Image Img) {
			T.SubImage2D(Img);
		}

		public static Texture FromImage(Image Img) {
			Texture Tex = new Texture(Img.Width, Img.Height);
			Tex.SubImage2D(Img);
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

		public static Texture FromFile(string FileName, bool FlipY = false) {
			if (FlipY) {
				using (Bitmap Bmp = new Bitmap(FileName)) {
					Bmp.RotateFlip(RotateFlipType.RotateNoneFlipY);
					return FromImage(Bmp);
				}
			}

			return FromImage(Image.FromFile(FileName));
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
	}
}
