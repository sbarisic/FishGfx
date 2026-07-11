namespace FishGfx.Graphics
{
	/// <summary>
	/// A replayable graphics operation. Commands do not own resources referenced by the operation.
	/// </summary>
	public abstract class GraphicsCommand
	{
		public abstract void Execute();
	}
}
