using System;

namespace FishGfx.Graphics.Shadows;

internal sealed class DirectionalShadowGpuTimer : IDisposable
{
	private const int QueryCount = 6;
	private readonly GraphicsQuery[] queries;
	private readonly bool[] pending;
	private int nextQuery;
	private bool disposed;

	internal DirectionalShadowGpuTimer(GraphicsContext graphics)
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

	internal IDisposable Begin(RenderPass pass)
	{
		ObjectDisposedException.ThrowIf(disposed, this);
		ArgumentNullException.ThrowIfNull(pass);
		Poll();

		if (!Enabled)
		{
			return EmptyScope.Instance;
		}

		for (int attempt = 0; attempt < queries.Length; attempt++)
		{
			int index = (nextQuery + attempt) % queries.Length;

			if (pending[index])
			{
				continue;
			}

			nextQuery = (index + 1) % queries.Length;
			return new QueryScope(this, index, pass.BeginQuery(queries[index]));
		}

		return EmptyScope.Instance;
	}

	internal void Poll()
	{
		ObjectDisposedException.ThrowIf(disposed, this);

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

		for (int index = 0; index < queries.Length; index++)
		{
			queries[index].Dispose();
		}
	}

	private void End(int queryIndex, IDisposable scope)
	{
		try
		{
			scope.Dispose();
		}
		finally
		{
			pending[queryIndex] = true;
		}
	}

	private sealed class QueryScope : IDisposable
	{
		private DirectionalShadowGpuTimer timer;
		private IDisposable scope;
		private readonly int queryIndex;

		internal QueryScope(
			DirectionalShadowGpuTimer timer,
			int queryIndex,
			IDisposable scope)
		{
			this.timer = timer;
			this.queryIndex = queryIndex;
			this.scope = scope;
		}

		public void Dispose()
		{
			DirectionalShadowGpuTimer currentTimer =
				System.Threading.Interlocked.Exchange(ref timer, null);
			IDisposable currentScope =
				System.Threading.Interlocked.Exchange(ref scope, null);

			if (currentTimer != null)
			{
				currentTimer.End(queryIndex, currentScope);
			}
		}
	}

	private sealed class EmptyScope : IDisposable
	{
		internal static EmptyScope Instance { get; } = new();

		public void Dispose()
		{
		}
	}
}
