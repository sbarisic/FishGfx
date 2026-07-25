using System.Numerics;
using global::FishUI;
using FishUI.Controls;
using FishUIRuntime = global::FishUI.FishUI;

namespace FishGfx.ManifoldCad;

internal sealed class WrappedLabel : Label
{
	public override void DrawControl(FishUIRuntime ui, float deltaTime, float time)
	{
		if (string.IsNullOrEmpty(Text))
		{
			return;
		}

		Vector2 position = GetAbsolutePosition();
		Vector2 size = GetAbsoluteSize();
		float lineHeight = ui.Graphics.MeasureText(
			ui.Settings.FontLabel,
			"Ag"
		).Y;
		if (!float.IsFinite(lineHeight) || lineHeight <= 0)
		{
			lineHeight = ui.Settings.FontLabel.Size;
		}

		IReadOnlyList<string> lines = WrapText(
			Text,
			size.X,
			value => ui.Graphics.MeasureText(
				ui.Settings.FontLabel,
				value
			).X
		);
		int visibleLineCount = Math.Max(
			1,
			(int)MathF.Floor(size.Y / lineHeight)
		);
		FishColor textColor = GetColorOverride("Text", FishColor.Black);

		ui.Graphics.PushScissor(position, size);
		try
		{
			int linesToDraw = Math.Min(lines.Count, visibleLineCount);
			for (int index = 0; index < linesToDraw; ++index)
			{
				string line = index == visibleLineCount - 1
					&& lines.Count > visibleLineCount
						? Ellipsize(
							lines[index],
							size.X,
							value => ui.Graphics.MeasureText(
								ui.Settings.FontLabel,
								value
							).X
						)
						: lines[index];
				ui.Graphics.DrawTextColor(
					ui.Settings.FontLabel,
					line,
					position + new Vector2(0, index * lineHeight),
					textColor
				);
			}
		}
		finally
		{
			ui.Graphics.PopScissor();
		}
	}

	internal static IReadOnlyList<string> WrapText(
		string text,
		float maximumWidth,
		Func<string, float> measure
	)
	{
		ArgumentNullException.ThrowIfNull(text);
		ArgumentNullException.ThrowIfNull(measure);

		List<string> lines = new();
		foreach (string paragraph in text
			.Replace("\r\n", "\n", StringComparison.Ordinal)
			.Split('\n'))
		{
			WrapParagraph(paragraph, maximumWidth, measure, lines);
		}
		return lines;
	}

	private static void WrapParagraph(
		string paragraph,
		float maximumWidth,
		Func<string, float> measure,
		List<string> lines
	)
	{
		if (paragraph.Length == 0)
		{
			lines.Add(string.Empty);
			return;
		}

		string current = string.Empty;
		foreach (string word in paragraph.Split(
			' ',
			StringSplitOptions.RemoveEmptyEntries
		))
		{
			string candidate = current.Length == 0
				? word
				: $"{current} {word}";
			if (Fits(candidate, maximumWidth, measure))
			{
				current = candidate;
				continue;
			}
			if (current.Length > 0)
			{
				lines.Add(current);
				current = string.Empty;
			}

			string remainder = word;
			while (!Fits(remainder, maximumWidth, measure))
			{
				int prefixLength = LargestFittingPrefix(
					remainder,
					maximumWidth,
					measure
				);
				lines.Add(remainder[..prefixLength]);
				remainder = remainder[prefixLength..];
			}
			current = remainder;
		}
		if (current.Length > 0)
		{
			lines.Add(current);
		}
	}

	private static int LargestFittingPrefix(
		string value,
		float maximumWidth,
		Func<string, float> measure
	)
	{
		int low = 1;
		int high = value.Length;
		int result = 1;
		while (low <= high)
		{
			int middle = low + (high - low) / 2;
			if (Fits(value[..middle], maximumWidth, measure))
			{
				result = middle;
				low = middle + 1;
			}
			else
			{
				high = middle - 1;
			}
		}
		return result;
	}

	private static string Ellipsize(
		string value,
		float maximumWidth,
		Func<string, float> measure
	)
	{
		const string suffix = "...";
		while (value.Length > 0
			&& !Fits(value + suffix, maximumWidth, measure))
		{
			value = value[..^1];
		}
		return value + suffix;
	}

	private static bool Fits(
		string value,
		float maximumWidth,
		Func<string, float> measure
	)
	{
		float width = measure(value);
		return float.IsFinite(width)
			&& width <= Math.Max(1, maximumWidth);
	}
}
