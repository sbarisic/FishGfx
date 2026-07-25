using System.Text;
using Xunit;

namespace FishGfx.ManifoldCad.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ApplicationLogCollection
{
	public const string Name = nameof(ApplicationLogCollection);
}

[Collection(ApplicationLogCollection.Name)]
public sealed class ApplicationLogTests
{
	[Fact]
	public void StartupOverwritesPreviousLogAndDisposeRestoresNativeLogPath()
	{
		string directory = Path.Combine(
			Path.GetTempPath(),
			$"FishGfx.ManifoldCad.Tests-{Guid.NewGuid():N}");
		string path = Path.Combine(directory, "application.log");
		string previousConfiguredPath = Environment.GetEnvironmentVariable("FISHGFX_MANIFOLD_LOG");
		string previousNativePath = Environment.GetEnvironmentVariable("FGCAD_LOG_PATH");
		Directory.CreateDirectory(directory);
		File.WriteAllText(path, "obsolete previous run", Encoding.UTF8);

		try
		{
			Environment.SetEnvironmentVariable("FISHGFX_MANIFOLD_LOG", path);
			Environment.SetEnvironmentVariable("FGCAD_LOG_PATH", "previous-native-path");
			using (ApplicationLog log = ApplicationLog.Start())
			{
				Assert.Equal(Path.GetFullPath(path), log.Path);
				Assert.Equal(log.Path, Environment.GetEnvironmentVariable("FGCAD_LOG_PATH"));
				log.Info("current run marker");
				string activeContents = File.ReadAllText(path, Encoding.UTF8);
				Assert.DoesNotContain("obsolete previous run", activeContents);
				Assert.Contains("[startup]", activeContents);
				Assert.Contains("current run marker", activeContents);
			}

			Assert.Equal("previous-native-path", Environment.GetEnvironmentVariable("FGCAD_LOG_PATH"));
			Assert.Contains("[shutdown]", File.ReadAllText(path, Encoding.UTF8));
		}
		finally
		{
			Environment.SetEnvironmentVariable("FISHGFX_MANIFOLD_LOG", previousConfiguredPath);
			Environment.SetEnvironmentVariable("FGCAD_LOG_PATH", previousNativePath);
			Directory.Delete(directory, true);
		}
	}
}
