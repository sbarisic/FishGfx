using System;

namespace FishGfx.Graphics;

/// <summary>
/// A replayable render operation. Commands do not own resources referenced by the operation.
/// </summary>
public abstract class RenderCommand
{
	public abstract void Execute(RenderPass pass);
}

internal static class RenderCommandReplay
{
	internal static void ValidateCommand(RenderCommand command)
	{
		ArgumentNullException.ThrowIfNull(command);
	}

	internal static void Begin(string containerName, ref bool isExecuting)
	{
		if (isExecuting)
		{
			throw new InvalidOperationException($"A {containerName} cannot execute recursively.");
		}

		isExecuting = true;
	}

	internal static void End(ref bool isExecuting)
	{
		isExecuting = false;
	}
}
