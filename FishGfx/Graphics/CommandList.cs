using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FishGfx.Graphics {
	public class CommandList {
		List<Action> Commands = new List<Action>();

		internal void Enqueue(Action Cmd) {
			Commands.Add(Cmd);
		}

		public void Clear() {
			Commands.Clear();
		}

		public void Execute() {
			for (int i = 0; i < Commands.Count; i++)
				Commands[i].Invoke();
		}
	}
}
