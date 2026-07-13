namespace FishGfx.Graphics
{
	/// <summary>
	/// A replayable graphics operation. Commands do not own resources referenced by the operation.
	/// </summary>
	public abstract class GraphicsCommand
	{
		public virtual void Execute(RenderPass pass)
		{
			if (pass == null) throw new System.ArgumentNullException(nameof(pass));
			Execute();
		}

		public abstract void Execute();
	}

	internal static class GraphicsCommandRunner
	{
		internal static void ValidateStateBalance(System.Collections.Generic.IEnumerable<GraphicsCommand> commands)
		{
			int depth = 0;
			foreach (GraphicsCommand command in commands)
			{
				if (command is PushRenderStateCommand) depth++;
				else if (command is PopRenderStateCommand && --depth < 0)
					throw new System.InvalidOperationException("A recorded render-state pop has no matching push.");
			}
			if (depth != 0)
				throw new System.InvalidOperationException("Every recorded render-state push must have a matching pop.");
		}

		internal static void Execute(System.Collections.Generic.IEnumerable<GraphicsCommand> commands, System.Action<GraphicsCommand> execute)
		{
			ValidateStateBalance(commands);
			int initialDepth = Gfx.GetRenderStateCount();
			try
			{
				foreach (GraphicsCommand command in commands) execute(command);
			}
			finally
			{
				if (Gfx.GetRenderStateCount() < initialDepth)
					throw new System.InvalidOperationException("Command execution popped render state owned by its caller.");
				while (Gfx.GetRenderStateCount() > initialDepth)
					Gfx.PopRenderState();
			}
		}
	}
}
