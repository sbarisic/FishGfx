using FishGfx.Cad;

namespace FishGfx.ManifoldCad;

internal enum CurveDisplayOwnerKind
{
	RunnerBezier,
	CollectorBranch,
	CollectorSystem,
}

internal enum CurveDisplayFreshness
{
	Current,
	Stale,
}

internal enum CurveDisplayValidity
{
	Valid,
	Invalid,
}

internal enum CurveDisplayControlKind
{
	Start,
	Control1,
	Control2,
	End,
}

internal readonly record struct CurveDisplayControlKey(
	int SpanIndex,
	CurveDisplayControlKind Kind
);

internal readonly record struct CurveOverlayIdentity(
	CurveDisplayOwnerKind OwnerKind,
	Guid OwnerId,
	Guid? ElementId,
	long ProjectEpoch,
	long EditRevision,
	CadGenerationStamp GenerationStamp
)
{
	internal bool HasSameSelection(CurveOverlayIdentity other)
	{
		return OwnerKind == other.OwnerKind
			&& OwnerId == other.OwnerId
			&& ElementId == other.ElementId
			&& ProjectEpoch == other.ProjectEpoch;
	}
}

internal sealed record CurveDisplaySpan(
	CadPoint3 Start,
	CadPoint3 Control1,
	CadPoint3 Control2,
	CadPoint3 End,
	IReadOnlyList<CadPoint3> Samples,
	CurveDisplayValidity Validity
);

internal sealed record CurveDisplayControl(
	CurveDisplayControlKey Key,
	CadPoint3 Position,
	bool Editable,
	RunnerPathPointKind? RunnerPointKind
);

internal sealed record CurveDisplayOverlay(
	CurveOverlayIdentity Identity,
	CurveDisplayFreshness Freshness,
	IReadOnlyList<CurveDisplaySpan> Spans,
	IReadOnlyList<CurveDisplayControl> Controls,
	CadFrame? RunnerEntryFrame,
	CadFrame? RunnerExitFrame
)
{
	internal bool IsInvalid => Spans.Count == 0 || Spans.Any(span =>
		span.Validity == CurveDisplayValidity.Invalid);

	internal static CurveDisplayOverlay Runner(
		CurveOverlayIdentity identity,
		RunnerFeature feature,
		bool endpointConstrained,
		bool exactCurrent,
		bool invalid
	)
	{
		ArgumentNullException.ThrowIfNull(feature);
		CurveDisplayValidity validity = invalid
			? CurveDisplayValidity.Invalid
			: CurveDisplayValidity.Valid;
		CurveDisplaySpan span = CreateSpan(
			feature.EntryFrame.Origin,
			feature.Control1,
			feature.Control2,
			feature.ExitFrame.Origin,
			validity
		);
		CurveDisplayControl[] controls =
		{
			Control(0, CurveDisplayControlKind.Start, span.Start, false, RunnerPathPointKind.Start),
			Control(0, CurveDisplayControlKind.Control1, span.Control1, true, RunnerPathPointKind.Control1),
			Control(0, CurveDisplayControlKind.Control2, span.Control2, !endpointConstrained, RunnerPathPointKind.Control2),
			Control(0, CurveDisplayControlKind.End, span.End, !endpointConstrained, RunnerPathPointKind.End),
		};
		return new CurveDisplayOverlay(
			identity,
			exactCurrent ? CurveDisplayFreshness.Current : CurveDisplayFreshness.Stale,
			Array.AsReadOnly(new[] { span }),
			Array.AsReadOnly(controls),
			feature.EntryFrame,
			feature.ExitFrame
		);
	}

	internal static CurveDisplayOverlay CollectorBranch(
		CurveOverlayIdentity identity,
		CadCollectorSystem system,
		CadCollectorInlet inlet,
		bool exactCurrent
	)
	{
		ArgumentNullException.ThrowIfNull(system);
		ArgumentNullException.ThrowIfNull(inlet);
		CadFrame inletFrame = system.GetWorldInletFrame(inlet);
		CadCollectorBranchPath path = inlet.BranchPath;
		IReadOnlyList<CadCollectorWorldBranchSpan> source = path == null
			? Array.Empty<CadCollectorWorldBranchSpan>()
			: CadCollectorBranchSolver.ToWorldSpans(
				path,
				system.OutletFrame,
				inletFrame.Origin
			);
		CurveDisplayValidity validity = path?.IsFeasible == true
			? CurveDisplayValidity.Valid
			: CurveDisplayValidity.Invalid;
		CurveDisplaySpan[] spans = source.Select(span => CreateSpan(
			span.Start,
			span.Control1,
			span.Control2,
			span.End,
			validity
		)).ToArray();
		List<CurveDisplayControl> controls = new(spans.Length * 4);
		for (int index = 0; index < spans.Length; ++index)
		{
			CurveDisplaySpan span = spans[index];
			controls.Add(Control(index, CurveDisplayControlKind.Start, span.Start, false, null));
			controls.Add(Control(index, CurveDisplayControlKind.Control1, span.Control1, false, null));
			controls.Add(Control(index, CurveDisplayControlKind.Control2, span.Control2, false, null));
			controls.Add(Control(index, CurveDisplayControlKind.End, span.End, false, null));
		}
		return new CurveDisplayOverlay(
			identity,
			exactCurrent ? CurveDisplayFreshness.Current : CurveDisplayFreshness.Stale,
			Array.AsReadOnly(spans),
			controls.AsReadOnly(),
			null,
			null
		);
	}

	internal static CurveDisplayOverlay CollectorSystem(
		CurveOverlayIdentity identity,
		CadCollectorSystem system,
		bool exactCurrent
	)
	{
		ArgumentNullException.ThrowIfNull(system);
		List<CurveDisplaySpan> spans = new();
		foreach (CadCollectorInlet inlet in system.Inlets)
		{
			CadCollectorBranchPath path = inlet.BranchPath;
			if (path == null)
			{
				continue;
			}
			CadFrame inletFrame = system.GetWorldInletFrame(inlet);
			CurveDisplayValidity validity = path.IsFeasible
				? CurveDisplayValidity.Valid
				: CurveDisplayValidity.Invalid;
			foreach (CadCollectorWorldBranchSpan span in CadCollectorBranchSolver.ToWorldSpans(
				path,
				system.OutletFrame,
				inletFrame.Origin
			))
			{
				spans.Add(CreateSpan(
					span.Start,
					span.Control1,
					span.Control2,
					span.End,
					validity
				));
			}
		}
		return new CurveDisplayOverlay(
			identity,
			exactCurrent ? CurveDisplayFreshness.Current : CurveDisplayFreshness.Stale,
			spans.AsReadOnly(),
			Array.Empty<CurveDisplayControl>(),
			null,
			null
		);
	}

	internal CurveDisplayOverlay MarkInvalid()
	{
		return this with
		{
			Spans = Array.AsReadOnly(Spans.Select(span => span with
			{
				Validity = CurveDisplayValidity.Invalid,
			}).ToArray()),
		};
	}

	private static CurveDisplayControl Control(
		int spanIndex,
		CurveDisplayControlKind kind,
		CadPoint3 position,
		bool editable,
		RunnerPathPointKind? runnerPointKind
	)
	{
		return new CurveDisplayControl(
			new CurveDisplayControlKey(spanIndex, kind),
			position,
			editable,
			runnerPointKind
		);
	}

	private static CurveDisplaySpan CreateSpan(
		CadPoint3 start,
		CadPoint3 control1,
		CadPoint3 control2,
		CadPoint3 end,
		CurveDisplayValidity validity
	)
	{
		const int segmentCount = 48;
		CadPoint3[] samples = new CadPoint3[segmentCount + 1];
		for (int index = 0; index <= segmentCount; ++index)
		{
			double parameter = index / (double)segmentCount;
			double inverse = 1 - parameter;
			samples[index] = start * (inverse * inverse * inverse)
				+ control1 * (3 * inverse * inverse * parameter)
				+ control2 * (3 * inverse * parameter * parameter)
				+ end * (parameter * parameter * parameter);
		}
		return new CurveDisplaySpan(
			start,
			control1,
			control2,
			end,
			Array.AsReadOnly(samples),
			validity
		);
	}
}

internal sealed class CurveDisplayOverlayState
{
	internal CurveOverlayIdentity? Selection { get; private set; }

	internal CurveDisplayOverlay Overlay { get; private set; }

	internal CurveDisplayControlKey? SelectedControl { get; private set; }

	internal bool DraftActive { get; private set; }

	internal bool OverlayMatchesSelection => Selection.HasValue
		&& Overlay != null
		&& Overlay.Identity == Selection.Value;

	internal bool HasDisplayForSelection => Selection.HasValue
		&& Overlay != null
		&& Overlay.Identity.HasSameSelection(Selection.Value);

	internal CurveDisplayFreshness EffectiveFreshness => (Overlay == null
		|| !OverlayMatchesSelection
		|| Overlay.Freshness == CurveDisplayFreshness.Stale)
			? CurveDisplayFreshness.Stale
			: CurveDisplayFreshness.Current;

	internal void Select(CurveOverlayIdentity? selection)
	{
		if (!selection.HasValue)
		{
			Clear();
			return;
		}
		if (!Selection.HasValue
			|| !Selection.Value.HasSameSelection(selection.Value))
		{
			Overlay = null;
			SelectedControl = null;
			DraftActive = false;
		}
		else if (Overlay != null && Overlay.Identity != selection.Value)
		{
			Overlay = Overlay with { Freshness = CurveDisplayFreshness.Stale };
		}
		Selection = selection;
	}

	internal bool TryPublish(CurveDisplayOverlay overlay)
	{
		ArgumentNullException.ThrowIfNull(overlay);
		if (DraftActive || !Selection.HasValue || overlay.Identity != Selection.Value)
		{
			return false;
		}
		Overlay = overlay;
		if (SelectedControl.HasValue
			&& !overlay.Controls.Any(control => control.Key == SelectedControl.Value))
		{
			SelectedControl = null;
		}
		return true;
	}

	internal bool TryBeginDraft()
	{
		if (!OverlayMatchesSelection || Overlay?.IsInvalid != false)
		{
			return false;
		}
		DraftActive = true;
		return true;
	}

	internal void EndDraft()
	{
		DraftActive = false;
	}

	internal void SelectControl(CurveDisplayControlKey? control)
	{
		SelectedControl = control;
	}

	internal void MarkInvalid(Guid ownerId, Guid? elementId)
	{
		if (Overlay?.Identity.OwnerId == ownerId
			&& (!elementId.HasValue || Overlay.Identity.ElementId == elementId))
		{
			Overlay = Overlay.MarkInvalid();
		}
	}

	internal void Clear()
	{
		Selection = null;
		Overlay = null;
		SelectedControl = null;
		DraftActive = false;
	}
}
