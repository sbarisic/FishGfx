using System.IO.Compression;
using FishGfx.Cad;
using Xunit;

namespace FishGfx.ManifoldCad.Tests;

public sealed class PersistenceAndRelationshipTests
{
	[Fact]
	public void ArchiveContainsVersionedExactDocumentAndReopensWithoutStepSource()
	{
		(ManifoldProject project, CadMate mate) = RunnerGraphTests.CreateProject();
		project.Parts[0].SourcePath = @"Z:\missing\flange.step";
		project.Parts[0].Transform = new CadTransform(
			new CadPoint3(10, 20, 30),
			CadQuaternion.FromEulerDegrees(new CadPoint3(0, 45, 0))
		);
		project.View.Orthographic = true;
		byte[] exact = { 1, 4, 9, 16, 25 };
		string path = Temporary(".fgcad");

		try
		{
			CadProjectArchive.Save(path, project, exact);
			using (ZipArchive archive = ZipFile.OpenRead(path))
			{
				Assert.Equal(
					new[] { "graph.json", "manifest.json", "model.xbf", "view.json" },
					archive.Entries.Select(entry => entry.FullName).Order().ToArray()
				);
			}

			CadProjectPackage loaded = CadProjectArchive.Load(path);
			Assert.Equal(exact, loaded.ModelDocument);
			Assert.Equal(project.Parts[0].Id, loaded.Project.Parts[0].Id);
			Assert.Equal(mate.Id, loaded.Project.Mates[0].Id);
			Assert.Equal(project.Graph.Id, loaded.Project.Graph.Id);
			Assert.True(loaded.Project.View.Orthographic);
			Assert.True(loaded.Project.EvaluateRunner().Success);
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Fact]
	public void ReplaceInvalidatesMateButManualRebindPreservesLogicalIdentity()
	{
		(ManifoldProject project, CadMate mate) = RunnerGraphTests.CreateProject();
		Guid mateId = mate.Id;
		string name = mate.Name;
		project.ReplacePart(project.Parts[0].Id, "replacement.step");

		Assert.False(mate.IsResolved);
		Assert.False(project.EvaluateRunner().Success);
		mate.Rebind(
			new CadTopologyRef(project.Parts[0].Id, 99, CadTopologyKind.CylindricalFace),
			new CadFrame(new CadPoint3(50, 0, 0), new CadPoint3(0, 0, 1), new CadPoint3(1, 0, 0)),
			21.2
		);
		Assert.Equal(mateId, mate.Id);
		Assert.Equal(name, mate.Name);
		Assert.True(project.EvaluateRunner().Success);
	}

	[Fact]
	public void MovingPartComposesMateFrameAndMovesExactPathStart()
	{
		(ManifoldProject project, CadMate _) = RunnerGraphTests.CreateProject();
		CadPoint3 original = project.EvaluateRunner().Path.Segments[0].Start;
		project.Parts[0].Transform = new CadTransform(new CadPoint3(25, -5, 8), CadQuaternion.Identity);
		CadPoint3 moved = project.EvaluateRunner().Path.Segments[0].Start;

		Assert.Equal(original + new CadPoint3(25, -5, 8), moved);
	}

	private static string Temporary(string extension)
	{
		return Path.Combine(Path.GetTempPath(), $"fishgfx-cad-{Guid.NewGuid():N}{extension}");
	}
}
