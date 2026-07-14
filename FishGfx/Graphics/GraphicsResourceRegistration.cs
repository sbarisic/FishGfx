using System;
using System.Threading;

namespace FishGfx.Graphics;

internal sealed class GraphicsResourceRegistration
{
	private const int Active = 0;
	private const int DeletionRequested = 1;
	private const int Deleted = 2;

	private readonly Action<uint> deleteHandle;
	private readonly uint handle;
	private readonly GraphicsContext owner;
	private readonly WeakReference<GraphicsResource> resource;
	private int state;

	private GraphicsResourceRegistration(
		GraphicsContext owner,
		GraphicsResource resource,
		Action<uint> deleteHandle
	)
	{
		this.owner = owner;
		this.resource = new WeakReference<GraphicsResource>(resource);
		this.deleteHandle = deleteHandle;
		handle = resource.Handle;
	}

	internal static GraphicsResourceRegistration Create(
		GraphicsContext owner,
		GraphicsResource resource
	)
	{
		ArgumentNullException.ThrowIfNull(owner);
		ArgumentNullException.ThrowIfNull(resource);

		Action<uint> deleteHandle = resource switch
		{
			GraphicsBuffer => DeleteBuffer,
			Framebuffer => DeleteFramebuffer,
			GraphicsQuery => DeleteQuery,
			ShaderProgram => DeleteProgram,
			ShaderStage => DeleteShader,
			Texture => DeleteTexture,
			VertexArray => DeleteVertexArray,
			_ => throw new NotSupportedException(
				$"Resource type '{resource.GetType().FullName}' is not registered for backend deletion."
			),
		};

		return new GraphicsResourceRegistration(
			owner,
			resource,
			deleteHandle
		);
	}

	internal void DeleteOnOwnerContext()
	{
		if (Volatile.Read(ref state) == Deleted)
		{
			return;
		}

		owner.EnsureCurrent();

		try
		{
			if (resource.TryGetTarget(out GraphicsResource target))
			{
				target.DeleteOnOwnerContext();
			}
			else
			{
				deleteHandle(handle);
			}
		}
		finally
		{
			Volatile.Write(ref state, Deleted);
		}
	}

	internal void DisposeResource()
	{
		if (resource.TryGetTarget(out GraphicsResource target))
		{
			target.Dispose();

			return;
		}

		RequestDeletion();
	}

	internal void RequestDeletion()
	{
		if (Interlocked.CompareExchange(
			ref state,
			DeletionRequested,
			Active
		) != Active)
		{
			return;
		}

		owner.EnqueueDeletion(this);
	}

	private static void DeleteBuffer(uint handle)
	{
		Internal_OpenGL.GL.DeleteBuffer(handle);
	}

	private static void DeleteFramebuffer(uint handle)
	{
		Internal_OpenGL.GL.DeleteFramebuffer(handle);
	}

	private static void DeleteProgram(uint handle)
	{
		Internal_OpenGL.GL.DeleteProgram(handle);
	}

	private static void DeleteQuery(uint handle)
	{
		Internal_OpenGL.GL.DeleteQuery(handle);
	}

	private static void DeleteShader(uint handle)
	{
		Internal_OpenGL.GL.DeleteShader(handle);
	}

	private static void DeleteTexture(uint handle)
	{
		Internal_OpenGL.GL.DeleteTexture(handle);
	}

	private static void DeleteVertexArray(uint handle)
	{
		Internal_OpenGL.GL.DeleteVertexArray(handle);
	}
}
