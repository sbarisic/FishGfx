namespace FishGfx.Im3d;

public readonly record struct Im3dReleaseTransition(uint Id, bool Suppressed);

/// <summary>
/// Tracks im3d's final active application ID across frame boundaries. A release
/// exists only when a previously active ID becomes invalid after EndFrame.
/// </summary>
public sealed class Im3dInteractionLifecycle
{
	private uint suppressedReleaseId = Im3dInteraction.InvalidId;

	public uint PreviousActiveId { get; private set; } = Im3dInteraction.InvalidId;

	public void SuppressRelease(uint id)
	{
		if (id != Im3dInteraction.InvalidId)
		{
			suppressedReleaseId = id;
		}
	}

	public Im3dReleaseTransition? ObserveAfterEndFrame(uint currentActiveId)
	{
		Im3dReleaseTransition? transition = null;
		if (PreviousActiveId != Im3dInteraction.InvalidId
			&& currentActiveId == Im3dInteraction.InvalidId)
		{
			bool suppressed = PreviousActiveId == suppressedReleaseId;
			transition = new Im3dReleaseTransition(PreviousActiveId, suppressed);
			if (suppressed)
			{
				suppressedReleaseId = Im3dInteraction.InvalidId;
			}
		}

		PreviousActiveId = currentActiveId;
		return transition;
	}

	public void Reset()
	{
		PreviousActiveId = Im3dInteraction.InvalidId;
		suppressedReleaseId = Im3dInteraction.InvalidId;
	}
}
