using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FishGfx.NodeGraph
{
	public readonly struct NodeGraphViewState
	{
		public Vector2 Pan { get; }
		public float Zoom { get; }

		public NodeGraphViewState(Vector2 pan, float zoom)
		{
			Pan = pan;
			Zoom = zoom;
		}
	}

	public sealed class NodeGraphLoadResult
	{
		public bool Success => Errors.Count == 0 && Graph != null;
		public FunctionNodeGraph Graph { get; internal set; }
		public NodeGraphViewState View { get; internal set; }
		public IReadOnlyList<string> Errors { get; internal set; } = Array.Empty<string>();
	}

	public sealed class NodeGraphExecutionOutput
	{
		public string Name { get; set; }
		public string Type { get; set; }
		public JsonElement Value { get; set; }
	}

	public sealed class NodeGraphExecutionNode
	{
		public Guid Id { get; set; }
		public string Title { get; set; }
		public string State { get; set; }
		public string Message { get; set; }
		public List<NodeGraphExecutionOutput> Outputs { get; set; } = new List<NodeGraphExecutionOutput>();
	}

	public sealed class NodeGraphExecutionResult
	{
		public bool Success { get; set; }
		public int SuccessfulNodes { get; set; }
		public int FailedNodes { get; set; }
		public List<string> Errors { get; set; } = new List<string>();
		public List<NodeGraphExecutionNode> Nodes { get; set; } = new List<NodeGraphExecutionNode>();
	}

	public static class NodeGraphJson
	{
		public const int CurrentVersion = 1;
		private static readonly JsonSerializerOptions FileOptions = CreateOptions(true);
		private static readonly JsonSerializerOptions ResultOptions = CreateOptions(true);

		public static string Serialize(FunctionNodeGraph graph, NodeGraphViewState view)
		{
			if (graph == null)
				throw new ArgumentNullException(nameof(graph));
			Document document = new Document
			{
				Version = CurrentVersion,
				Canvas = new CanvasDto
				{
					PanX = view.Pan.X,
					PanY = view.Pan.Y,
					Zoom = view.Zoom,
				},
				Nodes = graph
					.Nodes.OrderBy(n => n.Id)
					.Select(n => new NodeDto
					{
						Id = n.Id,
						DeclaringType = n.Function.Method.DeclaringType.FullName,
						Method = n.Function.Method.Name,
						ParameterTypes = n
							.Function.Method.GetParameters()
							.Select(p => p.ParameterType.FullName)
							.ToList(),
						X = n.Position.X,
						Y = n.Position.Y,
						Width = n.Width,
						Body = n.BodyValues.Select(v => v.Text).ToList(),
					})
					.ToList(),
				Connections = graph
					.Connections.OrderBy(c => c.Input.Node.Id)
					.ThenBy(c => c.Input.Node.Inputs.IndexOf(c.Input))
					.Select(c => new ConnectionDto
					{
						OutputNode = c.Output.Node.Id,
						OutputIndex = c.Output.Node.Outputs.IndexOf(c.Output),
						InputNode = c.Input.Node.Id,
						InputIndex = c.Input.Node.Inputs.IndexOf(c.Input),
					})
					.ToList(),
			};
			return JsonSerializer.Serialize(document, FileOptions);
		}

		public static void SaveFile(string path, FunctionNodeGraph graph, NodeGraphViewState view)
		{
			if (string.IsNullOrWhiteSpace(path))
				throw new ArgumentException("A file path is required.", nameof(path));
			string fullPath = Path.GetFullPath(path),
				directory = Path.GetDirectoryName(fullPath);
			Directory.CreateDirectory(directory);
			string temporary = fullPath + ".tmp";

			try
			{
				File.WriteAllText(temporary, Serialize(graph, view));
				File.Move(temporary, fullPath, true);
			}
			finally
			{
				if (File.Exists(temporary))
					File.Delete(temporary);
			}
		}

		public static NodeGraphLoadResult LoadFile(string path, NodeFunctionRegistry registry)
		{
			try
			{
				return Deserialize(File.ReadAllText(path), registry);
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
			{
				return Failure(ex.Message);
			}
		}

		public static NodeGraphLoadResult Deserialize(string json, NodeFunctionRegistry registry)
		{
			if (registry == null)
				throw new ArgumentNullException(nameof(registry));

			if (json == null)
				return Failure("JSON content is required.");

			Document document;

			try
			{
				document = JsonSerializer.Deserialize<Document>(json, FileOptions);
			}
			catch (JsonException ex)
			{
				return Failure($"Invalid JSON: {ex.Message}");
			}

			List<string> errors = new List<string>();

			if (document == null)
				return Failure("The document is empty.");
			if (document.Version != CurrentVersion)
				errors.Add($"Unsupported schema version {document.Version}; expected {CurrentVersion}.");
			if (
				document.Canvas == null
				|| !Finite(document.Canvas.PanX, document.Canvas.PanY, document.Canvas.Zoom)
				|| document.Canvas.Zoom < .35f
				|| document.Canvas.Zoom > 2.5f
			)
				errors.Add("Canvas pan/zoom is invalid.");
			document.Nodes ??= new List<NodeDto>();
			document.Connections ??= new List<ConnectionDto>();

			foreach (
				IGrouping<Guid, NodeDto> duplicate in document
					.Nodes.GroupBy(n => n.Id)
					.Where(g => g.Key == Guid.Empty || g.Count() > 1)
			)
				errors.Add($"Node id {duplicate.Key} is empty or duplicated.");

			Dictionary<Guid, NodeFunctionDescriptor> descriptors = new Dictionary<Guid, NodeFunctionDescriptor>();

			foreach (NodeDto node in document.Nodes)
			{
				if (!Finite(node.X, node.Y, node.Width) || node.Width <= 0)
					errors.Add($"Node {node.Id} has invalid layout values.");
				List<NodeFunctionDescriptor> matches = registry
					.Functions.Where(f =>
						f.Method.DeclaringType?.FullName == node.DeclaringType
						&& f.Method.Name == node.Method
						&& f.Method.GetParameters()
							.Select(p => p.ParameterType.FullName)
							.SequenceEqual(node.ParameterTypes ?? new List<string>())
					)
					.ToList();
				if (matches.Count != 1)
					errors.Add(
						$"Node {node.Id} function {node.DeclaringType}.{node.Method} resolved to {matches.Count} registered methods."
					);
				else
				{
					descriptors[node.Id] = matches[0];
					int bodyCount = matches[0].Parameters.Count(p => p.IsBody);

					if ((node.Body?.Count ?? 0) != bodyCount)
						errors.Add($"Node {node.Id} has {node.Body?.Count ?? 0} body values; expected {bodyCount}.");
					else
						for (int i = 0; i < bodyCount; i++)
							if (
								!NodeValueConverter.TryParse(
									node.Body[i],
									matches[0].Parameters.Where(p => p.IsBody).ElementAt(i).Type,
									out _
								)
							)
								errors.Add($"Node {node.Id} body value {i} is invalid.");
				}
			}

			HashSet<(Guid, int)> occupiedInputs = new HashSet<(Guid, int)>();

			foreach (ConnectionDto connection in document.Connections)
			{
				NodeDto outputNode = document.Nodes.FirstOrDefault(n => n.Id == connection.OutputNode),
					inputNode = document.Nodes.FirstOrDefault(n => n.Id == connection.InputNode);
				if (
					outputNode == null
					|| inputNode == null
					|| !descriptors.TryGetValue(connection.OutputNode, out NodeFunctionDescriptor outputDescriptor)
					|| !descriptors.TryGetValue(connection.InputNode, out NodeFunctionDescriptor inputDescriptor)
				)
				{
					errors.Add("A connection references a missing or unresolved node.");
					continue;
				}

				IReadOnlyList<NodeOutputDescriptor> outputs = outputDescriptor.Outputs;
				NodeParameterDescriptor[] inputs = inputDescriptor.Parameters.Where(p => !p.IsBody).ToArray();

				if (
					connection.OutputIndex < 0
					|| connection.OutputIndex >= outputs.Count
					|| connection.InputIndex < 0
					|| connection.InputIndex >= inputs.Length
				)
				{
					errors.Add(
						$"Connection {connection.OutputNode}:{connection.OutputIndex} -> {connection.InputNode}:{connection.InputIndex} has an invalid port index."
					);
					continue;
				}

				if (outputs[connection.OutputIndex].Type != inputs[connection.InputIndex].Type)
					errors.Add("A connection has mismatched port types.");
				if (!occupiedInputs.Add((connection.InputNode, connection.InputIndex)))
					errors.Add($"Node {connection.InputNode} input {connection.InputIndex} has multiple connections.");
			}

			if (errors.Count > 0)
				return new NodeGraphLoadResult { Errors = errors };

			FunctionNodeGraph graph = new FunctionNodeGraph();
			Dictionary<Guid, FunctionNode> nodes = new Dictionary<Guid, FunctionNode>();

			foreach (NodeDto dto in document.Nodes)
			{
				FunctionNode node = graph.CreateNode(descriptors[dto.Id], new Vector2(dto.X, dto.Y), dto.Id);
				node.Width = dto.Width;

				for (int i = 0; i < dto.Body.Count; i++)
				{
					node.BodyValues[i].Text = dto.Body[i];
					node.BodyValues[i].Parse();
				}

				nodes.Add(dto.Id, node);
			}

			foreach (ConnectionDto dto in document.Connections)
				graph.Connect(
					nodes[dto.OutputNode].Outputs[dto.OutputIndex],
					nodes[dto.InputNode].Inputs[dto.InputIndex]
				);
			graph.InvalidateEvaluation();
			return new NodeGraphLoadResult
			{
				Graph = graph,
				View = new NodeGraphViewState(
					new Vector2(document.Canvas.PanX, document.Canvas.PanY),
					document.Canvas.Zoom
				),
			};
		}

		public static NodeGraphExecutionResult LoadAndEvaluateFile(string path, NodeFunctionRegistry registry)
		{
			return EvaluateLoaded(LoadFile(path, registry));
		}

		public static NodeGraphExecutionResult DeserializeAndEvaluate(string json, NodeFunctionRegistry registry)
		{
			return EvaluateLoaded(Deserialize(json, registry));
		}

		private static NodeGraphExecutionResult EvaluateLoaded(NodeGraphLoadResult load)
		{
			if (!load.Success)
				return new NodeGraphExecutionResult { Success = false, Errors = load.Errors.ToList() };
			NodeEvaluationResult evaluation = new FunctionNodeEvaluator().Evaluate(load.Graph);
			NodeGraphExecutionResult result = new NodeGraphExecutionResult
			{
				Success = evaluation.Success,
				SuccessfulNodes = evaluation.SuccessfulNodes,
				FailedNodes = evaluation.FailedNodes,
			};

			foreach (FunctionNode node in load.Graph.Nodes.OrderBy(n => n.Id))
				result.Nodes.Add(
					new NodeGraphExecutionNode
					{
						Id = node.Id,
						Title = node.Title,
						State = node.EvaluationState.ToString(),
						Message = node.EvaluationMessage,
						Outputs = node
							.Outputs.Select(o => new NodeGraphExecutionOutput
							{
								Name = o.Name,
								Type = o.Type.FullName,
								Value = ToElement(o.Value, o.Type),
							})
							.ToList(),
					}
				);
			return result;
		}

		public static string SerializeExecutionResult(NodeGraphExecutionResult result) =>
			JsonSerializer.Serialize(result, ResultOptions);

		private static JsonElement ToElement(object value, Type type) =>
			value == null
				? JsonSerializer.SerializeToElement<object>(null, ResultOptions)
				: JsonSerializer.SerializeToElement(value, type, ResultOptions);

		private static bool Finite(params float[] values) => values.All(float.IsFinite);

		private static NodeGraphLoadResult Failure(string error) =>
			new NodeGraphLoadResult { Errors = new[] { error } };

		private static JsonSerializerOptions CreateOptions(bool indented)
		{
			JsonSerializerOptions options = new JsonSerializerOptions
			{
				WriteIndented = indented,
				PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
				IncludeFields = true,
				NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
			};
			options.Converters.Add(new JsonStringEnumConverter());
			return options;
		}

		private sealed class Document
		{
			public int Version { get; set; }
			public CanvasDto Canvas { get; set; }
			public List<NodeDto> Nodes { get; set; }
			public List<ConnectionDto> Connections { get; set; }
		}

		private sealed class CanvasDto
		{
			public float PanX { get; set; }
			public float PanY { get; set; }
			public float Zoom { get; set; }
		}

		private sealed class NodeDto
		{
			public Guid Id { get; set; }
			public string DeclaringType { get; set; }
			public string Method { get; set; }
			public List<string> ParameterTypes { get; set; }
			public float X { get; set; }
			public float Y { get; set; }
			public float Width { get; set; }
			public List<string> Body { get; set; }
		}

		private sealed class ConnectionDto
		{
			public Guid OutputNode { get; set; }
			public int OutputIndex { get; set; }
			public Guid InputNode { get; set; }
			public int InputIndex { get; set; }
		}
	}
}
