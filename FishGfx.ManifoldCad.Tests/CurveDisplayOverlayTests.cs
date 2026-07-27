using FishGfx.Cad;
using Xunit;

namespace FishGfx.ManifoldCad.Tests;

public sealed class CurveDisplayOverlayTests
{
	[Fact]
	public void RevisionChangeRetainsMatchingSelectionAsStale()
	{
		Guid runnerId = Guid.NewGuid();
		Guid nodeId = Guid.NewGuid();
		CurveOverlayIdentity revisionOne = RunnerIdentity(runnerId, nodeId, 7, 1);
		CurveDisplayOverlayState state = new();
		state.Select(revisionOne);
		Assert.True(state.TryPublish(RunnerOverlay(revisionOne, nodeId, false)));
		Assert.False(state.DraftActive);

		CurveOverlayIdentity revisionTwo = RunnerIdentity(runnerId, nodeId, 7, 2);
		state.Select(revisionTwo);

		Assert.NotNull(state.Overlay);
		Assert.True(state.HasDisplayForSelection);
		Assert.False(state.OverlayMatchesSelection);
		Assert.Equal(CurveDisplayFreshness.Stale, state.EffectiveFreshness);
		Assert.False(state.TryPublish(RunnerOverlay(revisionOne, nodeId, false)));
		Assert.True(state.TryPublish(RunnerOverlay(revisionTwo, nodeId, false)));
	}

	[Fact]
	public void StaleExactStateDoesNotMakeAValidCurveInvalid()
	{
		Guid runnerId = Guid.NewGuid();
		Guid nodeId = Guid.NewGuid();
		CurveOverlayIdentity identity = RunnerIdentity(runnerId, nodeId, 2, 3);
		CurveDisplayOverlay validStale = RunnerOverlay(
			identity,
			nodeId,
			false,
			false,
			false);
		CurveDisplayOverlay invalidCurrent = RunnerOverlay(
			identity,
			nodeId,
			false,
			true,
			true);

		Assert.Equal(CurveDisplayFreshness.Stale, validStale.Freshness);
		Assert.False(validStale.IsInvalid);
		Assert.Equal(CurveDisplayFreshness.Current, invalidCurrent.Freshness);
		Assert.True(invalidCurrent.IsInvalid);
	}

	[Fact]
	public void ProjectEpochAndOwnerMustMatchBeforePublishing()
	{
		Guid runnerId = Guid.NewGuid();
		Guid nodeId = Guid.NewGuid();
		CurveOverlayIdentity expected = RunnerIdentity(runnerId, nodeId, 11, 4);
		CurveDisplayOverlayState state = new();
		state.Select(expected);

		Assert.False(state.TryPublish(RunnerOverlay(
			expected with { ProjectEpoch = 10 },
			nodeId,
			false)));
		Assert.False(state.TryPublish(RunnerOverlay(
			expected with { OwnerId = Guid.NewGuid() },
			nodeId,
			false)));
		Assert.Null(state.Overlay);
	}

	[Fact]
	public void DirtyDraftRejectsAsynchronousOverlayReplacement()
	{
		Guid runnerId = Guid.NewGuid();
		Guid nodeId = Guid.NewGuid();
		CurveOverlayIdentity identity = RunnerIdentity(runnerId, nodeId, 3, 9);
		CurveDisplayOverlayState state = new();
		state.Select(identity);
		CurveDisplayOverlay first = RunnerOverlay(identity, nodeId, false);
		Assert.True(state.TryPublish(first));
		Assert.True(state.TryBeginDraft());

		Assert.False(state.TryPublish(first with
		{
			Freshness = CurveDisplayFreshness.Current,
		}));
		Assert.Same(first, state.Overlay);
	}

	[Fact]
	public void CollectorConstrainedRunnerLocksEndpointButKeepsP1Editable()
	{
		Guid runnerId = Guid.NewGuid();
		Guid nodeId = Guid.NewGuid();
		CurveOverlayIdentity identity = RunnerIdentity(runnerId, nodeId, 1, 2);
		CurveDisplayOverlay overlay = RunnerOverlay(identity, nodeId, true);

		Assert.False(Control(overlay, RunnerPathPointKind.Start).Editable);
		Assert.True(Control(overlay, RunnerPathPointKind.Control1).Editable);
		Assert.False(Control(overlay, RunnerPathPointKind.Control2).Editable);
		Assert.False(Control(overlay, RunnerPathPointKind.End).Editable);
	}

	[Fact]
	public void CollectorBranchReconstructsEverySpanAsReadOnly()
	{
		CadCollectorSystem system = new()
		{
			OutletFrame = new CadFrame(
				new CadPoint3(100, 20, 5),
				new CadPoint3(1, 0, 0),
				new CadPoint3(0, 1, 0)),
		};
		CadCollectorInlet inlet = new()
		{
			LocalFrame = new CadFrame(
				new CadPoint3(-80, 15, 0),
				new CadPoint3(1, 0, 0),
				new CadPoint3(0, 1, 0)),
			BranchPath = new CadCollectorBranchPath
			{
				IsFeasible = true,
				Spans =
				{
					new CadCollectorBranchSpan
					{
						Control1Local = new CadPoint3(-60, 10, 0),
						Control2Local = new CadPoint3(-45, 8, 0),
						EndLocal = new CadPoint3(-30, 5, 0),
					},
					new CadCollectorBranchSpan
					{
						Control1Local = new CadPoint3(-20, 3, 0),
						Control2Local = new CadPoint3(-10, 0, 0),
						EndLocal = CadPoint3.Zero,
					},
				},
			},
		};
		system.Inlets.Add(inlet);
		CurveOverlayIdentity identity = new(
			CurveDisplayOwnerKind.CollectorBranch,
			system.Id,
			inlet.Id,
			5,
			system.GenerationRevision,
			system.GenerationStamp);

		CurveDisplayOverlay overlay = CurveDisplayOverlay.CollectorBranch(
			identity,
			system,
			inlet,
			true);

		Assert.Equal(2, overlay.Spans.Count);
		Assert.Equal(8, overlay.Controls.Count);
		Assert.All(overlay.Controls, control => Assert.False(control.Editable));
		Assert.Equal(overlay.Spans[0].End, overlay.Spans[1].Start);
		Assert.False(overlay.IsInvalid);
	}

	[Fact]
	public void MissingCollectorPathIsGeometricallyInvalid()
	{
		CadCollectorSystem system = new();
		CadCollectorInlet inlet = new();
		system.Inlets.Add(inlet);
		CurveOverlayIdentity identity = new(
			CurveDisplayOwnerKind.CollectorBranch,
			system.Id,
			inlet.Id,
			1,
			0,
			system.GenerationStamp);

		CurveDisplayOverlay overlay = CurveDisplayOverlay.CollectorBranch(
			identity,
			system,
			inlet,
			false);

		Assert.True(overlay.IsInvalid);
		Assert.Equal(CurveDisplayFreshness.Stale, overlay.Freshness);
	}

	private static CurveDisplayControl Control(
		CurveDisplayOverlay overlay,
		RunnerPathPointKind pointKind)
	{
		return overlay.Controls.Single(control => control.RunnerPointKind == pointKind);
	}

	private static CurveOverlayIdentity RunnerIdentity(
		Guid runnerId,
		Guid nodeId,
		long epoch,
		long revision)
	{
		return new CurveOverlayIdentity(
			CurveDisplayOwnerKind.RunnerBezier,
			runnerId,
			nodeId,
			epoch,
			revision,
			new CadGenerationStamp(CadGenerationOwnerKind.Runner, runnerId, revision));
	}

	private static CurveDisplayOverlay RunnerOverlay(
		CurveOverlayIdentity identity,
		Guid nodeId,
		bool constrained,
		bool exactCurrent = true,
		bool invalid = false)
	{
		RunnerSectionProfile profile = RunnerSectionProfile.FromCircular(
			new PipeProfile(42.4, 2));
		RunnerFeature feature = new(
			nodeId,
			RunnerFeatureKind.CubicBezier,
			new CadFrame(CadPoint3.Zero, new CadPoint3(1, 0, 0), new CadPoint3(0, 1, 0)),
			new CadFrame(new CadPoint3(100, 0, 0), new CadPoint3(1, 0, 0), new CadPoint3(0, 1, 0)),
			profile,
			profile,
			100,
			CadPoint3.Zero,
			double.PositiveInfinity,
			0,
			0,
			new CadPoint3(33, 0, 0),
			new CadPoint3(66, 0, 0));
		return CurveDisplayOverlay.Runner(
			identity,
			feature,
			constrained,
			exactCurrent,
			invalid);
	}
}
