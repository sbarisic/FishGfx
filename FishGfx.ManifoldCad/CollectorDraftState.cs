using FishGfx.Cad;

namespace FishGfx.ManifoldCad;

internal sealed class CollectorDraftState
{
	internal CollectorDraftState(
		Guid systemId,
		Guid? inletId,
		CadFrame originalFrame,
		CadPoint3 originalEuler
	)
	{
		SystemId = systemId;
		InletId = inletId;
		OriginalFrame = originalFrame;
		Frame = originalFrame;
		EulerDegrees = originalEuler;
	}

	internal Guid SystemId { get; }

	internal Guid? InletId { get; }

	internal CadFrame OriginalFrame { get; }

	internal CadFrame Frame { get; set; }

	internal CadPoint3 EulerDegrees { get; set; }
}
