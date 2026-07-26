using FishGfx.Cad;

namespace FishGfx.ManifoldCad;

internal sealed class CollectorDraftState
{
	internal CollectorDraftState(
		Guid systemId,
		Guid? inletId,
		CadFrame originalFrame
	)
	{
		SystemId = systemId;
		InletId = inletId;
		OriginalFrame = originalFrame;
		Frame = originalFrame;
	}

	internal Guid SystemId { get; }

	internal Guid? InletId { get; }

	internal CadFrame OriginalFrame { get; }

	internal CadFrame Frame { get; set; }

}
