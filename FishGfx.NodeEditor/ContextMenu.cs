using System.Numerics;

namespace FishGfx.NodeEditor {
	internal sealed class ContextMenu {
		internal bool IsOpen { get; private set; }
		internal Vector2 Position { get; private set; }
		internal int HoverIndex { get; set; } = -1;
		internal void Open(Vector2 world) { Position = world; IsOpen = true; HoverIndex = -1; }
		internal void Close() { IsOpen = false; HoverIndex = -1; }
		internal int Hit(Vector2 world) {
			if (!IsOpen || world.X < Position.X || world.X > Position.X + 220 || world.Y < Position.Y + 6) return -1;
			int index = (int)((world.Y - Position.Y - 6) / 34);
			return index >= 0 && index < NodeTemplates.Names.Length ? index : -1;
		}
	}
}
