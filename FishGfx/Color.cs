using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FishGfx {
	public struct Color {
		public static readonly Color White = new Color() { R = 255, G = 255, B = 255, A = 255 };
		public static readonly Color Black = new Color() { R = 0, G = 0, B = 0, A = 255 };

		public static readonly Color Red = new Color() { R = 255, G = 0, B = 0, A = 255 };
		public static readonly Color Green = new Color() { R = 0, G = 255, B = 0, A = 255 };
		public static readonly Color Blue = new Color() { R = 0, G = 0, B = 255, A = 255 };

		public byte R, G, B, A;
	}
}
