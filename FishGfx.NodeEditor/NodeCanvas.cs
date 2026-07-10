using System;
using System.Numerics;

namespace FishGfx.NodeEditor {
	internal sealed class NodeCanvas {
		internal Vector2 Pan { get; private set; } = new Vector2(120, 80);
		internal float Zoom { get; private set; } = 1;

		internal Vector2 WorldToScreen(Vector2 world) => world * Zoom + Pan;
		internal Vector2 ScreenToWorld(Vector2 screen) => (screen - Pan) / Zoom;
		internal void PanBy(Vector2 screenDelta) => Pan += screenDelta;

		internal void ZoomAt(Vector2 screenPoint, float wheelDelta) {
			Vector2 before = ScreenToWorld(screenPoint);
			Zoom = Math.Clamp(Zoom * MathF.Pow(1.12f, wheelDelta), 0.35f, 2.5f);
			Pan = screenPoint - before * Zoom;
		}
	}

	internal readonly struct Bounds {
		internal readonly float X, Y, Width, Height;
		internal Bounds(float x, float y, float width, float height) { X = x; Y = y; Width = width; Height = height; }
		internal bool Contains(Vector2 point) => point.X >= X && point.X <= X + Width && point.Y >= Y && point.Y <= Y + Height;
	}

	internal static class NodeGeometry {
		internal const float HeaderHeight = 42;
		internal const float PortRadius = 8;
		internal static Bounds BoundsOf(Node node) => new Bounds(node.Position.X, node.Position.Y, node.Width, node.Height);
		internal static Bounds HeaderOf(Node node) => new Bounds(node.Position.X, node.Position.Y + node.Height - HeaderHeight, node.Width, HeaderHeight);
		internal static Bounds CloseOf(Node node) => new Bounds(node.Position.X + node.Width - 35, node.Position.Y + node.Height - 35, 26, 26);

		internal static Vector2 PortPosition(NodePort port) {
			int index = port.Direction == PortDirection.Input ? port.Node.Inputs.IndexOf(port) : port.Node.Outputs.IndexOf(port);
			float y = port.Node.Position.Y + port.Node.Height - HeaderHeight - 21 - index * 30;
			float x = port.Direction == PortDirection.Input ? port.Node.Position.X : port.Node.Position.X + port.Node.Width;
			return new Vector2(x, y);
		}

		internal static Bounds ValueBounds(Node node, int index) => new Bounds(node.Position.X + 75, node.Position.Y + node.Height - HeaderHeight - 33 - index * 30, 92, 25);

		internal static bool NearConnection(Vector2 point, Vector2 start, Vector2 end, float tolerance = 10) {
			Vector2 c1 = start + new Vector2(Math.Max(60, Math.Abs(end.X - start.X) * .45f), 0);
			Vector2 c2 = end - new Vector2(Math.Max(60, Math.Abs(end.X - start.X) * .45f), 0);
			Vector2 previous = start;
			for (int i = 1; i <= 32; i++) {
				float t = i / 32f, u = 1 - t;
				Vector2 current = u * u * u * start + 3 * u * u * t * c1 + 3 * u * t * t * c2 + t * t * t * end;
				if (DistanceToSegment(point, previous, current) <= tolerance) return true;
				previous = current;
			}
			return false;
		}

		private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b) {
			Vector2 ab = b - a;
			float lengthSquared = ab.LengthSquared();
			if (lengthSquared == 0) return Vector2.Distance(p, a);
			float t = Math.Clamp(Vector2.Dot(p - a, ab) / lengthSquared, 0, 1);
			return Vector2.Distance(p, a + ab * t);
		}
	}
}
