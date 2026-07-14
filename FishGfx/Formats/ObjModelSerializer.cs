using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;

namespace FishGfx.Formats;

public static class ObjModelSerializer
{
	public static IReadOnlyList<GenericMesh> Load(string path, bool reverseWinding = true)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			throw new ArgumentException("An OBJ path is required.", nameof(path));
		}

		using StreamReader reader = File.OpenText(path);

		return Parse(reader, reverseWinding);
	}

	public static IReadOnlyList<GenericMesh> Parse(TextReader reader, bool reverseWinding = true)
	{
		ArgumentNullException.ThrowIfNull(reader);

		List<Vector3> positions = new();
		List<Vector2> textureCoordinates = new();
		Dictionary<string, GenericMesh> meshes = new(StringComparer.Ordinal);
		List<GenericMesh> meshOrder = new();
		GenericMesh currentMesh = null;
		int lineNumber = 0;

		while (reader.ReadLine() is string source)
		{
			lineNumber++;
			string line = source.Trim();

			if (line.Length == 0 || line.StartsWith('#'))
			{
				continue;
			}

			string[] tokens = line.Split(
				(char[])null,
				StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
			);

			switch (tokens[0].ToLowerInvariant())
			{
				case "v":
					RequireCount(tokens, 4, lineNumber, "vertex");
					positions.Add(new Vector3(
						ParseFloat(tokens[1], lineNumber),
						ParseFloat(tokens[2], lineNumber),
						ParseFloat(tokens[3], lineNumber)
					));
					break;

				case "vt":
					RequireCount(tokens, 3, lineNumber, "texture coordinate");
					textureCoordinates.Add(new Vector2(
						ParseFloat(tokens[1], lineNumber),
						ParseFloat(tokens[2], lineNumber)
					));
					break;

				case "usemtl":
					RequireCount(tokens, 2, lineNumber, "material selection");
					currentMesh = GetMesh(tokens[1], meshes, meshOrder);
					break;

				case "f":
					if (tokens.Length < 4)
					{
						throw Error(lineNumber, "A face requires at least three vertices.");
					}

					currentMesh ??= GetMesh("default", meshes, meshOrder);
					AppendFace(
						currentMesh,
						tokens,
						positions,
						textureCoordinates,
						reverseWinding,
						lineNumber
					);
					break;

				case "o":
				case "g":
				case "s":
				case "vn":
				case "mtllib":
					break;
			}
		}

		return meshOrder.AsReadOnly();
	}

	public static void Save(string path, IEnumerable<GenericMesh> meshes)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			throw new ArgumentException("An OBJ path is required.", nameof(path));
		}

		ArgumentNullException.ThrowIfNull(meshes);

		GenericMesh[] materialized = meshes.ToArray();

		if (materialized.Any(mesh => mesh == null))
		{
			throw new ArgumentException("Meshes cannot contain null entries.", nameof(meshes));
		}

		using (StreamWriter writer = File.CreateText(path))
		{
			WriteObj(writer, Path.GetFileNameWithoutExtension(path), materialized);
		}

		using StreamWriter materialWriter = File.CreateText(Path.ChangeExtension(path, ".mtl"));

		WriteMaterials(materialWriter, materialized);
	}

	private static void WriteObj(TextWriter writer, string libraryName, IEnumerable<GenericMesh> meshes)
	{
		writer.WriteLine($"mtllib {libraryName}.mtl");

		int vertexOffset = 0;

		foreach (GenericMesh mesh in meshes)
		{
			if (mesh.Vertices.Count % 3 != 0)
			{
				throw new ArgumentException($"Mesh '{mesh.MaterialName}' does not contain complete triangles.");
			}

			foreach (Vertex3 vertex in mesh.Vertices)
			{
				writer.WriteLine(FormattableString.Invariant(
					$"v {vertex.Position.X} {vertex.Position.Y} {vertex.Position.Z}"
				));
			}

			foreach (Vertex3 vertex in mesh.Vertices)
			{
				writer.WriteLine(FormattableString.Invariant($"vt {vertex.UV.X} {vertex.UV.Y}"));
			}

			writer.WriteLine();
			writer.WriteLine($"usemtl {mesh.MaterialName}");

			for (int index = 0; index < mesh.Vertices.Count; index += 3)
			{
				int first = vertexOffset + index + 1;
				int second = first + 1;
				int third = first + 2;

				writer.WriteLine($"f {first}/{first} {second}/{second} {third}/{third}");
			}

			vertexOffset += mesh.Vertices.Count;
		}
	}

	private static void WriteMaterials(TextWriter writer, IEnumerable<GenericMesh> meshes)
	{
		foreach (string material in meshes.Select(mesh => mesh.MaterialName).Distinct(StringComparer.Ordinal))
		{
			writer.WriteLine($"newmtl {material}");
			writer.WriteLine($"map_Kd {material}.png");
			writer.WriteLine();
		}
	}

	private static void AppendFace(
		GenericMesh mesh,
		string[] tokens,
		IReadOnlyList<Vector3> positions,
		IReadOnlyList<Vector2> textureCoordinates,
		bool reverseWinding,
		int lineNumber
	)
	{
		Vertex3 first = ParseVertex(tokens[1], positions, textureCoordinates, lineNumber);

		for (int index = 2; index < tokens.Length - 1; index++)
		{
			Vertex3 second = ParseVertex(tokens[index], positions, textureCoordinates, lineNumber);
			Vertex3 third = ParseVertex(tokens[index + 1], positions, textureCoordinates, lineNumber);

			if (reverseWinding)
			{
				mesh.Vertices.Add(second);
				mesh.Vertices.Add(first);
				mesh.Vertices.Add(third);
			}
			else
			{
				mesh.Vertices.Add(first);
				mesh.Vertices.Add(second);
				mesh.Vertices.Add(third);
			}
		}
	}

	private static Vertex3 ParseVertex(
		string token,
		IReadOnlyList<Vector3> positions,
		IReadOnlyList<Vector2> textureCoordinates,
		int lineNumber
	)
	{
		string[] indices = token.Split('/');

		if (indices.Length == 0 || string.IsNullOrWhiteSpace(indices[0]))
		{
			throw Error(lineNumber, $"Face vertex '{token}' has no position index.");
		}

		int positionIndex = ResolveIndex(indices[0], positions.Count, lineNumber, "position");
		Vector2 uv = Vector2.Zero;

		if (indices.Length > 1 && !string.IsNullOrWhiteSpace(indices[1]))
		{
			int uvIndex = ResolveIndex(indices[1], textureCoordinates.Count, lineNumber, "texture coordinate");
			uv = textureCoordinates[uvIndex];
		}

		return new Vertex3(positions[positionIndex], uv);
	}

	private static int ResolveIndex(string text, int count, int lineNumber, string valueName)
	{
		if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
			|| parsed == 0)
		{
			throw Error(lineNumber, $"Invalid {valueName} index '{text}'.");
		}

		int index = parsed > 0 ? parsed - 1 : count + parsed;

		if (index < 0 || index >= count)
		{
			throw Error(lineNumber, $"{valueName} index '{text}' is outside the available data.");
		}

		return index;
	}

	private static float ParseFloat(string text, int lineNumber)
	{
		if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
			&& float.IsFinite(value))
		{
			return value;
		}

		throw Error(lineNumber, $"Invalid numeric value '{text}'.");
	}

	private static GenericMesh GetMesh(
		string material,
		IDictionary<string, GenericMesh> meshes,
		ICollection<GenericMesh> meshOrder
	)
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

	private static void RequireCount(string[] tokens, int expected, int lineNumber, string kind)
	{
		if (tokens.Length < expected)
		{
			throw Error(lineNumber, $"The {kind} record is incomplete.");
		}
	}

	private static FormatException Error(int lineNumber, string message)
	{
		return new FormatException($"Invalid OBJ at line {lineNumber}: {message}");
	}
}
