using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace FishGfx.Graphics;

/// <summary>
/// An immutable snapshot of render commands that can be replayed repeatedly by a render pass.
/// </summary>
public sealed class RenderCommandBatch
{
	private readonly RenderCommand[] commands;
	private readonly ReadOnlyCollection<RenderCommand> readOnlyCommands;
	private bool isExecuting;

	public RenderCommandBatch(IEnumerable<RenderCommand> commands)
	{
		ArgumentNullException.ThrowIfNull(commands);

		this.commands = commands.ToArray();

		if (this.commands.Any(command => command == null))
		{
			throw new ArgumentException("Render command batches cannot contain null commands.", nameof(commands));
		}

		readOnlyCommands = Array.AsReadOnly(this.commands);
	}

	public IReadOnlyList<RenderCommand> Commands => readOnlyCommands;

	public int Count => commands.Length;

	public bool IsExecuting => isExecuting;

	public RenderCommand this[int index] => commands[index];

	internal void BeginExecution()
	{
		RenderCommandReplay.Begin("render command batch", ref isExecuting);
	}

	internal void EndExecution()
	{
		RenderCommandReplay.End(ref isExecuting);
	}
}
