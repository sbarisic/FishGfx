using System.Linq;
using System.Numerics;
using FishGfx.NodeGraph;

namespace FishGfx.NodeEditor;

internal static class NodeHitTester
{
	internal static FunctionNode FindNode(FunctionGraph graph, Vector2 world)
	{
		for (int index = graph.Nodes.Count - 1; index >= 0; index--)
		{
			FunctionNode node = graph.Nodes[index];

			if (NodeGeometry.BoundsOf(node).Contains(world))
			{
				return node;
			}
		}

		return null;
	}

	internal static NodePort FindPort(FunctionGraph graph, NodeCanvas canvas, Vector2 world)
	{
		float radius = NodeGeometry.PortRadius + 5 / canvas.Zoom;

		for (int nodeIndex = graph.Nodes.Count - 1; nodeIndex >= 0; nodeIndex--)
		{
			FunctionNode node = graph.Nodes[nodeIndex];

			foreach (NodePort port in node.Inputs.Concat(node.Outputs))
			{
				if (Vector2.Distance(world, NodeGeometry.PortPosition(port)) <= radius)
				{
					return port;
				}
			}
		}

		return null;
	}

	internal static NodeConnection FindConnection(FunctionGraph graph, NodeCanvas canvas, Vector2 world)
	{
		foreach (NodeConnection connection in graph.Connections)
		{
			bool isNear = NodeGeometry.NearConnection(
				world,
				NodeGeometry.PortPosition(connection.Output),
				NodeGeometry.PortPosition(connection.Input),
				10 / canvas.Zoom
			);

			if (isNear)
			{
				return connection;
			}
		}

		return null;
	}
}
