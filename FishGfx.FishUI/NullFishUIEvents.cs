namespace FishGfx.FishUI;

public sealed class NullFishUIEvents : global::FishUI.IFishUIEvents
{
	public void Broadcast(
		global::FishUI.FishUI ui,
		global::FishUI.Controls.Control control,
		string name,
		object[] arguments
	)
	{
	}
}
