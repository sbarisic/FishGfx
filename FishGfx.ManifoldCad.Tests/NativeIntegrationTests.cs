using FishGfx.Cad;
using Xunit;

namespace FishGfx.ManifoldCad.Tests;

public sealed class NativeIntegrationTests
{
	[Fact]
	public async Task ReverseFacingExactMateProfileBuildsAndTessellates()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		string fixture = Path.Combine(
			AppContext.BaseDirectory,
			"TestData",
			"corsa_flange.step"
		);
		Assert.True(File.Exists(fixture), $"Missing STEP fixture: {fixture}");

		ManifoldProject project = new();
		CadPart part = project.AddPart("Reverse-facing flange profile");
		await using CadDocument document = await CadDocument.CreateAsync(cancellationToken);
		await document.ImportStepAsync(part, fixture, cancellationToken);
		NativeTopologyDescriptor[] profiles = (await document.GetTopologyAsync(
			part.Id,
			cancellationToken
		)).Value
			.Where(item => item.Topology.Kind == CadTopologyKind.ClosedProfile)
			.OrderByDescending(item => item.RadiusMillimetres)
			.ToArray();
		Assert.NotEmpty(profiles);
		NativeTopologyDescriptor forward = profiles[0];
		NativeTopologyDescriptor reverse = profiles.First(item =>
			Math.Abs(item.RadiusMillimetres - forward.RadiusMillimetres) < 1e-6
				&& CadPoint3.Dot(item.Axis, forward.Axis) < -0.99);

		CadMate mate = project.AddMate(part.Id, "Reverse profile");
		MateFrameResult frame = (await document.GetMateFrameAsync(
			reverse.Topology,
			reverse.Center,
			cancellationToken
		)).Value;
		mate.Rebind(reverse.Topology, frame.Frame, frame.RadiusMillimetres);
		await document.BindMateSelectorAsync(mate, cancellationToken);
		CadRunner runner = project.AddRunner(mate.Id);
		RunnerEvaluationResult evaluation = await project.EvaluateRunnerAsync(
			document,
			runner,
			cancellationToken
		);
		Assert.True(
			evaluation.Success,
			string.Join(Environment.NewLine, evaluation.Diagnostics.Select(item => item.Message))
		);

		await document.BuildRunnerAsync(runner, evaluation, cancellationToken);
		CadTessellation tessellation = (await document.TessellateRunnerAsync(
			runner.Id,
			cancellationToken: cancellationToken
		)).Value;
		Assert.NotEmpty(tessellation.Vertices);
		Assert.NotEmpty(tessellation.Indices);
		Assert.NotEmpty(tessellation.Faces);
	}

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
