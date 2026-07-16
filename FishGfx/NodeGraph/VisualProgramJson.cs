using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FishGfx.NodeGraph;

public sealed class VisualProgramLoadResult
{
	public bool Success => Program != null && Errors.Count == 0;
	public VisualProgram Program { get; internal set; }
	public IReadOnlyList<string> Errors { get; internal set; } = Array.Empty<string>();
	public IReadOnlyList<VisualProgramDiagnostic> Diagnostics { get; internal set; } =
		Array.Empty<VisualProgramDiagnostic>();
}

public static class VisualProgramJson
{
	public const string Schema = "fishgfx.visual-program";
	public const int CurrentVersion = 1;

	private static readonly JsonSerializerOptions Options = CreateOptions();

	public static string Serialize(VisualProgram program)
	{
		if (program == null)
		{
			throw new ArgumentNullException(nameof(program));
		}

		VisualProgramDocumentDto document = new VisualProgramDocumentDto
		{
			Schema = Schema,
			Version = CurrentVersion,
			Id = program.Id,
			Name = program.Name,
			Functions = program.Functions
				.OrderByDescending(function => function.IsEntryPoint)
				.ThenBy(function => function.Name, StringComparer.Ordinal)
				.Select(Function)
				.ToList(),
		};

		return JsonSerializer.Serialize(document, Options);
	}

	public static void SaveFile(string path, VisualProgram program)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			throw new ArgumentException("A file path is required.", nameof(path));
		}

		string fullPath = Path.GetFullPath(path);
		string temporaryPath = fullPath + ".tmp";

		Directory.CreateDirectory(Path.GetDirectoryName(fullPath));

		try
		{
			File.WriteAllText(temporaryPath, Serialize(program));
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

	public static VisualProgramLoadResult LoadFile(string path, VisualNodeCatalog catalog)
	{
		try
		{
			return Deserialize(File.ReadAllText(path), catalog);
		}
		catch (Exception exception) when (
			exception is IOException
			|| exception is UnauthorizedAccessException
			|| exception is ArgumentException
		)
		{
			return Failure(exception.Message);
		}
	}

	public static VisualProgramLoadResult Deserialize(string json, VisualNodeCatalog catalog)
	{
		if (catalog == null)
		{
			throw new ArgumentNullException(nameof(catalog));
		}

		if (json == null)
		{
			return Failure("JSON content is required.");
		}

		VisualProgramDocumentDto document;

		try
		{
			document = JsonSerializer.Deserialize<VisualProgramDocumentDto>(json, Options);
		}
		catch (JsonException exception)
		{
			return Failure("Invalid JSON: " + exception.Message);
		}

		List<string> errors = ValidateDocument(document);

		if (errors.Count > 0)
		{
			return new VisualProgramLoadResult
			{
				Errors = errors,
			};
		}

		try
		{
			VisualProgram program = Build(document, catalog);
			VisualProgramValidationResult validation = new VisualProgramValidator().Validate(program);

			return new VisualProgramLoadResult
			{
				Program = program,
				Diagnostics = validation.Diagnostics,
			};
		}
		catch (Exception exception) when (
			exception is ArgumentException
			|| exception is InvalidOperationException
			|| exception is KeyNotFoundException
		)
		{
			return Failure("Invalid visual program: " + exception.Message);
		}
	}

	private static VisualProgramFunctionDto Function(VisualFunction function)
	{
		return new VisualProgramFunctionDto
		{
			Id = function.Id,
			Name = function.Name,
			ReturnType = function.ReturnType,
			IsEntryPoint = function.IsEntryPoint,
			View = new VisualProgramViewDto
			{
				PanX = function.View.Pan.X,
				PanY = function.View.Pan.Y,
				Zoom = function.View.Zoom,
			},
			Symbols = function.Symbols
				.OrderBy(symbol => symbol.Id)
				.Select(Symbol)
				.ToList(),
			Nodes = function.Graph.Nodes
				.OrderBy(node => node.Id)
				.Select(Node)
				.ToList(),
			Connections = function.Graph.Connections
				.OrderBy(connection => connection.Input.Node.Id)
				.ThenBy(connection => connection.Input.Name, StringComparer.Ordinal)
				.Select(Connection)
				.ToList(),
		};
	}

	private static VisualProgramSymbolDto Symbol(VisualVariableSymbol symbol)
	{
		return new VisualProgramSymbolDto
		{
			Id = symbol.Id,
			Name = symbol.Name,
			Type = symbol.Type,
			IsParameter = symbol.IsParameter,
			ScopeNodeId = symbol.ScopeNodeId,
		};
	}

	private static VisualProgramNodeDto Node(VisualNode node)
	{
		return new VisualProgramNodeDto
		{
			Id = node.Id,
			Definition = node.DefinitionId,
			Title = node.Title,
			Role = node.Role,
			PositionX = node.Position.X,
			PositionY = node.Position.Y,
			Width = node.Width,
			PairedNodeId = node.PairedNodeId,
			Properties = new Dictionary<string, string>(node.Properties, StringComparer.Ordinal),
			Ports = node.Inputs
				.Concat(node.Outputs)
				.Select(Port)
				.ToList(),
		};
	}

	private static VisualProgramPortDto Port(VisualPort port)
	{
		return new VisualProgramPortDto
		{
			Name = port.Name,
			Label = port.Label,
			Kind = port.Kind,
			Direction = port.Direction,
			Type = port.Type,
			Optional = port.Optional,
			DefaultValue = port.DefaultValue,
		};
	}

	private static VisualProgramConnectionDto Connection(VisualConnection connection)
	{
		return new VisualProgramConnectionDto
		{
			Id = connection.Id,
			FromNode = connection.Output.Node.Id,
			FromPort = connection.Output.Name,
			ToNode = connection.Input.Node.Id,
			ToPort = connection.Input.Name,
		};
	}

	private static List<string> ValidateDocument(VisualProgramDocumentDto document)
	{
		List<string> errors = new List<string>();

		if (document == null)
		{
			errors.Add("The document is empty.");
			return errors;
		}

		if (!string.Equals(document.Schema, Schema, StringComparison.Ordinal))
		{
			errors.Add($"Unsupported schema '{document.Schema}'.");
		}

		if (document.Version != CurrentVersion)
		{
			errors.Add($"Unsupported schema version {document.Version}; expected {CurrentVersion}.");
		}

		if (document.Id == Guid.Empty)
		{
			errors.Add("The program id is empty.");
		}

		if (document.Functions == null || document.Functions.Count == 0)
		{
			errors.Add("At least one function is required.");
			return errors;
		}

		if (document.Functions.Any(function => function == null))
		{
			errors.Add("Functions cannot contain null entries.");
			return errors;
		}

		if (document.Functions.GroupBy(function => function.Id).Any(group => group.Key == Guid.Empty || group.Count() > 1))
		{
			errors.Add("Function ids must be non-empty and unique.");
		}

		foreach (VisualProgramFunctionDto function in document.Functions)
		{
			ValidateFunctionDto(function, errors);
		}

		return errors;
	}

	private static void ValidateFunctionDto(VisualProgramFunctionDto function, List<string> errors)
	{
		if (function == null)
		{
			errors.Add("A function entry is null.");
			return;
		}

		if (function.Nodes == null || function.Connections == null || function.Symbols == null)
		{
			errors.Add($"Function {function.Id} is missing a required collection.");
			return;
		}

		if (function.View == null
			|| !float.IsFinite(function.View.PanX)
			|| !float.IsFinite(function.View.PanY)
			|| !float.IsFinite(function.View.Zoom)
			|| function.View.Zoom < .35f
			|| function.View.Zoom > 2.5f)
		{
			errors.Add($"Function {function.Id} has an invalid viewport.");
		}

		if (!Enum.IsDefined(function.ReturnType))
		{
			errors.Add($"Function {function.Id} has an invalid return type.");
		}

		if (function.Nodes.Any(node => node == null)
			|| function.Symbols.Any(symbol => symbol == null)
			|| function.Connections.Any(connection => connection == null))
		{
			errors.Add($"Function {function.Id} contains null collection entries.");
			return;
		}

		if (function.Nodes.GroupBy(node => node.Id).Any(group => group.Key == Guid.Empty || group.Count() > 1))
		{
			errors.Add($"Function {function.Id} contains empty or duplicate node ids.");
		}

		if (function.Symbols.GroupBy(symbol => symbol.Id).Any(group => group.Key == Guid.Empty || group.Count() > 1))
		{
			errors.Add($"Function {function.Id} contains empty or duplicate symbol ids.");
		}

		if (function.Symbols.Any(symbol => !Enum.IsDefined(symbol.Type)))
		{
			errors.Add($"Function {function.Id} contains an invalid symbol type.");
		}

		if (function.Connections.GroupBy(connection => connection.Id).Any(group => group.Key == Guid.Empty || group.Count() > 1))
		{
			errors.Add($"Function {function.Id} contains empty or duplicate connection ids.");
		}

		foreach (VisualProgramNodeDto node in function.Nodes)
		{
			if (string.IsNullOrWhiteSpace(node.Definition)
				|| node.Ports == null
				|| node.Properties == null
				|| !Enum.IsDefined(node.Role)
				|| !float.IsFinite(node.PositionX)
				|| !float.IsFinite(node.PositionY)
				|| !float.IsFinite(node.Width)
				|| node.Width <= 0)
			{
				errors.Add($"Node {node.Id} is malformed.");
			}
			else if (node.Ports.Any(port => port == null))
			{
				errors.Add($"Node {node.Id} contains null ports.");
			}
			else if (node.Ports.GroupBy(port => (port.Direction, port.Name)).Any(group => string.IsNullOrWhiteSpace(group.Key.Name) || group.Count() > 1))
			{
				errors.Add($"Node {node.Id} contains empty or duplicate port names.");
			}
			else if (node.Ports.Any(port =>
				!Enum.IsDefined(port.Kind)
					|| !Enum.IsDefined(port.Direction)
					|| !Enum.IsDefined(port.Type)
					|| port.Kind == VisualPortKind.Execution && port.Type != VisualValueType.None
					|| port.Kind == VisualPortKind.Value && port.Type == VisualValueType.None
					|| (port.Optional || port.DefaultValue != null)
					&& (port.Kind != VisualPortKind.Value || port.Direction != VisualPortDirection.Input)
					|| !port.Optional && port.DefaultValue != null
			))
			{
				errors.Add($"Node {node.Id} contains an invalid port definition.");
			}
		}
	}

	private static VisualProgram Build(VisualProgramDocumentDto document, VisualNodeCatalog catalog)
	{
		VisualProgram program = new VisualProgram(document.Name, catalog, document.Id);

		foreach (VisualProgramFunctionDto functionDto in document.Functions)
		{
			VisualFunction function = program.AddFunction(
				functionDto.Id,
				functionDto.Name,
				functionDto.ReturnType,
				functionDto.IsEntryPoint,
				false
			);

			function.View = new VisualProgramViewState(
				new Vector2(functionDto.View.PanX, functionDto.View.PanY),
				functionDto.View.Zoom
			);

			foreach (VisualProgramSymbolDto symbol in functionDto.Symbols)
			{
				function.AddSymbol(
					new VisualVariableSymbol(
						symbol.Name,
						symbol.Type,
						symbol.IsParameter,
						symbol.ScopeNodeId,
						symbol.Id
					)
				);
			}

			BuildGraph(function, functionDto, catalog);
		}

		return program;
	}

	private static void BuildGraph(
		VisualFunction function,
		VisualProgramFunctionDto dto,
		VisualNodeCatalog catalog
	)
	{
		Dictionary<Guid, VisualNode> nodes = new Dictionary<Guid, VisualNode>();

		foreach (VisualProgramNodeDto nodeDto in dto.Nodes)
		{
			bool known = catalog.TryGet(nodeDto.Definition, out VisualNodeDefinition definition);
			VisualNode node = new VisualNode(
				nodeDto.Id,
				nodeDto.Definition,
				string.IsNullOrWhiteSpace(nodeDto.Title) ? definition?.Title ?? nodeDto.Definition : nodeDto.Title,
				nodeDto.Role,
				new Vector2(nodeDto.PositionX, nodeDto.PositionY)
			)
			{
				Width = nodeDto.Width,
				PairedNodeId = nodeDto.PairedNodeId,
				IsMissingDefinition = !known,
			};

			foreach (KeyValuePair<string, string> property in nodeDto.Properties)
			{
				node.Properties.Add(property.Key, property.Value);
			}

			foreach (VisualProgramPortDto port in nodeDto.Ports)
			{
				node.AddPort(
					new VisualPort(
						port.Name,
						port.Label,
						port.Kind,
						port.Direction,
						port.Type,
						port.Optional,
						port.DefaultValue
					)
				);
			}

			function.Graph.AddDeserializedNode(node);
			nodes.Add(node.Id, node);
		}

		foreach (VisualProgramConnectionDto connectionDto in dto.Connections)
		{
			if (!nodes.TryGetValue(connectionDto.FromNode, out VisualNode outputNode)
				|| !nodes.TryGetValue(connectionDto.ToNode, out VisualNode inputNode)
				|| !outputNode.TryGetOutput(connectionDto.FromPort, out VisualPort output)
				|| !inputNode.TryGetInput(connectionDto.ToPort, out VisualPort input))
			{
				throw new InvalidOperationException($"Connection {connectionDto.Id} references a missing endpoint.");
			}

			function.Graph.AddDeserializedConnection(
				new VisualConnection(output, input, connectionDto.Id)
			);
		}
	}

	private static VisualProgramLoadResult Failure(string error)
	{
		return new VisualProgramLoadResult
		{
			Errors = new[]
			{
				error,
			},
		};
	}

	private static JsonSerializerOptions CreateOptions()
	{
		return new JsonSerializerOptions
		{
			WriteIndented = true,
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
			PropertyNameCaseInsensitive = false,
			UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
			Converters =
			{
				new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
			},
		};
	}
}

internal sealed class VisualProgramDocumentDto
{
	public string Schema { get; set; }
	public int Version { get; set; }
	public Guid Id { get; set; }
	public string Name { get; set; }
	public List<VisualProgramFunctionDto> Functions { get; set; }
}

internal sealed class VisualProgramFunctionDto
{
	public Guid Id { get; set; }
	public string Name { get; set; }
	public VisualValueType ReturnType { get; set; }
	public bool IsEntryPoint { get; set; }
	public VisualProgramViewDto View { get; set; }
	public List<VisualProgramSymbolDto> Symbols { get; set; }
	public List<VisualProgramNodeDto> Nodes { get; set; }
	public List<VisualProgramConnectionDto> Connections { get; set; }
}

internal sealed class VisualProgramViewDto
{
	public float PanX { get; set; }
	public float PanY { get; set; }
	public float Zoom { get; set; }
}

internal sealed class VisualProgramSymbolDto
{
	public Guid Id { get; set; }
	public string Name { get; set; }
	public VisualValueType Type { get; set; }
	public bool IsParameter { get; set; }
	public Guid? ScopeNodeId { get; set; }
}

internal sealed class VisualProgramNodeDto
{
	public Guid Id { get; set; }
	public string Definition { get; set; }
	public string Title { get; set; }
	public VisualNodeRole Role { get; set; }
	public float PositionX { get; set; }
	public float PositionY { get; set; }
	public float Width { get; set; }
	public Guid? PairedNodeId { get; set; }
	public Dictionary<string, string> Properties { get; set; }
	public List<VisualProgramPortDto> Ports { get; set; }
}

internal sealed class VisualProgramPortDto
{
	public string Name { get; set; }
	public string Label { get; set; }
	public VisualPortKind Kind { get; set; }
	public VisualPortDirection Direction { get; set; }
	public VisualValueType Type { get; set; }
	public bool Optional { get; set; }
	public string DefaultValue { get; set; }
}

internal sealed class VisualProgramConnectionDto
{
	public Guid Id { get; set; }
	public Guid FromNode { get; set; }
	public string FromPort { get; set; }
	public Guid ToNode { get; set; }
	public string ToPort { get; set; }
}
