using System;
using System.Collections.Generic;

namespace FishGfx.NodeGraph;

internal sealed class NodeGraphDocument
{
	public int Version { get; set; }
	public NodeGraphViewportDto Viewport { get; set; }
	public List<NodeGraphNodeDto> Nodes { get; set; }
	public List<NodeGraphConnectionDto> Connections { get; set; }
}

internal sealed class NodeGraphViewportDto
{
	public NodeGraphVectorDto Pan { get; set; }
	public float Zoom { get; set; }
}

internal sealed class NodeGraphVectorDto
{
	public float X { get; set; }
	public float Y { get; set; }
}

internal sealed class NodeGraphNodeDto
{
	public Guid Id { get; set; }
	public string Function { get; set; }
	public NodeGraphVectorDto Position { get; set; }
	public float Width { get; set; }
	public Dictionary<string, string> InlineValues { get; set; }
}

internal sealed class NodeGraphConnectionDto
{
	public NodeGraphEndpointDto From { get; set; }
	public NodeGraphEndpointDto To { get; set; }
}

internal sealed class NodeGraphEndpointDto
{
	public Guid Node { get; set; }
	public string Port { get; set; }
}
