using FishGfx.Cad;
using Xunit;

namespace FishGfx.ManifoldCad.Tests;

public sealed class NativeIntegrationTests
{
	[Fact]
	public async Task KernelEvaluatesBuildsAndMapsCubicBezierFeature()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		(ManifoldProject project, _, CadRunner runner) = RunnerGraphTests.CreateProject();
		RunnerNode lastStraight = runner.Graph.Nodes.Last(node => node.DefinitionId == RunnerNodes.Straight);
		RunnerNode output = runner.Graph.Nodes.Single(node => node.DefinitionId == RunnerNodes.RunnerOutput);
		RunnerConnection tail = runner.Graph.Connections.Single(connection =>
			connection.OutputNodeId == lastStraight.Id && connection.InputNodeId == output.Id);
		Assert.True(runner.Graph.TrySpliceConnection(
			tail.Id,
			RunnerNodes.CubicBezier,
			1300,
			220,
			out RunnerNode bezier,
			out string error
		), error);
		runner.CommitEdit();

		await using CadDocument document = await CadDocument.CreateAsync(cancellationToken);
		RunnerEvaluationResult evaluation = await project.EvaluateRunnerAsync(document, runner, cancellationToken);
		Assert.True(evaluation.Success, string.Join(Environment.NewLine, evaluation.Diagnostics.Select(item => item.Message)));
		RunnerFeature feature = evaluation.Chain.Features.Single(item => item.NodeId == bezier.Id);
		Assert.Equal(RunnerFeatureKind.CubicBezier, feature.Kind);
		Assert.InRange(feature.LengthMillimetres, 99.999, 100.001);
		Assert.Equal(100, (feature.ExitFrame.Origin - feature.EntryFrame.Origin).Length, 3);

		long revision = await document.BuildRunnerAsync(runner, evaluation, cancellationToken);
		CadRevisioned<CadTessellation> tessellation = await document.TessellateRunnerAsync(
			runner.Id,
			cancellationToken: cancellationToken
		);
		Assert.Equal(revision, tessellation.Revision);
		Assert.Contains(tessellation.Value.Faces, face => face.SourceNodeId == bezier.Id);

		int validVertexCount = tessellation.Value.Vertices.Length;
		bezier.Properties["endT"] = bezier.Properties["control2T"];
		bezier.Properties["endU"] = bezier.Properties["control2U"];
		bezier.Properties["endV"] = bezier.Properties["control2V"];
		runner.CommitEdit();
		RunnerEvaluationResult invalid = await project.EvaluateRunnerAsync(document, runner, cancellationToken);
		Assert.False(invalid.Success);
		Assert.Contains(invalid.Diagnostics, diagnostic => diagnostic.NodeId == bezier.Id);
		CadRevisioned<CadTessellation> stale = await document.TessellateRunnerAsync(
			runner.Id,
			cancellationToken: cancellationToken
		);
		Assert.Equal(validVertexCount, stale.Value.Vertices.Length);

		await Assert.ThrowsAsync<InvalidOperationException>(
			() => document.BuildRunnerAsync(runner, evaluation, cancellationToken)
		);
	}

	[Fact]
	public async Task ManagedSafeHandlesRevisionAndExactSweepWorkEndToEnd()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		(ManifoldProject project, CadMate _, CadRunner runner) = RunnerGraphTests.CreateProject();
		RunnerEvaluationResult evaluation = project.EvaluateRunner(runner);
		await using CadDocument document = await CadDocument.CreateAsync(cancellationToken);
		long buildRevision = await document.BuildRunnerAsync(runner, evaluation, cancellationToken);
		CadRevisioned<CadTessellation> preview = await document.TessellateRunnerAsync(runner.Id,
			cancellationToken: cancellationToken);

		Assert.Equal(buildRevision, preview.Revision);
		Assert.NotEmpty(preview.Value.Vertices);
		Assert.NotEmpty(preview.Value.Indices);
		Assert.NotEmpty(preview.Value.Faces);
		Assert.Contains(preview.Value.Faces, face => face.SourceNodeId.HasValue);

		string path = Path.Combine(Path.GetTempPath(), $"fishgfx-managed-{Guid.NewGuid():N}.xbf");

		try
		{
			await document.SaveXcafAsync(path, cancellationToken);
			Assert.True(new FileInfo(path).Length > 0);
			await using CadDocument reopened = await CadDocument.CreateAsync(cancellationToken);
			long loadRevision = await reopened.LoadXcafAsync(path, cancellationToken);
			CadRevisioned<CadTessellation> reopenedPreview = await reopened.TessellateRunnerAsync(runner.Id,
				cancellationToken: cancellationToken);
			Assert.Equal(loadRevision, reopenedPreview.Revision);
			Assert.NotEmpty(reopenedPreview.Value.Vertices);
		}
		finally
		{
			File.Delete(path);
		}
	}
}
