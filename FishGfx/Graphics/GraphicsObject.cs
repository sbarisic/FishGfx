using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FishGfx;
using Silk.NET.OpenGL;

namespace FishGfx.Graphics
{
	public abstract class GraphicsObject : IDisposable
	{
		public uint ID;
		bool Collected = false;

		~GraphicsObject()
		{
			Dispose();
		}

		public virtual void Bind()
		{
			throw new InvalidOperationException("Unimplemented function call");
		}

		public virtual void Unbind()
		{
			throw new InvalidOperationException("Unimplemented function call");
		}

		protected void SetLabel(string Lbl)
		{
#if DEBUG
			// Labels are optional diagnostics and must not leak binding-specific APIs.
#endif
		}

		public void Dispose()
		{
			if (Collected)
				return;
			Collected = true;

			GC.SuppressFinalize(this);
			RenderAPI.EnqueueCollection(this);
		}

		public abstract void GraphicsDispose();
	}
}
