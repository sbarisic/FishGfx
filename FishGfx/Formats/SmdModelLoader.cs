using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;

namespace FishGfx.Formats;

public static class SmdModelLoader
{
	public static IReadOnlyList<GenericMesh> Load(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			throw new ArgumentException("An SMD path is required.", nameof(path));
		}

		using StreamReader reader = File.OpenText(path);

		return Parse(reader);
	}

	public static IReadOnlyList<GenericMesh> Parse(TextReader reader)
	{
		ArgumentNullException.ThrowIfNull(reader);

		SmdParser parser = new(reader);

		return parser.Parse();
	}

	private sealed class SmdParser
	{
		private readonly TextReader reader;
		private readonly Dictionary<string, GenericMesh> meshes = new(StringComparer.OrdinalIgnoreCase);
		private readonly List<GenericMesh> meshOrder = new();
		private int lineNumber;

		internal SmdParser(TextReader reader)
		{
			this.reader = reader;
		}

		internal IReadOnlyList<GenericMesh> Parse()
		{
			string version = ReadRequiredLine();

			if (!string.Equals(version, "version 1", StringComparison.OrdinalIgnoreCase))
			{
				throw Error("Only SMD version 1 is supported.");
			}

			while (TryReadLine(out string section))
			{
				switch (section.ToLowerInvariant())
				{
					case "nodes":
						ParseNodes();
						break;

					case "skeleton":
						ParseSkeleton();
						break;

					case "triangles":
						ParseTriangles();
						break;

					default:
						throw Error($"Unknown SMD section '{section}'.");
				}
			}

			return meshOrder.AsReadOnly();
		}

		private void ParseNodes()
		{
			while (true)
			{
				string line = ReadRequiredLine();

				if (IsEnd(line))
				{
					return;
				}

				int firstQuote = line.IndexOf('"');
				int lastQuote = line.LastIndexOf('"');

				if (firstQuote <= 0 || lastQuote <= firstQuote)
				{
					throw Error("A node must contain an id, quoted name, and parent id.");
				}

				ParseInteger(line[..firstQuote].Trim(), "node id");
				ParseInteger(line[(lastQuote + 1)..].Trim(), "node parent id");
			}
		}

		private void ParseSkeleton()
		{
			bool hasTime = false;

			while (true)
			{
				string line = ReadRequiredLine();

				if (IsEnd(line))
				{
					return;
				}

				string[] tokens = Split(line);

				if (tokens.Length == 2 && string.Equals(tokens[0], "time", StringComparison.OrdinalIgnoreCase))
				{
					ParseInteger(tokens[1], "skeleton time");
					hasTime = true;
					continue;
				}

				if (!hasTime || tokens.Length != 7)
				{
					throw Error("A skeleton transform requires a time and seven numeric values.");
				}

				ParseInteger(tokens[0], "bone id");

				for (int index = 1; index < tokens.Length; index++)
				{
					ParseFloat(tokens[index], "skeleton transform");
				}
			}
		}

		private void ParseTriangles()
		{
			while (true)
			{
				string material = ReadRequiredLine();

				if (IsEnd(material))
				{
					return;
				}

				if (material.Contains(' '))
				{
					throw Error("A triangle material must appear before its three vertices.");
				}

				Vertex3[] triangle = new Vertex3[3];

				for (int index = 0; index < triangle.Length; index++)
				{
					triangle[index] = ParseVertex(ReadRequiredLine());
				}

				GenericMesh mesh = GetMesh(NormalizeMaterial(material));
				mesh.Vertices.Add(triangle[2]);
				mesh.Vertices.Add(triangle[1]);
				mesh.Vertices.Add(triangle[0]);
			}
		}

		private Vertex3 ParseVertex(string line)
		{
			string[] tokens = Split(line);

			if (tokens.Length < 9)
			{
				throw Error("An SMD vertex requires bone, position, normal, and UV values.");
			}

			ParseInteger(tokens[0], "parent bone");

			Vector3 position = new(
				ParseFloat(tokens[1], "position x"),
				ParseFloat(tokens[2], "position y"),
				ParseFloat(tokens[3], "position z")
			);

			for (int index = 4; index <= 6; index++)
			{
				ParseFloat(tokens[index], "normal");
			}

			Vector2 uv = new(
				ParseFloat(tokens[7], "texture u"),
				ParseFloat(tokens[8], "texture v")
			);

			ValidateLinks(tokens);

			return new Vertex3(position, uv);
		}

		private void ValidateLinks(string[] tokens)
		{
			if (tokens.Length == 9)
			{
				return;
			}

			int linkCount = ParseInteger(tokens[9], "link count");

			if (linkCount < 0 || tokens.Length != 10 + linkCount * 2)
			{
				throw Error("The SMD vertex link count does not match its bone/weight pairs.");
			}

			for (int index = 0; index < linkCount; index++)
			{
				int tokenIndex = 10 + index * 2;
				ParseInteger(tokens[tokenIndex], "link bone id");
				ParseFloat(tokens[tokenIndex + 1], "link weight");
			}
		}

		private GenericMesh GetMesh(string material)
		{
			if (meshes.TryGetValue(material, out GenericMesh mesh))
			{
				return mesh;
			}

			mesh = new GenericMesh(material);
			meshes.Add(material, mesh);
			meshOrder.Add(mesh);

			return mesh;
		}

		private string ReadRequiredLine()
		{
			if (TryReadLine(out string line))
			{
				return line;
			}

			throw Error("Unexpected end of SMD data.");
		}

		private bool TryReadLine(out string line)
		{
			while (true)
			{
				string source = reader.ReadLine();

				if (source == null)
				{
					line = null;
					return false;
				}

				lineNumber++;
				line = source.Replace('\t', ' ').Trim();

				if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
				{
					continue;
				}

				return true;
			}
		}

		private FormatException Error(string message)
		{
			return new FormatException($"Invalid SMD at line {lineNumber}: {message}");
		}

		private int ParseInteger(string text, string valueName)
		{
			if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
			{
				return value;
			}

			throw Error($"Invalid {valueName} '{text}'.");
		}

		private float ParseFloat(string text, string valueName)
		{
			if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
				&& float.IsFinite(value))
			{
				return value;
			}

			throw Error($"Invalid {valueName} '{text}'.");
		}

		private static bool IsEnd(string line)
		{
			return string.Equals(line, "end", StringComparison.OrdinalIgnoreCase);
		}

		private static string[] Split(string line)
		{
			return line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		}

		private static string NormalizeMaterial(string material)
		{
			int extensionIndex = material.IndexOf('.');

			return extensionIndex < 0 ? material : material[..extensionIndex];
		}
	}
}
