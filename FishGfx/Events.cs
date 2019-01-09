using Glfw3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FishGfx {
	public static class Events {
		public static void Poll() {
			Glfw.PollEvents();
		}
	}
}
