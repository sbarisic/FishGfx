using System.Diagnostics;
using System.Text.Json;
using FishGfx.Cad;
using Xunit;

namespace FishGfx.ManifoldCad.Tests;

public sealed class CorsaFlangeCollectorTests
{
	private readonly ITestOutputHelper output;

	public CorsaFlangeCollectorTests(ITestOutputHelper output)
	{
		this.output = output;
	}

	[Fact]
	public async Task LegacyArchiveWithoutNativeSelectorsRestoresResolvedMates()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		string stepPath = Path.Combine(AppContext.BaseDirectory, "TestData", "corsa_flange.step");
		string xcafPath = Path.Combine(
			Path.GetTempPath(),
			$"fishgfx-corsa-legacy-{Guid.NewGuid():N}.xbf"
		);

		try
		{
			ManifoldProject project = new();
			await using (CadDocument source = await CadDocument.CreateAsync(cancellationToken))
			{
				CadPart part = project.AddPart("Corsa flange");
				await source.ImportStepAsync(part, stepPath, cancellationToken);
				NativeTopologyDescriptor opening = (await source.GetTopologyAsync(
					part.Id,
					cancellationToken
				)).Value
					.Where(item => item.Topology.Kind == CadTopologyKind.ClosedProfile)
					.MaxBy(item => item.RadiusMillimetres);
				MateFrameResult mateFrame = (await source.GetMateFrameAsync(
					opening.Topology,
					opening.Center,
					cancellationToken
				)).Value;
				CadMate mate = project.AddMate(part.Id, "Legacy Corsa port");
				mate.Rebind(opening.Topology, mateFrame.Frame, mateFrame.RadiusMillimetres);
				project.AddRunner(mate.Id, "Legacy runner");

				// Schema-v3 archives could contain resolved logical mates without native
				// FGSELECTOR labels. Preserve that legacy condition in this fixture.
				await source.SaveXcafAsync(xcafPath, cancellationToken);
			}

			await using CadDocument reopened = await CadDocument.CreateAsync(cancellationToken);
			await reopened.LoadXcafAsync(xcafPath, cancellationToken);
			CadRunner runner = Assert.Single(project.Runners);
			RunnerEvaluationResult evaluation = await project.EvaluateRunnerAsync(
				reopened,
				runner,
				cancellationToken
			);
			Assert.True(evaluation.Success, FormatDiagnostics(evaluation));
			await reopened.BeginRunnerBuildAsync(runner.Id, cancellationToken);
			CadKernelException missingSelector = await Assert.ThrowsAsync<CadKernelException>(() =>
				reopened.BuildRunnerAsync(runner, evaluation, cancellationToken)
			);
			Assert.Contains(
				"selector was not found",
				missingSelector.Message,
				StringComparison.OrdinalIgnoreCase
			);
			await reopened.AbortRunnerBuildAsync(runner.Id, CancellationToken.None);

			await ManifoldCadApplication.RestoreResolvedMateSelectorsAsync(
				reopened,
				project,
				cancellationToken
			);
			await reopened.BeginRunnerBuildAsync(runner.Id, cancellationToken);
			await reopened.BuildRunnerAsync(runner, evaluation, cancellationToken);
			await reopened.CommitRunnerBuildAsync(runner.Id, cancellationToken);
		}
		finally
		{
			File.Delete(xcafPath);
		}
	}

	[Fact]
	public async Task FourLargestOpeningsProduceValidCircularRunnerConstraints()
	{
		await using CorsaCollectorFixture fixture = await CorsaCollectorFixture.CreateAsync(
			TestContext.Current.CancellationToken
		);

		Assert.Equal(4, fixture.Runners.Count);
		Assert.All(fixture.Openings, opening => Assert.Equal(
			CadTopologyKind.ClosedProfile,
			opening.Topology.Kind
		));
		Assert.True(fixture.Openings.Min(opening => opening.RadiusMillimetres) > 20);
		Assert.All(
			fixture.Openings.SelectMany((left, leftIndex) =>
				fixture.Openings.Skip(leftIndex + 1).Select(right => (left, right))),
			pair => Assert.True((pair.left.Center - pair.right.Center).Length > 20)
		);
		Assert.All(fixture.MemberEvaluations.Values, result => Assert.True(
			result.Success,
			FormatDiagnostics(result)
		));
	}

	[Fact]
	public async Task RotatingOutletPreservesCorsaRunnerCurves()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using CorsaCollectorFixture fixture = await CorsaCollectorFixture.CreateAsync(
			cancellationToken
		);
		Dictionary<Guid, CadFrame> inletFrames = fixture.Collector.Inlets.ToDictionary(
			inlet => inlet.Id,
			fixture.Collector.GetWorldInletFrame
		);
		CadFrame rotatedOutlet = new(
			fixture.Collector.OutletFrame.Origin,
			new CadPoint3(1, 0, 0),
			new CadPoint3(0, 1, 0)
		);

		fixture.Collector.SetOutletFramePreservingWorldInlets(rotatedOutlet);
		fixture.Collector.CommitEdit();

		foreach (CadCollectorInlet inlet in fixture.Collector.Inlets)
		{
			CadFrame expected = inletFrames[inlet.Id];
			CadFrame actual = fixture.Collector.GetWorldInletFrame(inlet);
			Assert.Equal(expected.Origin.X, actual.Origin.X, 9);
			Assert.Equal(expected.Origin.Y, actual.Origin.Y, 9);
			Assert.Equal(expected.Origin.Z, actual.Origin.Z, 9);
			Assert.Equal(expected.Tangent.X, actual.Tangent.X, 9);
			Assert.Equal(expected.Tangent.Y, actual.Tangent.Y, 9);
			Assert.Equal(expected.Tangent.Z, actual.Tangent.Z, 9);
		}
		foreach (CadRunner runner in fixture.Runners)
		{
			RunnerEvaluationResult result = await fixture.Project.EvaluateRunnerAsync(
				fixture.Document,
				runner,
				cancellationToken
			);
			Assert.True(result.Success, $"{runner.Name}: {FormatDiagnostics(result)}");
		}
	}

	[Fact]
	public async Task FourLargestOpeningsBuildAndTessellateCircularCollector()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using CorsaCollectorFixture fixture = await CorsaCollectorFixture.CreateAsync(
			cancellationToken
		);
		bool staging = false;
		try
		{
			Stopwatch preview = Stopwatch.StartNew();
			RunnerEvaluationResult previewResult = await fixture.Project.EvaluateRunnerAsync(
				fixture.Document,
				fixture.Runners[0],
				cancellationToken
			);
			preview.Stop();
			Assert.True(previewResult.Success, FormatDiagnostics(previewResult));

			Stopwatch coldSystem = Stopwatch.StartNew();
			await fixture.Document.BeginCollectorSystemBuildAsync(
				fixture.Collector,
				cancellationToken
			);
			staging = true;
			foreach (CadRunner runner in fixture.Runners)
			{
				await fixture.Document.BuildRunnerAsync(
					runner,
					fixture.MemberEvaluations[runner.Id],
					fixture.Collector,
					cancellationToken
				);
			}
			await fixture.Document.BuildCollectorSystemAsync(
				fixture.Collector,
				fixture.MemberEvaluations,
				cancellationToken
			);
			await fixture.Document.CommitCollectorSystemBuildAsync(
				fixture.Collector.Id,
				fixture.Collector.GenerationRevision,
				CancellationToken.None
			);
			staging = false;
			coldSystem.Stop();
			CadBuildMetrics coldCollectorMetrics = (await fixture.Document
				.GetBuildMetricsAsync(fixture.Collector.Id, cancellationToken)).Value;
			CadBuildMetrics[] coldRunnerMetrics = await Task.WhenAll(
				fixture.Runners.Select(async runner => (await fixture.Document
					.GetBuildMetricsAsync(runner.Id, cancellationToken)).Value)
			);
			Assert.Equal(2u, coldCollectorMetrics.Operations.MergeBooleanCount);
			Assert.Equal(0u, coldCollectorMetrics.Operations.InterfaceBooleanCount);
			Assert.Equal(1u, coldCollectorMetrics.Operations.FinalBooleanCount);
			Assert.Equal(3u, coldCollectorMetrics.Operations.CutCount);
			Assert.Equal(1u, coldCollectorMetrics.Topology.SolidCount);
			Assert.Equal(1u, coldCollectorMetrics.Topology.ShellCount);
			Assert.Equal(
				(uint)(fixture.Collector.Inlets.Count + 1),
				coldCollectorMetrics.Operations.ClassificationCount
			);
			Assert.All(coldRunnerMetrics, metrics =>
			{
				Assert.Equal(0u, metrics.Operations.MergeBooleanCount);
				Assert.Equal(0u, metrics.Operations.CutCount);
			});

			CadTessellation tessellation = (await fixture.Document
				.TessellateCollectorSystemAsync(
					fixture.Collector.Id,
					0.35,
					Math.PI / 12,
					cancellationToken
				)).Value;
			Assert.NotEmpty(tessellation.Vertices);
			Assert.NotEmpty(tessellation.Indices);
			Assert.NotEmpty(tessellation.Faces);
			Assert.Contains(
				tessellation.Faces.SelectMany(face => face.Sources),
				source => source.SourceKind == CadGeometrySourceKind.CollectorInlet
			);
			double furthestDownstream = tessellation.Vertices.Max(vertex =>
				CadPoint3.Dot(
					new CadPoint3(vertex.X, vertex.Y, vertex.Z)
						- fixture.Collector.OutletFrame.Origin,
					fixture.Collector.OutletFrame.Tangent
				)
			);
			Assert.InRange(furthestDownstream, -0.1, 0.1);

			_ = await fixture.Document.TessellateCollectorSystemAsync(
				fixture.Collector.Id,
				0.35,
				Math.PI / 12,
				cancellationToken
			);
			CadBuildMetrics tessellationCacheMetrics = (await fixture.Document
				.GetBuildMetricsAsync(fixture.Collector.Id, cancellationToken)).Value;
			Assert.True(tessellationCacheMetrics.CacheLayers.HasFlag(
				CadBuildCacheLayers.Tessellation
			));

			Stopwatch unchanged = Stopwatch.StartNew();
			await fixture.Document.BeginCollectorSystemBuildAsync(
				fixture.Collector,
				cancellationToken
			);
			staging = true;
			foreach (CadRunner runner in fixture.Runners)
			{
				await fixture.Document.BuildRunnerAsync(
					runner,
					fixture.MemberEvaluations[runner.Id],
					fixture.Collector,
					cancellationToken
				);
			}
			await fixture.Document.BuildCollectorSystemAsync(
				fixture.Collector,
				fixture.MemberEvaluations,
				cancellationToken
			);
			await fixture.Document.CommitCollectorSystemBuildAsync(
				fixture.Collector.Id,
				fixture.Collector.GenerationRevision,
				CancellationToken.None
			);
			staging = false;
			unchanged.Stop();
			CadBuildMetrics unchangedMetrics = (await fixture.Document
				.GetBuildMetricsAsync(fixture.Collector.Id, cancellationToken)).Value;
			Assert.True(unchangedMetrics.CacheLayers.HasFlag(
				CadBuildCacheLayers.SystemAssembly
			));
			Assert.Equal(0u, unchangedMetrics.Operations.MergeBooleanCount);
			Assert.All(fixture.Runners, runner =>
			{
				CadBuildMetrics runnerMetrics = fixture.Document
					.GetBuildMetricsAsync(runner.Id, cancellationToken)
					.GetAwaiter().GetResult().Value;
				Assert.True(runnerMetrics.CacheLayers.HasFlag(
					CadBuildCacheLayers.RunnerSolid
				));
			});

			CadRunner changedRunner = fixture.Runners[0];
			RunnerNode changedLoft = changedRunner.Graph.Nodes.First(node =>
				node.DefinitionId == RunnerNodes.LoftTransition);
			changedLoft.Properties["length"] = "31";
			changedRunner.CommitEdit();
			fixture.Collector.CommitEdit();
			Dictionary<Guid, RunnerEvaluationResult> changedEvaluations = new();
			foreach (CadRunner runner in fixture.Runners)
			{
				changedEvaluations[runner.Id] = await fixture.Project.EvaluateRunnerAsync(
					fixture.Document,
					runner,
					cancellationToken
				);
			}
			Assert.All(changedEvaluations.Values, result => Assert.True(
				result.Success,
				FormatDiagnostics(result)
			));

			Stopwatch changedRunnerSystem = Stopwatch.StartNew();
			await fixture.Document.BeginCollectorSystemBuildAsync(
				fixture.Collector,
				cancellationToken
			);
			staging = true;
			foreach (CadRunner runner in fixture.Runners)
			{
				await fixture.Document.BuildRunnerAsync(
					runner,
					changedEvaluations[runner.Id],
					fixture.Collector,
					cancellationToken
				);
			}
			await fixture.Document.BuildCollectorSystemAsync(
				fixture.Collector,
				changedEvaluations,
				cancellationToken
			);
			await fixture.Document.CommitCollectorSystemBuildAsync(
				fixture.Collector.Id,
				fixture.Collector.GenerationRevision,
				CancellationToken.None
			);
			staging = false;
			changedRunnerSystem.Stop();
			CadBuildMetrics changedSystemMetrics = (await fixture.Document
				.GetBuildMetricsAsync(fixture.Collector.Id, cancellationToken)).Value;
			Assert.True(changedSystemMetrics.CacheLayers.HasFlag(
				CadBuildCacheLayers.CollectorBody
			));
			Assert.False(changedSystemMetrics.CacheLayers.HasFlag(
				CadBuildCacheLayers.SystemAssembly
			));
			Assert.Equal(0u, changedSystemMetrics.Operations.MergeBooleanCount);

			double[] runnerMilliseconds = coldRunnerMetrics
				.Select(metrics => metrics.Stages.Total.TotalMilliseconds)
				.OrderBy(value => value)
				.ToArray();
			double runnerMedianMilliseconds = 0.5 * (
				runnerMilliseconds[(runnerMilliseconds.Length - 1) / 2]
				+ runnerMilliseconds[runnerMilliseconds.Length / 2]
			);
			output.WriteLine(JsonSerializer.Serialize(new
			{
				Fixture = "corsa-flange-four-largest-openings",
				PreviewMilliseconds = preview.Elapsed.TotalMilliseconds,
				ColdRunnerMilliseconds = runnerMilliseconds,
				RunnerMedianMilliseconds = runnerMedianMilliseconds,
				RunnerP95Milliseconds = runnerMilliseconds[^1],
				ColdCollectorMilliseconds = coldCollectorMetrics.Stages.Total.TotalMilliseconds,
				ColdSystemMilliseconds = coldSystem.Elapsed.TotalMilliseconds,
				UnchangedWallMilliseconds = unchanged.Elapsed.TotalMilliseconds,
				ChangedRunnerSystemMilliseconds = changedRunnerSystem.Elapsed.TotalMilliseconds,
				ColdCollector = coldCollectorMetrics,
				ColdRunners = coldRunnerMetrics,
				UnchangedCacheLayers = unchangedMetrics.CacheLayers,
				ChangedCacheLayers = changedSystemMetrics.CacheLayers,
				TessellationCacheLayers = tessellationCacheMetrics.CacheLayers,
			}, new JsonSerializerOptions { WriteIndented = true }));

			if (string.Equals(
				Environment.GetEnvironmentVariable("FGCAD_ENFORCE_PERFORMANCE_TARGETS"),
				"1",
				StringComparison.Ordinal
			))
			{
				Assert.InRange(preview.Elapsed.TotalMilliseconds, 0, 100);
				Assert.InRange(coldSystem.Elapsed.TotalMilliseconds, 0, 8000);
				Assert.InRange(runnerMedianMilliseconds, 0, 500);
				Assert.InRange(runnerMilliseconds[^1], 0, 750);
				Assert.InRange(unchanged.Elapsed.TotalMilliseconds, 0, 250);
				Assert.InRange(changedRunnerSystem.Elapsed.TotalMilliseconds, 0, 2000);
			}

			await VerifySaveReopenAndStepRoundTripAsync(
				fixture,
				cancellationToken
			);
		}
		finally
		{
			if (staging)
			{
				await fixture.Document.AbortCollectorSystemBuildAsync(
					fixture.Collector.Id,
					fixture.Collector.GenerationRevision,
					CancellationToken.None
				);
			}
		}
	}

	private async Task VerifySaveReopenAndStepRoundTripAsync(
		CorsaCollectorFixture fixture,
		CancellationToken cancellationToken
	)
	{
		foreach (CadRunner runner in fixture.Runners)
		{
			string hash = CadGeometryDependencyHash.Runner(fixture.Project, runner);
			runner.ExactBuild.Request(runner.EditRevision, hash);
			Assert.True(runner.ExactBuild.TryBegin(runner.EditRevision, hash));
			Assert.True(runner.ExactBuild.TryPublish(runner.EditRevision, hash));
		}
		string collectorHash = CadGeometryDependencyHash.Collector(
			fixture.Project,
			fixture.Collector
		);
		fixture.Collector.ExactBuild.Request(
			fixture.Collector.GenerationRevision,
			collectorHash
		);
		Assert.True(fixture.Collector.ExactBuild.TryBegin(
			fixture.Collector.GenerationRevision,
			collectorHash
		));
		Assert.True(fixture.Collector.ExactBuild.TryPublish(
			fixture.Collector.GenerationRevision,
			collectorHash
		));

		string directory = Path.Combine(
			Path.GetTempPath(),
			$"fishgfx-corsa-acceptance-{Guid.NewGuid():N}"
		);
		Directory.CreateDirectory(directory);
		try
		{
			string xcafPath = Path.Combine(directory, "model.xbf");
			string archivePath = Path.Combine(directory, "corsa.fgcad");
			string reopenedXcafPath = Path.Combine(directory, "reopened.xbf");
			string stepPath = Path.Combine(directory, "corsa-ap242.step");
			await fixture.Document.SaveXcafAsync(xcafPath, cancellationToken);
			CadProjectArchive.Save(
				archivePath,
				fixture.Project,
				await File.ReadAllBytesAsync(xcafPath, cancellationToken)
			);
			CadProjectPackage reopenedPackage = CadProjectArchive.Load(archivePath);
			if (!reopenedPackage.ExactGeometryFresh)
			{
				foreach (CadRunner savedRunner in fixture.Runners)
				{
					CadRunner loadedRunner = reopenedPackage.Project.Runners.Single(
						candidate => candidate.Id == savedRunner.Id
					);
					output.WriteLine(
						$"Runner freshness {savedRunner.Id}: "
							+ $"saved={CadGeometryDependencyHash.Runner(fixture.Project, savedRunner)}; "
							+ $"loaded={CadGeometryDependencyHash.Runner(reopenedPackage.Project, loadedRunner)}; "
							+ $"exact={loadedRunner.ExactBuild.Snapshot}"
					);
				}
				CadCollectorSystem loadedCollector = reopenedPackage.Project.CollectorSystems
					.Single(candidate => candidate.Id == fixture.Collector.Id);
				output.WriteLine(
					$"Collector freshness {fixture.Collector.Id}: "
						+ $"saved={CadGeometryDependencyHash.Collector(fixture.Project, fixture.Collector)}; "
						+ $"loaded={CadGeometryDependencyHash.Collector(reopenedPackage.Project, loadedCollector)}; "
						+ $"exact={loadedCollector.ExactBuild.Snapshot}"
				);
			}
			Assert.True(reopenedPackage.ExactGeometryFresh);
			await File.WriteAllBytesAsync(
				reopenedXcafPath,
				reopenedPackage.ModelDocument,
				cancellationToken
			);

			await using (CadDocument reopened = await CadDocument.CreateAsync(cancellationToken))
			{
				await reopened.LoadXcafAsync(reopenedXcafPath, cancellationToken);
				CadTessellation reopenedCollector = (await reopened
					.TessellateCollectorSystemAsync(
						fixture.Collector.Id,
						0.35,
						Math.PI / 12,
						cancellationToken
					)).Value;
				Assert.NotEmpty(reopenedCollector.Indices);
				Dictionary<Guid, RunnerEvaluationResult> reopenedEvaluations = new();
				foreach (CadRunner runner in reopenedPackage.Project.Runners)
				{
					reopenedEvaluations[runner.Id] = await reopenedPackage.Project
						.EvaluateRunnerAsync(reopened, runner, cancellationToken);
				}
				Assert.True(ManifoldCadApplication.CanExportProject(
					reopenedPackage.Project,
					reopenedEvaluations,
					new Dictionary<Guid, string>()
				));
				await reopened.ExportStepAsync(stepPath, cancellationToken);
			}

			Assert.True(new FileInfo(stepPath).Length > 0);
			await using CadDocument reimported = await CadDocument.CreateAsync(cancellationToken);
			CadPart imported = new() { Id = Guid.NewGuid(), Name = "Reimported Corsa assembly" };
			await reimported.ImportStepAsync(imported, stepPath, cancellationToken);
			Assert.NotEmpty((await reimported.GetTopologyAsync(
				imported.Id,
				cancellationToken
			)).Value);
		}
		finally
		{
			Directory.Delete(directory, true);
		}
	}

	private static string FormatDiagnostics(RunnerEvaluationResult result)
	{
		return string.Join(
			Environment.NewLine,
			result.Diagnostics.Select(diagnostic => diagnostic.Message)
		);
	}

	private sealed class CorsaCollectorFixture : IAsyncDisposable
	{
		private CorsaCollectorFixture(
			CadDocument document,
			ManifoldProject project,
			CadCollectorSystem collector,
			IReadOnlyList<NativeTopologyDescriptor> openings,
			IReadOnlyList<CadRunner> runners,
			IReadOnlyDictionary<Guid, RunnerEvaluationResult> memberEvaluations
		)
		{
			Document = document;
			Project = project;
			Collector = collector;
			Openings = openings;
			Runners = runners;
			MemberEvaluations = memberEvaluations;
		}

		public CadDocument Document { get; }

		public ManifoldProject Project { get; }

		public CadCollectorSystem Collector { get; }

		public IReadOnlyList<NativeTopologyDescriptor> Openings { get; }

		public IReadOnlyList<CadRunner> Runners { get; }

		public IReadOnlyDictionary<Guid, RunnerEvaluationResult> MemberEvaluations { get; }

		public static async Task<CorsaCollectorFixture> CreateAsync(
			CancellationToken cancellationToken
		)
		{
			string path = Path.Combine(
				AppContext.BaseDirectory,
				"TestData",
				"corsa_flange.step"
			);
			Assert.True(File.Exists(path), $"Missing STEP fixture: {path}");

			CadDocument document = await CadDocument.CreateAsync(cancellationToken);
			try
			{
				ManifoldProject project = new();
				CadPart part = project.AddPart("Corsa flange");
				await document.ImportStepAsync(part, path, cancellationToken);
				IReadOnlyList<NativeTopologyDescriptor> topology = (await document
					.GetTopologyAsync(part.Id, cancellationToken)).Value;
				NativeTopologyDescriptor[] openings = SelectFourLargestOpenings(topology);
				Assert.Equal(4, openings.Length);

				List<CadRunner> runners = new();
				for (int index = 0; index < openings.Length; index++)
				{
					NativeTopologyDescriptor opening = openings[index];
					MateFrameResult mateFrame = (await document.GetMateFrameAsync(
						opening.Topology,
						opening.Center,
						cancellationToken
					)).Value;
					CadMate mate = project.AddMate(part.Id, $"Corsa port {index + 1}");
					mate.Rebind(
						opening.Topology,
						mateFrame.Frame,
						mateFrame.RadiusMillimetres
					);
					await document.BindMateSelectorAsync(mate, cancellationToken);
					runners.Add(project.AddRunner(mate.Id, $"Corsa runner {index + 1}"));
				}

				Dictionary<Guid, RunnerEvaluationResult> initialEvaluations = new();
				foreach (CadRunner runner in runners)
				{
					initialEvaluations[runner.Id] = await project.EvaluateRunnerAsync(
						document,
						runner,
						cancellationToken
					);
				}
				Assert.All(initialEvaluations.Values, result => Assert.True(
					result.Success,
					FormatDiagnostics(result)
				));

				Assert.True(project.TryCreateCollectorSystem(
					runners.Select(runner => runner.Id),
					CollectorLayoutPreset.Radial,
					"Corsa circular 4 into 1",
					initialEvaluations,
					out CadCollectorSystem collector,
					out string collectorError
				), collectorError);

				Dictionary<Guid, RunnerEvaluationResult> memberEvaluations = new();
				foreach (CadRunner runner in runners)
				{
					memberEvaluations[runner.Id] = await project.EvaluateRunnerAsync(
						document,
						runner,
						cancellationToken
					);
				}

				return new CorsaCollectorFixture(
					document,
					project,
					collector,
					openings,
					runners,
					memberEvaluations
				);
			}
			catch
			{
				await document.DisposeAsync();
				throw;
			}
		}

		public ValueTask DisposeAsync()
		{
			return Document.DisposeAsync();
		}

		private static NativeTopologyDescriptor[] SelectFourLargestOpenings(
			IReadOnlyList<NativeTopologyDescriptor> topology
		)
		{
			NativeTopologyDescriptor[] profiles = topology
				.Where(item => item.Topology.Kind == CadTopologyKind.ClosedProfile)
				.OrderByDescending(item => item.RadiusMillimetres)
				.ToArray();
			Assert.NotEmpty(profiles);
			CadPoint3 side = profiles[0].Axis.Normalized();
			return profiles
				.Where(item => CadPoint3.Dot(item.Axis.Normalized(), side) > 0.99)
				.OrderByDescending(item => item.RadiusMillimetres)
				.Take(4)
				.ToArray();
		}
	}
}
