using System;
using System.Text;

namespace FishGfx.Game;

public sealed partial class DevConsole
{
	private readonly char[] characters;
	private readonly Color[] colors;
	private readonly StringBuilder input = new();
	private int cursorX;
	private int cursorY;
	private int removableCharacterCount;
	private bool isAwaitingInput;
	private bool isDirty = true;

	public event Action<string> InputSubmitted;

	public int Width { get; }

	public int Height { get; }

	public int BufferWidth => Width;

	public int BufferHeight { get; }

	public int CharacterSize { get; }

	public int ViewScroll { get; private set; }

	public Color TextColor { get; set; } = Color.White;

	public Color BackgroundColor { get; set; } = Color.Coal;

	public bool IsEnabled { get; set; } = true;

	private DevConsole(
		int characterSize,
		int width,
		int height,
		int bufferHeight
	)
	{
		if (characterSize <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(characterSize));
		}

		if (width <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(width));
		}

		if (height <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(height));
		}

		if (bufferHeight < height)
		{
			throw new ArgumentOutOfRangeException(
				nameof(bufferHeight),
				"The backing buffer must be at least as tall as the visible console."
			);
		}

		CharacterSize = characterSize;
		Width = width;
		Height = height;
		BufferHeight = bufferHeight;
		characters = new char[checked(width * bufferHeight)];
		colors = new Color[characters.Length];
	}

	public void BeginInput()
	{
		Print("> ");
		removableCharacterCount = 0;
		input.Clear();
		isAwaitingInput = true;
	}

	public void PutCharacter(char character)
	{
		if (character == '\b')
		{
			Backspace();

			return;
		}

		if (character == '\n')
		{
			NewLine();

			if (isAwaitingInput)
			{
				SubmitInput();
			}

			return;
		}

		if (isAwaitingInput)
		{
			input.Append(character);
		}

		removableCharacterCount++;
		SetCharacter(cursorX, cursorY, character, TextColor);
		MoveCursorForward();
	}

	public void Print(string text)
	{
		ArgumentNullException.ThrowIfNull(text);

		foreach (char character in text)
		{
			PutCharacter(character);
		}
	}

	public void PrintLine(string text = "")
	{
		Print(text);
		PutCharacter('\n');
	}

	public void PrintLine(string format, params object[] arguments)
	{
		ArgumentNullException.ThrowIfNull(format);
		ArgumentNullException.ThrowIfNull(arguments);
		PrintLine(string.Format(format, arguments));
	}

	public void SendInput(string text)
	{
		if (!IsEnabled || !isAwaitingInput)
		{
			return;
		}

		Print(text);
	}

	public void SetViewScroll(int scroll)
	{
		if (scroll < 0 || Height + scroll > BufferHeight)
		{
			return;
		}

		ViewScroll = scroll;
		isDirty = true;
	}

	private void SubmitInput()
	{
		isAwaitingInput = false;
		string command = input.ToString();

		if (command.Length > 0)
		{
			InputSubmitted?.Invoke(command);
		}

		BeginInput();
	}

	private void Backspace()
	{
		if (removableCharacterCount == 0)
		{
			return;
		}

		removableCharacterCount--;

		if (isAwaitingInput && input.Length > 0)
		{
			input.Length--;
		}

		MoveCursorBackward();
		SetCharacter(cursorX, cursorY, '\0', Color.White);
	}

	private void NewLine()
	{
		cursorX = 0;
		cursorY--;
		ScrollBufferIfNeeded();
	}

	private void MoveCursorForward()
	{
		cursorX++;

		if (cursorX >= BufferWidth)
		{
			NewLine();
		}

		ScrollBufferIfNeeded();
	}

	private void MoveCursorBackward()
	{
		cursorX--;

		if (cursorX < 0)
		{
			cursorX = BufferWidth - 1;
			cursorY++;
		}

		ScrollBufferIfNeeded();
	}

	private void ScrollBufferIfNeeded()
	{
		cursorY = Math.Min(cursorY, BufferHeight - 1);

		if (cursorY >= 0)
		{
			return;
		}

		cursorY = 0;

		for (int y = BufferHeight - 2; y >= 0; y--)
		{
			for (int x = 0; x < BufferWidth; x++)
			{
				SetCharacter(x, y + 1, GetCharacter(x, y), GetColor(x, y));
				SetCharacter(x, y, '\0', Color.White);
			}
		}
	}

	private void SetCharacter(int x, int y, char character, Color color)
	{
		int index = GetBufferIndex(x, y);
		characters[index] = character;
		colors[index] = color;
		isDirty = true;
	}

	private char GetCharacter(int x, int y)
	{
		return characters[GetBufferIndex(x, y)];
	}

	private Color GetColor(int x, int y)
	{
		return colors[GetBufferIndex(x, y)];
	}

	private int GetBufferIndex(int x, int y)
	{
		if ((uint)x >= (uint)BufferWidth)
		{
			throw new ArgumentOutOfRangeException(nameof(x));
		}

		if ((uint)y >= (uint)BufferHeight)
		{
			throw new ArgumentOutOfRangeException(nameof(y));
		}

		return (BufferHeight - y - 1) * BufferWidth + x;
	}
}
