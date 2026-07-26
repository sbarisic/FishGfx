using FishGfx.Cad;

namespace FishGfx.ManifoldCad;

internal sealed class PartTransformDraftState
{
	internal PartTransformDraftState(CadPart part)
	{
		ArgumentNullException.ThrowIfNull(part);
		PartId = part.Id;
		OriginalTransform = part.Transform;
		Transform = part.Transform;
	}

	internal Guid PartId { get; }

	internal CadTransform OriginalTransform { get; }

	internal CadTransform Transform { get; set; }
}
