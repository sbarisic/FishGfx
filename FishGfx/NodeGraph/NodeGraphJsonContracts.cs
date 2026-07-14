using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;

namespace FishGfx.NodeGraph;

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
	public FunctionGraph Graph { get; internal set; }
	public NodeGraphViewState View { get; internal set; }
	public IReadOnlyList<string> Errors { get; internal set; } = Array.Empty<string>();
}

public sealed class NodeGraphExecutionOutput
{
	public string Type { get; set; }
	public JsonElement Value { get; set; }
}

public sealed class NodeGraphExecutionNode
{
	public Guid Id { get; set; }
	public string Function { get; set; }
	public string State { get; set; }
	public string Message { get; set; }
	public SortedDictionary<string, NodeGraphExecutionOutput> Outputs { get; set; } =
		new SortedDictionary<string, NodeGraphExecutionOutput>(StringComparer.Ordinal);
}

public sealed class NodeGraphExecutionResult
{
	public int Version { get; set; } = NodeGraphJson.CurrentVersion;
	public bool Success { get; set; }
	public int SuccessfulNodeCount { get; set; }
	public int FailedNodeCount { get; set; }
	public List<string> Errors { get; set; } = new List<string>();
	public List<NodeGraphExecutionNode> Nodes { get; set; } = new List<NodeGraphExecutionNode>();
}
