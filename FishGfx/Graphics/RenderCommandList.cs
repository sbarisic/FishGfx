using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace FishGfx.Graphics;

/// <summary>
/// A mutable recorder for render commands. Only an active render pass can replay it.
/// </summary>
public sealed partial class RenderCommandList
{
	private readonly List<RenderCommand> commands = new();
	private readonly ReadOnlyCollection<RenderCommand> readOnlyCommands;
	private bool isExecuting;

	public RenderCommandList()
	{
		readOnlyCommands = commands.AsReadOnly();
	}

	public IReadOnlyList<RenderCommand> Commands => readOnlyCommands;

	public int Count => commands.Count;

	public bool IsExecuting => isExecuting;

	public RenderCommand this[int index] => commands[index];

	public RenderCommandBatch Snapshot()
	{
		return new RenderCommandBatch(commands);
	}

	public T Add<T>(T command)
		where T : RenderCommand
	{
		EnsureMutable();
		ArgumentNullException.ThrowIfNull(command);

		commands.Add(command);

		return command;
	}

	public bool Remove(RenderCommand command)
	{
		EnsureMutable();
		ArgumentNullException.ThrowIfNull(command);

		return commands.Remove(command);
	}

	public void RemoveAt(int index)
	{
		EnsureMutable();
		commands.RemoveAt(index);
	}

	public void Clear()
	{
		EnsureMutable();
		commands.Clear();
	}

	public ClearCommand RecordClear(
		Color color,
		bool clearColor = true,
		bool clearDepth = true,
		bool clearStencil = true,
		float depth = 1,
		int stencil = 0
	)
	{
		return Add(new ClearCommand(color, clearColor, clearDepth, clearStencil, depth, stencil));
	}

	public ClearDepthCommand RecordClearDepth(float value = 1)
	{
		return Add(new ClearDepthCommand(value));
	}

	public ClearStencilCommand RecordClearStencil(int value = 0)
	{
		return Add(new ClearStencilCommand(value));
	}

	public RenderStateScopeCommand RecordStateScope(
		RenderState state,
		Action<RenderCommandList> record
	)
	{
		ArgumentNullException.ThrowIfNull(record);

		RenderCommandList nestedCommands = new();
		record(nestedCommands);

		return RecordStateScope(state, nestedCommands.Snapshot());
	}

	public RenderStateScopeCommand RecordStateScope(
		RenderState state,
		RenderCommandBatch commands
	)
	{
		return Add(new RenderStateScopeCommand(state, commands));
	}

	internal void BeginExecution()
	{
		RenderCommandReplay.Begin("render command list", ref isExecuting);
	}

	internal void EndExecution()
	{
		RenderCommandReplay.End(ref isExecuting);
	}

	private void EnsureMutable()
	{
		if (isExecuting)
		{
			throw new InvalidOperationException(
				"A render command list cannot be modified while it is executing."
			);
		}
	}
}
