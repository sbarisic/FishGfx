using System;

namespace FishGfx.Graphics;

public sealed class ClearCommand : RenderCommand
{
	public ClearCommand(
		Color color,
		bool clearColor = true,
		bool clearDepth = true,
		bool clearStencil = true,
		float depth = 1,
		int stencil = 0
	)
	{
		Color = color;
		ClearColor = clearColor;
		ClearDepth = clearDepth;
		ClearStencil = clearStencil;
		Depth = depth;
		Stencil = stencil;
	}

	public Color Color { get; }

	public bool ClearColor { get; }

	public bool ClearDepth { get; }

	public bool ClearStencil { get; }

	public float Depth { get; }

	public int Stencil { get; }

	public override void Execute(RenderPass pass)
	{
		ArgumentNullException.ThrowIfNull(pass);
		pass.Clear(Color, ClearColor, ClearDepth, ClearStencil, Depth, Stencil);
	}
}

public sealed class ClearDepthCommand : RenderCommand
{
	public ClearDepthCommand(float value = 1)
	{
		Value = value;
	}

	public float Value { get; }

	public override void Execute(RenderPass pass)
	{
		ArgumentNullException.ThrowIfNull(pass);
		pass.Clear(default, false, true, false, Value);
	}
}

public sealed class ClearStencilCommand : RenderCommand
{
	public ClearStencilCommand(int value = 0)
	{
		Value = value;
	}

	public int Value { get; }

	public override void Execute(RenderPass pass)
	{
		ArgumentNullException.ThrowIfNull(pass);
		pass.Clear(default, false, false, true, stencil: Value);
	}
}

public sealed class RenderStateScopeCommand : RenderCommand
{
	public RenderStateScopeCommand(RenderState state, RenderCommandBatch commands)
	{
		state.Validate();
		Commands = commands ?? throw new ArgumentNullException(nameof(commands));
		State = state;
	}

	public RenderState State { get; }

	public RenderCommandBatch Commands { get; }

	public override void Execute(RenderPass pass)
	{
		ArgumentNullException.ThrowIfNull(pass);

		using IDisposable scope = pass.PushState(State);

		pass.Execute(Commands);
	}
}
