using FishGfx.NodeGraph;

namespace FishGfx.NodeEditor;

internal sealed class InlineValueEditor
{
	internal NodeInlineValue Target { get; private set; }
	internal string Text { get; private set; } = "";
	internal bool IsActive => Target != null;

	internal void Begin(NodeInlineValue value)
	{
		Target = value;
		Text = value.Text;
	}

	internal void Append(string value)
	{
		if (!IsActive)
		{
			return;
		}

		Text += value;
	}

	internal void Backspace()
	{
		if (IsActive && Text.Length > 0)
		{
			Text = Text.Substring(0, Text.Length - 1);
		}
	}

	internal bool Commit()
	{
		if (!IsActive)
		{
			return false;
		}

		Target.Text = Text;

		if (!Target.Parse())
		{
			return false;
		}

		Cancel();
		return true;
	}

	internal void Cancel()
	{
		Target = null;
		Text = "";
	}
}
