using FishGfx.Cad;
using Xunit;

namespace FishGfx.ManifoldCad.Tests;

public sealed class BezierDraftTests
{
	[Fact]
	public void DraftMovementIsIsolatedUntilAtomicCommit()
	{
		RunnerNode node = new(RunnerNodes.CubicBezier);
		Guid runnerId = Guid.NewGuid();
		CadFrame entry = new(
			new CadPoint3(10, 20, 30),
			new CadPoint3(1, 0, 0),
			new CadPoint3(0, 1, 0)
		);
		RunnerSectionProfile profile = RunnerSectionProfile.FromCircular(new PipeProfile(42.4, 2));
		RunnerFeature feature = new(
			node.Id,
			RunnerFeatureKind.CubicBezier,
			entry,
			new CadFrame(new CadPoint3(110, 20, 30), new CadPoint3(1, 0, 0), new CadPoint3(0, 1, 0)),
			profile,
			profile,
			100,
			CadPoint3.Zero,
			double.PositiveInfinity,
			0,
			0,
			new CadPoint3(43.333, 20, 30),
			new CadPoint3(76.667, 20, 30)
		);
		BezierDraftState draft = BezierDraftState.Create(runnerId, node, feature);
		string committedControl = node.Properties["control2U"];

		draft.MoveWorldPoint(RunnerPathPointKind.Control2, new CadPoint3(75, 28, 25));
		Assert.True(draft.IsDirty);
		Assert.Equal(committedControl, node.Properties["control2U"]);
		Assert.Equal(new CadPoint3(65, 8, -5), draft.Control2Local);

		draft.MoveWorldPoint(RunnerPathPointKind.Control1, new CadPoint3(5, 100, 100));
		Assert.True(draft.StartHandleLength > 0);
		draft.Commit(node);
		Assert.False(draft.IsDirty);
		Assert.Equal("8", node.Properties["control2U"]);
		Assert.Equal("-5", node.Properties["control2V"]);
	}

	[Fact]
	public void MovingAHandleToItsCurrentPointDoesNotDirtyTheDraft()
	{
		RunnerNode node = new(RunnerNodes.CubicBezier);
		RunnerSectionProfile profile = RunnerSectionProfile.FromCircular(new PipeProfile(42.4, 2));
		CadFrame entry = new(
			CadPoint3.Zero,
			new CadPoint3(1, 1, 0),
			new CadPoint3(0, 0, 1)
		);
		RunnerFeature feature = new(
			node.Id,
			RunnerFeatureKind.CubicBezier,
			entry,
			new CadFrame(new CadPoint3(100, 0, 0), entry.Tangent, entry.Normal),
			profile,
			profile,
			100,
			CadPoint3.Zero,
			double.PositiveInfinity,
			0,
			0,
			new CadPoint3(33.333333333333336, 0, 0),
			new CadPoint3(66.66666666666667, 0, 0)
		);
		BezierDraftState draft = BezierDraftState.Create(Guid.NewGuid(), node, feature);

		Assert.False(draft.MoveWorldPoint(RunnerPathPointKind.Control1, draft.Control1));
		Assert.False(draft.MoveWorldPoint(RunnerPathPointKind.Control2, draft.Control2));
		Assert.False(draft.MoveWorldPoint(RunnerPathPointKind.End, draft.End));
		Assert.False(draft.IsDirty);
	}
}
