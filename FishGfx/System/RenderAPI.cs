using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FishGfx.Graphics;

namespace FishGfx.System {
	public static class RenderAPI {
		public static string Version {
			get {
				return Internal_OpenGL.Version;
			}
		}

		public static string[] Extensions {
			get {
				return Internal_OpenGL.Extensions;
			}
		}

		public static string Renderer {
			get;
			internal set;
		}
	}
}
