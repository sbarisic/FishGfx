using System.Globalization;
using FishGfx.Cad;
using Xunit;

namespace FishGfx.ManifoldCad.Tests;

public sealed class ExactBuildLifecycleTests
{
	[Fact]
	public void SupersededBuildCannotReplacePublishedExactState()
	{
		CadExactBuildState state = new();
		state.Request(1, "first");
		Assert.True(state.TryBegin(1, "first"));
		Assert.True(state.TryPublish(1, "first"));

		state.Request(2, "second");
		Assert.True(state.TryBegin(2, "second"));
		state.Request(3, "third");

		Assert.False(state.TryPublish(2, "second"));
		CadExactBuildSnapshot snapshot = state.Snapshot;
		Assert.Equal(CadExactBuildStatus.Requested, snapshot.Status);
		Assert.Equal(3, snapshot.RequestedRevision);
		Assert.Equal(1, snapshot.PublishedRevision);
		Assert.Equal("first", snapshot.PublishedDependencyHash);
	}

	[Fact]
	public void CancelledBuildRetainsPreviousValidPublication()
	{
		CadExactBuildState state = new();
		state.Request(4, "valid");
		Assert.True(state.TryBegin(4, "valid"));
		Assert.True(state.TryPublish(4, "valid"));
		state.Request(5, "draft");
		Assert.True(state.TryBegin(5, "draft"));

		state.Cancel(5);

		CadExactBuildSnapshot snapshot = state.Snapshot;
		Assert.Equal(CadExactBuildStatus.Cancelled, snapshot.Status);
		Assert.Equal(4, snapshot.PublishedRevision);
		Assert.Equal("valid", snapshot.PublishedDependencyHash);
		Assert.False(snapshot.IsCurrent);
	}

	[Fact]
	public void DuplicateWorkerAndLateFailureCannotOverwritePublication()
	{
		CadExactBuildState state = new();
		state.Request(7, "same-revision");
		Assert.True(state.TryBegin(7, "same-revision"));
		Assert.False(state.TryBegin(7, "same-revision"));
		Assert.True(state.TryPublish(7, "same-revision"));

		state.Fail(7, "late failure");
		state.Cancel(7);

		CadExactBuildSnapshot snapshot = state.Snapshot;
		Assert.True(snapshot.IsCurrent);
		Assert.Equal(CadExactBuildStatus.Current, snapshot.Status);
		Assert.Null(snapshot.Diagnostic);
	}

	[Fact]
	public void NativePublicationCallbackRunsOnlyForTheCurrentBuild()
	{
		CadExactBuildState state = new();
		int publicationCount = 0;
		state.Request(10, "superseded");
		Assert.True(state.TryBegin(10, "superseded"));
		state.Request(11, "current");

		Assert.False(state.TryPublish(
			10,
			"superseded",
			() => publicationCount++
		));
		Assert.Equal(0, publicationCount);

		Assert.True(state.TryBegin(11, "current"));
		Assert.True(state.TryPublish(
			11,
			"current",
			() => publicationCount++
		));
		Assert.Equal(1, publicationCount);
		Assert.True(state.TryPublish(11, "current"));
		Assert.Equal(1, publicationCount);
	}

	[Fact]
	public void ArchiveFreshnessRequiresCurrentDependencyAndNonDraftSave()
	{
		(ManifoldProject project, _, CadRunner runner) = RunnerGraphTests.CreateProject();
		runner.CommitEdit();
		string dependency = CadGeometryDependencyHash.Runner(project, runner);
		runner.ExactBuild.Request(runner.EditRevision, dependency);
		Assert.True(runner.ExactBuild.TryBegin(runner.EditRevision, dependency));
		Assert.True(runner.ExactBuild.TryPublish(runner.EditRevision, dependency));
		byte[] exactDocument = { 2, 3, 5, 7, 11 };
		string currentPath = Temporary("current");
		string draftPath = Temporary("draft");
		string failedPath = Temporary("failed");
		try
		{
			CadProjectArchive.Save(currentPath, project, exactDocument);
			CadProjectPackage current = CadProjectArchive.Load(currentPath);
			Assert.True(current.ExactGeometryFresh);
			Assert.True(Assert.Single(current.Project.Runners).ExactBuild.IsCurrent);

			CadProjectArchive.Save(draftPath, project, exactDocument, draft: true);
			CadProjectPackage draft = CadProjectArchive.Load(draftPath);
			Assert.False(draft.ExactGeometryFresh);
			Assert.False(Assert.Single(draft.Project.Runners).ExactBuild.IsCurrent);

			runner.ExactBuild.Request(runner.EditRevision, dependency);
			Assert.True(runner.ExactBuild.TryBegin(runner.EditRevision, dependency));
			runner.ExactBuild.Fail(runner.EditRevision, "forced test failure");
			CadProjectArchive.Save(failedPath, project, exactDocument);
			CadProjectPackage failed = CadProjectArchive.Load(failedPath);
			Assert.False(failed.ExactGeometryFresh);
			Assert.False(Assert.Single(failed.Project.Runners).ExactBuild.IsCurrent);
		}
		finally
		{
			File.Delete(currentPath);
			File.Delete(draftPath);
			File.Delete(failedPath);
		}
	}

	[Fact]
	public void EditingAfterPublicationChangesDependencyAndMarksExactStale()
	{
		(ManifoldProject project, _, CadRunner runner) = RunnerGraphTests.CreateProject();
		string before = CadGeometryDependencyHash.Runner(project, runner);
		runner.ExactBuild.Request(runner.EditRevision, before);
		Assert.True(runner.ExactBuild.TryBegin(runner.EditRevision, before));
		Assert.True(runner.ExactBuild.TryPublish(runner.EditRevision, before));

		RunnerNode straight = runner.Graph.Nodes.First(node =>
			node.DefinitionId == RunnerNodes.Straight);
		straight.Properties["length"] = "101";
		runner.CommitEdit();

		string after = CadGeometryDependencyHash.Runner(project, runner);
		Assert.NotEqual(before, after);
		Assert.Equal(CadExactBuildStatus.Stale, runner.ExactBuild.Snapshot.Status);
		Assert.Equal(before, runner.ExactBuild.Snapshot.PublishedDependencyHash);
	}

	[Fact]
	public void GeometryDependencyHashesAreCultureInvariant()
	{
		(ManifoldProject project, _, CadRunner runner) = RunnerGraphTests.CreateProject();
		CultureInfo originalCulture = CultureInfo.CurrentCulture;
		CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
		try
		{
			CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
			CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
			string english = CadGeometryDependencyHash.Runner(project, runner);

			CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
			CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
			string german = CadGeometryDependencyHash.Runner(project, runner);

			Assert.Equal(english, german);
		}
		finally
		{
			CultureInfo.CurrentCulture = originalCulture;
			CultureInfo.CurrentUICulture = originalUiCulture;
		}
	}

	[Fact]
	public void RunnerDependencyIgnoresCanvasLayoutAndDisconnectedNodes()
	{
		(ManifoldProject project, _, CadRunner runner) = RunnerGraphTests.CreateProject();
		string original = CadGeometryDependencyHash.Runner(project, runner);

		RunnerNode reachable = runner.Graph.Nodes.First(node =>
			node.DefinitionId == RunnerNodes.Straight);
		reachable.X += 123.5;
		reachable.Y -= 47.25;
		RunnerNode disconnected = runner.Graph.AddNode(RunnerNodes.Straight, 900, -400);
		disconnected.Properties["length"] = "777";

		Assert.Equal(original, CadGeometryDependencyHash.Runner(project, runner));

		reachable.Properties["length"] = "101";
		Assert.NotEqual(original, CadGeometryDependencyHash.Runner(project, runner));
	}

	[Fact]
	public void RunnerDependencyIncludesOwningPartIdentity()
	{
		(ManifoldProject project, CadMate mate, CadRunner runner) =
			RunnerGraphTests.CreateProject();
		string original = CadGeometryDependencyHash.Runner(project, runner);

		mate.PartId = Guid.NewGuid();

		Assert.NotEqual(original, CadGeometryDependencyHash.Runner(project, runner));
	}

	private static string Temporary(string suffix)
	{
		return Path.Combine(
			Path.GetTempPath(),
			$"fishgfx-exact-{suffix}-{Guid.NewGuid():N}.fgcad"
		);
	}
}
