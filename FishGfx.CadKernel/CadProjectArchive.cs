using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FishGfx.Cad;

public sealed class CadProjectPackage
{
	public ManifoldProject Project { get; internal set; }

	public byte[] ModelDocument { get; internal set; }
}

public static class CadProjectArchive
{
	public const string Schema = "fishgfx.manifold-cad";

	public const int CurrentVersion = 1;

	private const string ManifestEntry = "manifest.json";
	private const string GraphEntry = "graph.json";
	private const string ViewEntry = "view.json";
	private const string ModelEntry = "model.xbf";

	private static readonly JsonSerializerOptions Options = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	};

	public static void Save(string path, ManifoldProject project, ReadOnlySpan<byte> modelDocument)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		ArgumentNullException.ThrowIfNull(project);

		if (modelDocument.IsEmpty)
		{
			throw new ArgumentException("An exact XCAF model document is required.", nameof(modelDocument));
		}

		string fullPath = Path.GetFullPath(path);
		string temporaryPath = fullPath + ".tmp";
		Directory.CreateDirectory(Path.GetDirectoryName(fullPath));

		try
		{
			using (FileStream stream = File.Create(temporaryPath))
			using (ZipArchive archive = new(stream, ZipArchiveMode.Create, false))
			{
				WriteText(archive, ManifestEntry, JsonSerializer.Serialize(CreateManifest(project), Options));
				WriteText(archive, GraphEntry, RunnerGraphJson.Serialize(project.Graph));
				WriteText(archive, ViewEntry, JsonSerializer.Serialize(project.View, Options));
				WriteBytes(archive, ModelEntry, modelDocument);
			}

			File.Move(temporaryPath, fullPath, true);
		}
		finally
		{
			if (File.Exists(temporaryPath))
			{
				File.Delete(temporaryPath);
			}
		}
	}

	public static CadProjectPackage Load(string path)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);

		using FileStream stream = File.OpenRead(path);
		using ZipArchive archive = new(stream, ZipArchiveMode.Read, false);
		ProjectManifest manifest = JsonSerializer.Deserialize<ProjectManifest>(
			ReadText(archive, ManifestEntry),
			Options
		);

		if (manifest == null
			|| !string.Equals(manifest.Schema, Schema, StringComparison.Ordinal)
			|| manifest.Version != CurrentVersion)
		{
			throw new InvalidDataException("The project schema or version is unsupported.");
		}

		RunnerGraphLoadResult graphResult = RunnerGraphJson.Deserialize(ReadText(archive, GraphEntry));

		if (!graphResult.Success)
		{
			throw new InvalidDataException(string.Join(Environment.NewLine, graphResult.Errors));
		}

		ManifoldProject project = new()
		{
			Id = manifest.Id,
			Name = manifest.Name,
			Graph = graphResult.Graph,
			View = JsonSerializer.Deserialize<ManifoldViewState>(ReadText(archive, ViewEntry), Options)
				?? new ManifoldViewState(),
		};

		foreach (PartDto item in manifest.Parts ?? new List<PartDto>())
		{
			project.AddLoadedPart(new CadPart
			{
				Id = item.Id,
				Name = item.Name,
				SourcePath = item.SourcePath,
				Transform = item.Transform,
			});
		}

		foreach (MateDto item in manifest.Mates ?? new List<MateDto>())
		{
			CadMate mate = new()
			{
				Id = item.Id,
				PartId = item.PartId,
				Name = item.Name,
			};
			mate.RestoreBinding(item.Topology, item.LocalFrame, item.RadiusMillimetres);
			project.AddLoadedMate(mate);
		}

		return new CadProjectPackage
		{
			Project = project,
			ModelDocument = ReadBytes(archive, ModelEntry),
		};
	}

	private static ProjectManifest CreateManifest(ManifoldProject project)
	{
		return new ProjectManifest
		{
			Schema = Schema,
			Version = CurrentVersion,
			Id = project.Id,
			Name = project.Name,
			Units = "millimetres",
			Parts = project.Parts.Select(part => new PartDto
			{
				Id = part.Id,
				Name = part.Name,
				SourcePath = part.SourcePath,
				Transform = part.Transform,
			}).ToList(),
			Mates = project.Mates.Select(mate => new MateDto
			{
				Id = mate.Id,
				PartId = mate.PartId,
				Name = mate.Name,
				Topology = mate.Topology,
				LocalFrame = mate.LocalFrame,
				RadiusMillimetres = mate.RadiusMillimetres,
			}).ToList(),
		};
	}

	private static void WriteText(ZipArchive archive, string name, string value)
	{
		using StreamWriter writer = new(archive.CreateEntry(name, CompressionLevel.Optimal).Open());
		writer.Write(value);
	}

	private static void WriteBytes(ZipArchive archive, string name, ReadOnlySpan<byte> value)
	{
		using Stream output = archive.CreateEntry(name, CompressionLevel.Optimal).Open();
		output.Write(value);
	}

	private static string ReadText(ZipArchive archive, string name)
	{
		ZipArchiveEntry entry = archive.GetEntry(name)
			?? throw new InvalidDataException($"Project entry '{name}' is missing.");
		using StreamReader reader = new(entry.Open());

		return reader.ReadToEnd();
	}

	private static byte[] ReadBytes(ZipArchive archive, string name)
	{
		ZipArchiveEntry entry = archive.GetEntry(name)
			?? throw new InvalidDataException($"Project entry '{name}' is missing.");
		using Stream input = entry.Open();
		using MemoryStream output = new();
		input.CopyTo(output);

		return output.ToArray();
	}

	private sealed class ProjectManifest
	{
		public string Schema { get; set; }

		public int Version { get; set; }

		public Guid Id { get; set; }

		public string Name { get; set; }

		public string Units { get; set; }

		public List<PartDto> Parts { get; set; }

		public List<MateDto> Mates { get; set; }
	}

	private sealed class PartDto
	{
		public Guid Id { get; set; }

		public string Name { get; set; }

		public string SourcePath { get; set; }

		public CadTransform Transform { get; set; }
	}

	private sealed class MateDto
	{
		public Guid Id { get; set; }

		public Guid PartId { get; set; }

		public string Name { get; set; }

		public CadTopologyRef? Topology { get; set; }

		public CadFrame? LocalFrame { get; set; }

		public double RadiusMillimetres { get; set; }
	}
}
