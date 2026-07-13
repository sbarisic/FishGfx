namespace FishGfx.FishUI
{
	/// <summary>No-op FishUI event sink for applications that use control events directly.</summary>
	public sealed class FishGfxFishUIEvents : global::FishUI.IFishUIEvents
	{
		public void Broadcast(
			global::FishUI.FishUI ui,
			global::FishUI.Controls.Control control,
			string name,
			object[] arguments
		) { }
	}
}
