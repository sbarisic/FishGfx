using FishGfx.Cad;
using Xunit;

namespace FishGfx.ManifoldCad.Tests;

public sealed class CorsaFlangeCollectorTests
{
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
	public async Task FourLargestOpeningsBuildAndTessellateCircularCollector()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using CorsaCollectorFixture fixture = await CorsaCollectorFixture.CreateAsync(
			cancellationToken
		);
		bool staging = false;
		try
		{
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
			staging = false;

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
