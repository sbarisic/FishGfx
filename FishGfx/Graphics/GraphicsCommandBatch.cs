using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace FishGfx.Graphics
{
	/// <summary>
	/// An immutable snapshot of graphics commands that can be replayed repeatedly.
	/// </summary>
	public sealed class GraphicsCommandBatch
	{
		private readonly GraphicsCommand[] commands;
		private readonly ReadOnlyCollection<GraphicsCommand> readOnlyCommands;

		public GraphicsCommandBatch(IEnumerable<GraphicsCommand> commands)
		{
			if (commands == null)
				throw new ArgumentNullException(nameof(commands));

			this.commands = commands.ToArray();

			if (this.commands.Any(command => command == null))
				throw new ArgumentException("Command batches cannot contain null commands.", nameof(commands));
			GraphicsCommandRunner.ValidateStateBalance(this.commands);

			readOnlyCommands = Array.AsReadOnly(this.commands);
		}

		public IReadOnlyList<GraphicsCommand> Commands => readOnlyCommands;
		public int Count => commands.Length;
		public bool IsExecuting { get; private set; }
		public GraphicsCommand this[int index] => commands[index];

		public void Execute(RenderPass pass)
		{
			if (pass == null) throw new ArgumentNullException(nameof(pass));
			if (IsExecuting) throw new InvalidOperationException("A graphics command batch cannot execute recursively.");
			IsExecuting = true;
			try { GraphicsCommandRunner.Execute(commands, command => command.Execute(pass)); }
			finally { IsExecuting = false; }
		}

		public void Execute()
		{
			if (IsExecuting)
				throw new InvalidOperationException("A graphics command batch cannot execute recursively.");

			IsExecuting = true;

			try
			{
				GraphicsCommandRunner.Execute(commands, command => command.Execute());
			}
			finally
			{
				IsExecuting = false;
			}
		}
	}
}
