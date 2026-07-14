using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FishGfx.NodeGraph;

public static partial class NodeGraphJson
{
	public const int CurrentVersion = 2;

	private static readonly JsonSerializerOptions FileOptions = CreateOptions();
	private static readonly JsonSerializerOptions ResultOptions = CreateOptions();

	public static string Serialize(FunctionGraph graph, NodeGraphViewState view)
	{
		if (graph == null)
		{
			throw new ArgumentNullException(nameof(graph));
		}

		NodeGraphDocument document = new NodeGraphDocument
		{
			Version = CurrentVersion,
			Viewport = new NodeGraphViewportDto
			{
				Pan = Vector(view.Pan.X, view.Pan.Y),
				Zoom = view.Zoom,
			},
			Nodes = graph.Nodes
				.OrderBy(node => node.Id)
				.Select(Node)
				.ToList(),
			Connections = graph.Connections
				.OrderBy(connection => connection.Input.Node.Id)
				.ThenBy(connection => connection.Input.Name, StringComparer.Ordinal)
				.ThenBy(connection => connection.Output.Node.Id)
				.ThenBy(connection => connection.Output.Name, StringComparer.Ordinal)
				.Select(Connection)
				.ToList(),
		};

		return JsonSerializer.Serialize(document, FileOptions);
	}

	public static void SaveFile(string path, FunctionGraph graph, NodeGraphViewState view)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			throw new ArgumentException("A file path is required.", nameof(path));
		}

		string fullPath = Path.GetFullPath(path);
		string directory = Path.GetDirectoryName(fullPath);
		string temporaryPath = fullPath + ".tmp";

		Directory.CreateDirectory(directory);

		try
		{
			File.WriteAllText(temporaryPath, Serialize(graph, view));
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

	public static NodeGraphLoadResult LoadFile(string path, NodeFunctionRegistry registry)
	{
		try
		{
			return Deserialize(File.ReadAllText(path), registry);
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

	public static NodeGraphExecutionResult LoadAndEvaluateFile(string path, NodeFunctionRegistry registry)
	{
		return EvaluateLoaded(LoadFile(path, registry));
	}

	public static NodeGraphExecutionResult DeserializeAndEvaluate(string json, NodeFunctionRegistry registry)
	{
		return EvaluateLoaded(Deserialize(json, registry));
	}

	public static string SerializeExecutionResult(NodeGraphExecutionResult result)
	{
		if (result == null)
		{
			throw new ArgumentNullException(nameof(result));
		}

		return JsonSerializer.Serialize(result, ResultOptions);
	}

	private static NodeGraphExecutionResult EvaluateLoaded(NodeGraphLoadResult load)
	{
		if (!load.Success)
		{
			return new NodeGraphExecutionResult
			{
				Success = false,
				Errors = load.Errors.ToList(),
			};
		}

		NodeEvaluationResult evaluation = new FunctionGraphEvaluator().Evaluate(load.Graph);
		NodeGraphExecutionResult result = new NodeGraphExecutionResult
		{
			Success = evaluation.Success,
			SuccessfulNodeCount = evaluation.SuccessfulNodeCount,
			FailedNodeCount = evaluation.FailedNodeCount,
		};

		foreach (FunctionNode node in load.Graph.Nodes.OrderBy(node => node.Id))
		{
			result.Nodes.Add(ExecutionNode(node));
		}

		return result;
	}

	private static NodeGraphExecutionNode ExecutionNode(FunctionNode node)
	{
		NodeGraphExecutionNode result = new NodeGraphExecutionNode
		{
			Id = node.Id,
			Function = node.Function.Id,
			State = node.EvaluationState.ToString().ToLowerInvariant(),
			Message = node.EvaluationMessage,
		};

		foreach (NodePort output in node.Outputs.OrderBy(output => output.Name, StringComparer.Ordinal))
		{
			result.Outputs.Add(
				output.Name,
				new NodeGraphExecutionOutput
				{
					Type = output.Type.FullName ?? output.Type.Name,
					Value = ToElement(output.Value, output.Type),
				}
			);
		}

		return result;
	}

	private static NodeGraphNodeDto Node(FunctionNode node)
	{
		Dictionary<string, string> inlineValues = new Dictionary<string, string>(StringComparer.Ordinal);

		foreach (NodeInlineValue inlineValue in node.InlineValues.OrderBy(value => value.Name, StringComparer.Ordinal))
		{
			inlineValues.Add(inlineValue.Name, inlineValue.Text);
		}

		return new NodeGraphNodeDto
		{
			Id = node.Id,
			Function = node.Function.Id,
			Position = Vector(node.Position.X, node.Position.Y),
			Width = node.Width,
			InlineValues = inlineValues,
		};
	}

	private static NodeGraphConnectionDto Connection(NodeConnection connection)
	{
		return new NodeGraphConnectionDto
		{
			From = new NodeGraphEndpointDto
			{
				Node = connection.Output.Node.Id,
				Port = connection.Output.Name,
			},
			To = new NodeGraphEndpointDto
			{
				Node = connection.Input.Node.Id,
				Port = connection.Input.Name,
			},
		};
	}

	private static NodeGraphVectorDto Vector(float x, float y)
	{
		return new NodeGraphVectorDto
		{
			X = x,
			Y = y,
		};
	}

	private static JsonElement ToElement(object value, Type type)
	{
		return value == null
			? JsonSerializer.SerializeToElement<object>(null, ResultOptions)
			: JsonSerializer.SerializeToElement(value, type, ResultOptions);
	}

	private static NodeGraphLoadResult Failure(string error)
	{
		return new NodeGraphLoadResult
		{
			Errors = new[]
			{
				error,
			},
		};
	}

	private static JsonSerializerOptions CreateOptions()
	{
		JsonSerializerOptions options = new JsonSerializerOptions
		{
			WriteIndented = true,
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
			PropertyNameCaseInsensitive = false,
			IncludeFields = true,
			NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
			UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
		};

		return options;
	}
}
