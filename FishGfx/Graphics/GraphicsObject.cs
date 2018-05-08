using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenGL;
using FishGfx.System;

namespace FishGfx.Graphics {
	public abstract class GraphicsObject : IDisposable {
		public uint ID;
		bool Collected = false;

		~GraphicsObject() {
			Dispose();
		}

		public virtual void Bind() {
			throw new InvalidOperationException("Unimplemented function call");
		}

		public virtual void Unbind() {
			throw new InvalidOperationException("Unimplemented function call");
		}

		public void SetLabel(ObjectIdentifier ObjID, string Lbl) {
#if DEBUG
			Gl.ObjectLabel(ObjID, ID, Lbl.Length, Lbl);
#endif
		}
		
		public void Dispose() {
			if (Collected)
				return;
			Collected = true;

			GC.SuppressFinalize(this);
			RenderAPI.EnqueueCollection(this);
		}

		public abstract void GraphicsDispose();
	}
}
