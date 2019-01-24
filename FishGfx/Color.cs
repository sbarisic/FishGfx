using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace FishGfx {
	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public struct Color {
		public static readonly Color Transparent = new Color() { R = 0, G = 0, B = 0, A = 0 };
		public static readonly Color White = new Color() { R = 255, G = 255, B = 255, A = 255 };
		public static readonly Color Black = new Color() { R = 0, G = 0, B = 0, A = 255 };

		public static readonly Color Red = new Color() { R = 255, G = 0, B = 0, A = 255 };
		public static readonly Color Green = new Color() { R = 0, G = 255, B = 0, A = 255 };
		public static readonly Color Blue = new Color() { R = 0, G = 0, B = 255, A = 255 };

		public static readonly Color Yellow = new Color() { R = 255, G = 255, B = 0, A = 255 };
		public static readonly Color Cyan = new Color() { R = 0, G = 255, B = 255, A = 255 };
		public static readonly Color Magenta = new Color() { R = 255, G = 0, B = 255, A = 255 };

		public static readonly Color Orange = new Color() { R = 230, G = 140, B = 0, A = 255 };

		public byte R;
		public byte G;
		public byte B;
		public byte A;

		public int ColorInt {
			get {
				return ((int)A << 24) | ((int)B << 16) | ((int)G << 8) | (int)R;
			}

			set {
				A = (byte)((value >> 24) & 0xFF);
				B = (byte)((value >> 16) & 0xFF);
				G = (byte)((value >> 8) & 0xFF);
				R = (byte)((value >> 0) & 0xFF);
			}
		}

		public Color(byte R, byte G, byte B, byte A) {
			this.R = R;
			this.G = G;
			this.B = B;
			this.A = A;
		}

		public Color(byte R, byte G, byte B) : this(R, G, B, 255) {
		}

		public Color(int ColorInt) : this(0, 0, 0, 0) {
			this.ColorInt = ColorInt;
		}

		public Color(float R, float G, float B) : this((byte)(R * 255), (byte)(G * 255), (byte)(B * 255)) {
		}

		public Color(double R, double G, double B) : this((float)R, (float)G, (float)B) {
		}

		public override string ToString() {
			return string.Format("({0} {1} {2} {3})", R, G, B, A);
		}

		public override bool Equals(object Obj) {
			if (Obj is Color Clr)
				return Clr == this;

			return false;
		}

		public override int GetHashCode() {
			return ColorInt.GetHashCode();
		}

		public static Color Clamp(Color C, IEnumerable<Color> Palette) {
			return Color.White; // TODO
		}

		public static bool operator ==(Color A, Color B) {
			return A.R == B.R && A.G == B.G && A.B == B.B && A.A == B.A;
		}

		public static bool operator !=(Color A, Color B) {
			return !(A == B);
		}

		public static implicit operator System.Drawing.Color(Color Clr) {
			return System.Drawing.Color.FromArgb(Clr.A, Clr.R, Clr.G, Clr.B);
		}

		public static implicit operator Color(System.Drawing.Color Clr) {
			return new Color(Clr.R, Clr.G, Clr.B, Clr.A);
		}
	}
}
