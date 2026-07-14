using System;
using System.Collections.Generic;

namespace FishGfx.Graphics;

public sealed partial class RenderPass
{
	private readonly HashSet<RenderCommand> executingCommands = new(
		ReferenceEqualityComparer.Instance
	);

	public void Execute(RenderCommand command)
	{
		EnsureActive();
		RenderCommandReplay.ValidateCommand(command);

		if (!executingCommands.Add(command))
		{
			throw new InvalidOperationException("A render command cannot execute recursively.");
		}

		try
		{
			command.Execute(this);
		}
		finally
		{
			executingCommands.Remove(command);
		}
	}

	public void Execute(RenderCommandList commands)
	{
		EnsureActive();
		ArgumentNullException.ThrowIfNull(commands);
		commands.BeginExecution();

		try
		{
			ExecuteCommands(commands.Commands);
		}
		finally
		{
			commands.EndExecution();
		}
	}

	public void Execute(RenderCommandBatch commands)
	{
		EnsureActive();
		ArgumentNullException.ThrowIfNull(commands);
		commands.BeginExecution();

		try
		{
			ExecuteCommands(commands.Commands);
		}
		finally
		{
			commands.EndExecution();
		}
	}

	public void Execute(RenderItem item)
	{
		EnsureActive();
		ArgumentNullException.ThrowIfNull(item);

		using IDisposable modelScope = PushModel(item.Model);

		Execute(item.Batch);
	}

	public void Execute(
		RenderQueue queue,
		RenderQueueBucket bucket,
		IComparer<RenderItem> comparer = null
	)
	{
		EnsureActive();
		ArgumentNullException.ThrowIfNull(queue);
		queue.BeginExecution();

		try
		{
			ExecuteQueueBucket(queue, bucket, comparer);
		}
		finally
		{
			queue.EndExecution();
		}
	}

	public void Execute(RenderQueue queue)
	{
		EnsureActive();
		ArgumentNullException.ThrowIfNull(queue);
		queue.BeginExecution();

		try
		{
			ExecuteQueueBucket(
				queue,
				RenderQueueBucket.Opaque,
				GetDefaultComparer(RenderQueueBucket.Opaque)
			);
			ExecuteQueueBucket(
				queue,
				RenderQueueBucket.Transparent,
				GetDefaultComparer(RenderQueueBucket.Transparent)
			);

			foreach (RenderQueueBucket bucket in queue.Buckets)
			{
				if (bucket != RenderQueueBucket.Opaque
					&& bucket != RenderQueueBucket.Transparent)
				{
					ExecuteQueueBucket(queue, bucket, null);
				}
			}
		}
		finally
		{
			queue.EndExecution();
		}
	}

	private void ExecuteCommands(IReadOnlyList<RenderCommand> commands)
	{
		foreach (RenderCommand command in commands)
		{
			Execute(command);
		}
	}

	private void ExecuteQueueBucket(
		RenderQueue queue,
		RenderQueueBucket bucket,
		IComparer<RenderItem> comparer
	)
	{
		RenderItem[] executionOrder = queue.CreateExecutionOrder(bucket, comparer);

		foreach (RenderItem item in executionOrder)
		{
			Execute(item);
		}
	}

	private IComparer<RenderItem> GetDefaultComparer(RenderQueueBucket bucket)
	{
		if (bucket == RenderQueueBucket.Opaque)
		{
			return RenderItemComparers.OpaqueStateThenFrontToBack(View);
		}

		if (bucket == RenderQueueBucket.Transparent)
		{
			return RenderItemComparers.TransparentBackToFront(View);
		}

		return null;
	}
}
