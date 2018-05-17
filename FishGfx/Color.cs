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

		public byte R, G, B, A;

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

		public override string ToString() {
			return string.Format("({0} {1} {2} {3})", R, G, B, A);
		}

		public static Color Clamp(Color C, IEnumerable<Color> Palette) {
			return Color.White; // TODO
		}
	}
}
