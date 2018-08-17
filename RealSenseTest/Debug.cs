using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RealSenseTest {
	static class Debug {
		public static bool FakePosition;
		public static bool FakePoints;

		static Debug() {
			FakePosition = false;
			FakePoints = false;
		}
	}
}
