using System;
using System.Threading;

namespace FishGfx.Graphics;

public abstract class GraphicsResource : IDisposable
{
	private int disposeRequested;
	private bool deleted;
	private GraphicsResourceRegistration registration;

	protected GraphicsResource(GraphicsContext owner)
	{
		Owner = owner ?? throw new ArgumentNullException(nameof(owner));
		Owner.EnsureCurrent();
	}

	~GraphicsResource()
	{
		Dispose(false);
	}

	public GraphicsContext Owner { get; }

	public bool IsDisposed => Volatile.Read(ref disposeRequested) != 0;

	internal uint Handle { get; set; }

	protected void RegisterResource()
	{
		if (registration != null)
		{
			throw new InvalidOperationException(
				"The graphics resource is already registered."
			);
		}

		registration = Owner.Register(this);
	}

	internal void EnsureOwner(GraphicsContext context)
	{
		if (IsDisposed)
		{
			throw new ObjectDisposedException(GetType().Name);
		}

		if (!ReferenceEquals(Owner, context))
		{
			throw new InvalidOperationException("The graphics resource belongs to another graphics context.");
		}
	}

	protected GraphicsContext EnsureCurrentOwner()
	{
		Owner.EnsureCurrent();
		EnsureOwner(Owner);

		return Owner;
	}

	protected void SetLabel(string label)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(label);

#if DEBUG
		// Resource labels are optional backend diagnostics.
#endif
	}

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	internal void DeleteOnOwnerContext()
	{
		if (deleted)
		{
			return;
		}

		Owner.EnsureCurrent();
		deleted = true;

		DeleteResource();
		Handle = 0;
	}

	internal abstract void DeleteResource();

	private void Dispose(bool disposing)
	{
		if (Interlocked.Exchange(ref disposeRequested, 1) != 0)
		{
			return;
		}

		registration?.RequestDeletion();
	}
}
