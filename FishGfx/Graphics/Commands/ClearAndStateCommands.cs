namespace FishGfx.Graphics
{
	public sealed class ClearCommand : GraphicsCommand
	{
		public Color Color { get; }
		public bool ClearColor { get; }
		public bool ClearDepth { get; }
		public bool ClearStencil { get; }

		public ClearCommand(Color color, bool clearColor = true, bool clearDepth = true, bool clearStencil = true)
		{
			Color = color;
			ClearColor = clearColor;
			ClearDepth = clearDepth;
			ClearStencil = clearStencil;
		}

		public override void Execute() => Gfx.Clear(Color, ClearColor, ClearDepth, ClearStencil);
	}

	public sealed class ClearDepthCommand : GraphicsCommand
	{
		public float Value { get; }

		public ClearDepthCommand(float value = 1)
		{
			Value = value;
		}

		public override void Execute() => Gfx.ClearDepth(Value);
	}

	public sealed class ClearStencilCommand : GraphicsCommand
	{
		public int Value { get; }

		public ClearStencilCommand(int value = 0)
		{
			Value = value;
		}

		public override void Execute() => Gfx.ClearStencil(Value);
	}

	public sealed class PushRenderStateCommand : GraphicsCommand
	{
		public RenderState State { get; }

		public PushRenderStateCommand(RenderState state)
		{
			State = state;
		}

		public override void Execute() => Gfx.PushRenderState(State);
	}

	public sealed class PopRenderStateCommand : GraphicsCommand
	{
		public override void Execute() => Gfx.PopRenderState();
	}
}
