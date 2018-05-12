using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Glfw3;

namespace FishGfx.System {
	public static class Events {
		public static void Poll() {
			Glfw.PollEvents();
		}
	}
}
