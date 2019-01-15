using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace FishGfx {
	public abstract class GfxFont {
		public struct CharOrigin {
			public char Char;

			public float X;
			public float Y;
			public float W;
			public float H;

			public int XOffset;
			public int YOffset;

			public int XAdvance;

			public GfxFont Owner;
		}

		public struct CharDest {
			public float X;
			public float Y;
			public float W;
			public float H;

			public CharOrigin CharOrigin;
		}

		public virtual object Userdata { get; set; }
		public abstract string FontName { get; }

		public abstract int LineHeight { get; }
		public virtual int ScaledLineHeight {
			get {
				return (int)(LineHeight * (ScaledFontSize / FontSize));
			}
		}

		public abstract int FontSize { get; }
		public virtual float ScaledFontSize { get; set; }

		public abstract int TabSize { get; }
		public virtual int ScaledTabSize {
			get {
				return (int)(TabSize * (ScaledFontSize / FontSize));
			}
		}

		public abstract CharOrigin? GetCharInfo(char C);

		public virtual Vector2 MeasureString(string Str) {
			return MeasureString(LayoutString(Str));
		}

		public virtual Vector2 MeasureString(CharDest[] Chars) {
			MeasureString(Chars, out Vector2 Min, out Vector2 Max);
			return Max - Min;
		}

		public virtual void MeasureString(CharDest[] Chars, out Vector2 MinCoord, out Vector2 MaxCoord) {
			Vector2 Max = new Vector2(Chars[0].X, Chars[0].Y);
			Vector2 Min = new Vector2(Chars[0].X, Chars[0].Y);

			for (int i = 0; i < Chars.Length; i++) {
				Max.X = Math.Max(Max.X, Chars[i].X + Chars[i].W);
				Max.Y = Math.Max(Max.Y, Chars[i].Y + Chars[i].H);

				Min.X = Math.Min(Min.X, Chars[i].X);
				Min.Y = Math.Min(Min.Y, Chars[i].Y);
			}

			MinCoord = Min;
			MaxCoord = Max;
		}

		// TODO: Padding 'nd shit
		public virtual CharDest[] LayoutString(string Str) {
			List<CharDest> Chars = new List<CharDest>(Str.Length);
			float Scale = ScaledFontSize / FontSize;

			int X = 0;
			int Y = 0;
			int LineCount = 1;

			for (int i = 0; i < Str.Length; i++) {
				char Chr = Str[i];
				CharOrigin? MaybeCharInfo = GetCharInfo(Chr);

				if (!MaybeCharInfo.HasValue)
					MaybeCharInfo = GetCharInfo('?');
				//throw new NotImplementedException();

				CharOrigin ChrOrigin = MaybeCharInfo.Value;

				CharDest CurChar = new CharDest();
				CurChar.CharOrigin = ChrOrigin;
				CurChar.X = (X + ChrOrigin.XOffset);
				CurChar.W = ChrOrigin.W;
				CurChar.H = ChrOrigin.H;

				CurChar.Y = (Y - ChrOrigin.YOffset - CurChar.H);

				if (Chr == '\r')
					continue;

				if (Chr == '\n') {
					X = 0;
					LineCount++;

					Y -= LineHeight;
					continue;
				}

				if (Chr == '\t') {
					X += TabSize;
					continue;
				}

				X += ChrOrigin.XAdvance;

				CurChar.X = (CurChar.X * Scale);
				CurChar.Y = (CurChar.Y * Scale);
				CurChar.W = (CurChar.W * Scale);
				CurChar.H = (CurChar.H * Scale);
				Chars.Add(CurChar);
			}

			for (int i = 0; i < Chars.Count; i++) {
				CharDest C = Chars[i];
				C.Y += LineCount * LineHeight * Scale;
				Chars[i] = C;
			}

			return Chars.ToArray();
		}
	}
}
