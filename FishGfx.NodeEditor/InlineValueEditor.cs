using System;
using System.Globalization;

namespace FishGfx.NodeEditor {
	internal sealed class InlineValueEditor {
		internal NodeValue Target { get; private set; }
		internal string Text { get; private set; } = "";
		internal bool IsActive => Target != null;

		internal void Begin(NodeValue value) { Target = value; Text = value.Value.ToString("0.###", CultureInfo.InvariantCulture); }
		internal void Append(string value) {
			if (!IsActive) return;
			foreach (char c in value)
				if (char.IsDigit(c) || c == '-' || c == '+' || c == '.') Text += c;
		}
		internal void Backspace() { if (IsActive && Text.Length > 0) Text = Text.Substring(0, Text.Length - 1); }
		internal bool Commit() {
			if (!IsActive) return false;
			if (!float.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || !float.IsFinite(value)) return false;
			Target.Value = value; Cancel(); return true;
		}
		internal void Cancel() { Target = null; Text = ""; }
	}
}
