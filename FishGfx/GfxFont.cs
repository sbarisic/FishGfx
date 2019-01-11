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

			public bool Drawable;
			public CharOrigin CharOrigin;
		}

		public virtual object Userdata { get; set; }

		public virtual float FontScale { get; set; }
		public virtual bool FlipY { get; set; }

		public abstract string FontName { get; }
		public abstract int LineHeight { get; }
		public abstract int FontSize { get; }
		public abstract int TabSize { get; }

		public abstract CharOrigin? GetCharInfo(char C);

		public virtual Vector2 MeasureString(string Str) {
			return MeasureString(LayoutString(Str));
		}

		public virtual Vector2 MeasureString(CharDest[] Chars) {
			Vector2 Max = new Vector2(Chars[0].X, Chars[0].Y);
			Vector2 Min = new Vector2(Chars[0].X, Chars[0].Y);

			for (int i = 0; i < Chars.Length; i++) {
				Max.X = Math.Max(Max.X, Chars[i].X + Chars[i].W);
				Max.Y = Math.Max(Max.Y, Chars[i].Y + Chars[i].H);

				Min.X = Math.Min(Min.X, Chars[i].X);
				Min.Y = Math.Min(Min.Y, Chars[i].Y);
			}

			return Max - Min;
		}

		// TODO: Padding 'nd shit
		public virtual CharDest[] LayoutString(string Str) {
			CharDest[] Chars = new CharDest[Str.Length];
			float Scale = FontScale / FontSize;

			int X = 0;
			int Y = 0;

			for (int i = 0; i < Str.Length; i++) {
				char Chr = Str[i];
				CharOrigin? MaybeCharInfo = GetCharInfo(Chr);

				if (!MaybeCharInfo.HasValue)
					MaybeCharInfo = GetCharInfo('?');
				//throw new NotImplementedException();

				CharOrigin ChrOrigin = MaybeCharInfo.Value;

				ref CharDest CurChar = ref Chars[i];
				CurChar.CharOrigin = ChrOrigin;
				CurChar.Drawable = false;
				CurChar.X = (X + ChrOrigin.XOffset);
				CurChar.W = ChrOrigin.W;
				CurChar.H = ChrOrigin.H;

				if (FlipY)
					CurChar.Y = (Y - ChrOrigin.YOffset - CurChar.H);
				else
					CurChar.Y = (Y + ChrOrigin.YOffset);

				if (Chr == '\r')
					continue;

				if (Chr == '\n') {
					X = 0;

					if (FlipY)
						Y -= LineHeight;
					else
						Y += LineHeight;
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
				CurChar.Drawable = true;
			}

			return Chars;
		}
	}
}
