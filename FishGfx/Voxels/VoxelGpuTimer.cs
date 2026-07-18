using System;
using FishGfx.Graphics;

namespace FishGfx.Voxels;

internal sealed class VoxelGpuTimer : IDisposable
{
	private const int QueryCount = 6;
	private readonly GraphicsQuery[] queries;
	private readonly bool[] pending;
	private int nextQuery;
	private bool disposed;

	internal VoxelGpuTimer(GraphicsContext graphics)
	{
		ArgumentNullException.ThrowIfNull(graphics);
		queries = new GraphicsQuery[QueryCount];
		pending = new bool[QueryCount];

		for (int index = 0; index < queries.Length; index++)
		{
			queries[index] = graphics.CreateQuery(GraphicsQueryType.TimeElapsed);
		}
	}

	internal bool Enabled { get; set; }

	internal double LastMilliseconds { get; private set; }

	internal int Begin(RenderPass pass, out IDisposable scope)
	{
		ArgumentNullException.ThrowIfNull(pass);
		Poll();
		scope = null;

		if (!Enabled)
		{
			return -1;
		}

		for (int attempt = 0; attempt < queries.Length; attempt++)
		{
			int index = (nextQuery + attempt) % queries.Length;

			if (pending[index])
			{
				continue;
			}

			nextQuery = (index + 1) % queries.Length;
			scope = pass.BeginQuery(queries[index]);
			return index;
		}

		return -1;
	}

	internal void End(int queryIndex, IDisposable scope)
	{
		if (queryIndex < 0)
		{
			return;
		}

		scope.Dispose();
		pending[queryIndex] = true;
	}

	internal void Poll()
	{
		for (int index = 0; index < queries.Length; index++)
		{
			if (!pending[index] || !queries[index].IsResultAvailable)
			{
				continue;
			}

			LastMilliseconds = queries[index].GetResult() / 1_000_000.0;
			pending[index] = false;
		}
	}

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}

		disposed = true;

		foreach (GraphicsQuery query in queries)
		{
			query.Dispose();
		}
	}
}
