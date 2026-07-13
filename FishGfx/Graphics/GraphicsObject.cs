using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using FishGfx;
using Silk.NET.OpenGL;

namespace FishGfx.Graphics
{
	public abstract class GraphicsObject : IDisposable
	{
		private int collected;
		private bool deleted;

		protected GraphicsObject()
		{
			Owner = GraphicsContext.CurrentOrNull;
			Owner?.Register(this);
		}

		~GraphicsObject() => Dispose();

		public uint ID;
		public GraphicsContext Owner { get; }
		public bool IsDisposed => Volatile.Read(ref collected) != 0;

		public virtual void Bind() => throw new InvalidOperationException("Unimplemented function call");
		public virtual void Unbind() => throw new InvalidOperationException("Unimplemented function call");

		internal void EnsureOwner(GraphicsContext context)
		{
			if (IsDisposed)
				throw new ObjectDisposedException(GetType().Name);
			if (Owner != null && !ReferenceEquals(Owner, context))
				throw new InvalidOperationException("The graphics resource belongs to another graphics context.");
		}

		protected GraphicsContext EnsureCurrentOwner()
		{
			GraphicsContext context = GraphicsContext.Current;
			EnsureOwner(context);
			return context;
		}
		protected void SetLabel(string label)
		{
#if DEBUG
			// Labels are optional diagnostics and must not leak binding-specific APIs.
#endif
		}

		public void Dispose()
		{
			if (Interlocked.Exchange(ref collected, 1) != 0)
				return;
			GC.SuppressFinalize(this);
			if (Owner != null)
				Owner.EnqueueDeletion(this);
			else
				RenderAPI.EnqueueCollection(this);
		}

		internal void DeleteOnOwnerContext()
		{
			if (deleted)
				return;
			deleted = true;
			GraphicsDispose();
			ID = 0;
		}

		public abstract void GraphicsDispose();
	}
}
