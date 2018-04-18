using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenGL;
using System.Drawing;
using System.Drawing.Imaging;

using GLPixelFormat = OpenGL.PixelFormat;
using IPixFormat = System.Drawing.Imaging.PixelFormat;
using System.Diagnostics;

namespace FishGfx.Graphics {
	public unsafe partial class Texture : GraphicsObject {
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

		TextureTarget Target;

		private Texture(int W, int H, TextureTarget Target = TextureTarget.Texture2d, int MipLevels = 1, InternalFormat IntFormat = InternalFormat.Rgba8) {
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

		public void SetWrap(int Val = Gl.CLAMP_TO_EDGE) {
			TextureParam(TextureParameterName.TextureWrapS, Val);
			TextureParam(TextureParameterName.TextureWrapT, Val);
			TextureParam(TextureParameterName.TextureWrapR, Val);
		}

		public void SetFilter(int Min = Gl.NEAREST, int Mag = Gl.NEAREST) {
			TextureParam(TextureParameterName.TextureMinFilter, Min);
			TextureParam(TextureParameterName.TextureMagFilter, Mag);
		}

		public void SetFilterSmooth() {
			SetFilter(Gl.LINEAR, Gl.LINEAR);
		}

		public void SetMaxAnisotropy() {
			if (!Internal_OpenGL.Is45OrAbove)
				return;

			Gl.Get(Gl.MAX_TEXTURE_MAX_ANISOTROPY, out float Max);
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
			if (Internal_OpenGL.Is45OrAbove)
				Gl.BindTextureUnit(Unit, 0);
			else {
				Gl.ActiveTexture(TextureUnit.Texture0 + (int)Unit);
				Unbind();
			}
		}

		public override void Bind() {
			if (Internal_OpenGL.Is45OrAbove)
				throw new Exception("This function is not used in OpenGL 4.5");

			Gl.BindTexture(TextureTarget.Texture2d, ID);
		}

		public override void Unbind() {
			if (Internal_OpenGL.Is45OrAbove)
				throw new Exception("This function is not used in OpenGL 4.5");

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
	}

	public unsafe partial class Texture : GraphicsObject {
		public static Texture FromImage(Image Img) {
			Texture Tex = new Texture(Img.Width, Img.Height);
			Tex.SubImage2D(Img);
			return Tex;
		}

		public static Texture FromFile(string FileName) {
			using (Image Img = Image.FromFile(FileName))
				return FromImage(Img);
		}
	}
}
