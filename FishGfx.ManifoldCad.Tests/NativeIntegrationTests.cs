using FishGfx.Cad;
using Xunit;

namespace FishGfx.ManifoldCad.Tests;

public sealed class NativeIntegrationTests
{
	[Fact]
	public async Task ManagedSafeHandlesRevisionAndExactSweepWorkEndToEnd()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		(ManifoldProject project, CadMate _) = RunnerGraphTests.CreateProject();
		RunnerEvaluationResult evaluation = project.EvaluateRunner();
		await using CadDocument document = await CadDocument.CreateAsync(cancellationToken);
		long buildRevision = await document.BuildRunnerAsync(evaluation, cancellationToken);
		CadRevisioned<CadTessellation> preview = await document.TessellateRunnerAsync(cancellationToken: cancellationToken);

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
			CadRevisioned<CadTessellation> reopenedPreview = await reopened.TessellateRunnerAsync(cancellationToken: cancellationToken);
			Assert.Equal(loadRevision, reopenedPreview.Revision);
			Assert.NotEmpty(reopenedPreview.Value.Vertices);
		}
		finally
		{
			File.Delete(path);
		}
	}
}
