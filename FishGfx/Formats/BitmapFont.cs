using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using FishGfx.Graphics;

namespace FishGfx.Formats;

public sealed class BitmapFont : GraphicsFont
{
	private readonly Dictionary<char, CharacterRecord> characters = new();
	private readonly Dictionary<ulong, short> kerningPairs = new();
	private readonly Dictionary<GraphicsContext, FontAtlas> atlases = new();
	private readonly string textureDirectory;
	private string fontName;
	private string pageName;
	private short sourceFontSize;
	private ushort sourceLineHeight;
	private bool disposed;

	public BitmapFont(string descriptorPath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(descriptorPath);

		string fullPath = Path.GetFullPath(descriptorPath);
		textureDirectory = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();

		using FileStream stream = File.OpenRead(fullPath);
		Read(stream);

		if (string.IsNullOrWhiteSpace(fontName))
		{
			fontName = Path.GetFileNameWithoutExtension(fullPath);
		}
	}

	public override string Name => fontName;

	public override float BaseSize => Math.Abs((int)sourceFontSize);

	public override float LineHeight => sourceLineHeight;

	public override float TabWidth => LineHeight;

	public override FontRenderMode RenderMode => FontRenderMode.Bitmap;

	public override float SdfPixelRange => 0;

	public override GlyphMetrics? GetGlyph(char character)
	{
		ThrowIfDisposed();

		if (!characters.TryGetValue(character, out CharacterRecord record))
		{
			return null;
		}

		return new GlyphMetrics(
			character,
			new Vector2(record.X, record.Y),
			new Vector2(record.Width, record.Height),
			new Vector2(record.XOffset, record.YOffset),
			record.XAdvance
		);
	}

	public override float GetKerning(char first, char second)
	{
		ThrowIfDisposed();
		ulong key = ((ulong)first << 32) | second;

		return kerningPairs.TryGetValue(key, out short amount) ? amount : 0;
	}

	public override FontAtlas PrepareAtlas(GraphicsContext graphics, string text)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(graphics);
		ArgumentNullException.ThrowIfNull(text);
		graphics.EnsureCurrent();

		if (atlases.TryGetValue(graphics, out FontAtlas cached) && !cached.IsDisposed)
		{
			return cached;
		}

		string texturePath = Path.Combine(textureDirectory, pageName);
		Texture texture = graphics.LoadTexture(
			texturePath,
			new TextureLoadOptions
			{
				Sampling = new TextureSamplingState(
					TextureFilter.Nearest,
					TextureFilter.Nearest
				),
			}
		);
		FontAtlas atlas = new(graphics, texture, RenderMode, SdfPixelRange);
		atlases[graphics] = atlas;

		return atlas;
	}

	public override void Dispose()
	{
		if (disposed)
		{
			return;
		}

		disposed = true;

		foreach (FontAtlas atlas in atlases.Values)
		{
			atlas.Dispose();
		}

		atlases.Clear();
	}

	private void Read(Stream stream)
	{
		using BinaryReader reader = new(stream, Encoding.UTF8, leaveOpen: true);
		string magic = Encoding.ASCII.GetString(reader.ReadBytes(3));

		if (magic != "BMF")
		{
			throw new InvalidDataException("The file is not a binary BMFont descriptor.");
		}

		byte version = reader.ReadByte();

		if (version != 3)
		{
			throw new InvalidDataException($"BMFont version {version} is unsupported; expected version 3.");
		}

		while (stream.Position < stream.Length)
		{
			byte blockType = reader.ReadByte();
			int blockLength = reader.ReadInt32();

			if (blockLength < 0 || blockLength > stream.Length - stream.Position)
			{
				throw new InvalidDataException("The BMFont descriptor contains an invalid block length.");
			}

			long blockEnd = stream.Position + blockLength;
			ReadBlock(reader, blockType, blockLength);

			if (stream.Position != blockEnd)
			{
				throw new InvalidDataException($"BMFont block {blockType} has an invalid payload length.");
			}
		}

		if (sourceFontSize == 0 || sourceLineHeight == 0)
		{
			throw new InvalidDataException("The BMFont descriptor is missing required metrics.");
		}

		if (string.IsNullOrWhiteSpace(pageName))
		{
			throw new InvalidDataException("The BMFont descriptor does not define an atlas page.");
		}
	}

	private void ReadBlock(BinaryReader reader, byte blockType, int blockLength)
	{
		switch (blockType)
		{
			case 1:
				ReadInfoBlock(reader, blockLength);
				break;

			case 2:
				ReadCommonBlock(reader, blockLength);
				break;

			case 3:
				ReadPageBlock(reader, blockLength);
				break;

			case 4:
				ReadCharacterBlock(reader, blockLength);
				break;

			case 5:
				ReadKerningBlock(reader, blockLength);
				break;

			default:
				reader.BaseStream.Seek(blockLength, SeekOrigin.Current);
				break;
		}
	}

	private void ReadInfoBlock(BinaryReader reader, int blockLength)
	{
		int fixedSize = Marshal.SizeOf<InfoRecord>();

		if (blockLength < fixedSize + 1)
		{
			throw new InvalidDataException("The BMFont info block is truncated.");
		}

		InfoRecord info = reader.ReadStruct<InfoRecord>();
		sourceFontSize = info.FontSize;
		byte[] nameBytes = reader.ReadBytes(blockLength - fixedSize);
		fontName = Encoding.UTF8.GetString(nameBytes).TrimEnd('\0');
	}

	private void ReadCommonBlock(BinaryReader reader, int blockLength)
	{
		if (blockLength != Marshal.SizeOf<CommonRecord>())
		{
			throw new InvalidDataException("The BMFont common block has an unexpected size.");
		}

		CommonRecord common = reader.ReadStruct<CommonRecord>();
		sourceLineHeight = common.LineHeight;

		if (common.Pages != 1)
		{
			throw new NotSupportedException("BitmapFont currently supports exactly one atlas page.");
		}
	}

	private void ReadPageBlock(BinaryReader reader, int blockLength)
	{
		pageName = Encoding.UTF8.GetString(reader.ReadBytes(blockLength)).TrimEnd('\0');
	}

	private void ReadCharacterBlock(BinaryReader reader, int blockLength)
	{
		int recordSize = Marshal.SizeOf<CharacterRecord>();

		if (blockLength % recordSize != 0)
		{
			throw new InvalidDataException("The BMFont character block is misaligned.");
		}

		for (int index = 0; index < blockLength / recordSize; index++)
		{
			CharacterRecord character = reader.ReadStruct<CharacterRecord>();

			if (character.Id > char.MaxValue || character.Page != 0)
			{
				continue;
			}

			characters[(char)character.Id] = character;
		}
	}

	private void ReadKerningBlock(BinaryReader reader, int blockLength)
	{
		int recordSize = Marshal.SizeOf<KerningRecord>();

		if (blockLength % recordSize != 0)
		{
			throw new InvalidDataException("The BMFont kerning block is misaligned.");
		}

		for (int index = 0; index < blockLength / recordSize; index++)
		{
			KerningRecord pair = reader.ReadStruct<KerningRecord>();
			ulong key = ((ulong)pair.First << 32) | pair.Second;
			kerningPairs[key] = pair.Amount;
		}
	}

	private void ThrowIfDisposed()
	{
		if (disposed)
		{
			throw new ObjectDisposedException(nameof(BitmapFont));
		}
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	private struct InfoRecord
	{
		internal short FontSize;
		internal byte BitField;
		internal byte CharacterSet;
		internal ushort StretchHeight;
		internal byte Supersampling;
		internal byte PaddingUp;
		internal byte PaddingRight;
		internal byte PaddingDown;
		internal byte PaddingLeft;
		internal byte HorizontalSpacing;
		internal byte VerticalSpacing;
		internal byte Outline;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	private struct CommonRecord
	{
		internal ushort LineHeight;
		internal ushort Baseline;
		internal ushort TextureWidth;
		internal ushort TextureHeight;
		internal ushort Pages;
		internal byte BitField;
		internal byte AlphaChannel;
		internal byte RedChannel;
		internal byte GreenChannel;
		internal byte BlueChannel;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	private struct CharacterRecord
	{
		internal uint Id;
		internal ushort X;
		internal ushort Y;
		internal ushort Width;
		internal ushort Height;
		internal short XOffset;
		internal short YOffset;
		internal short XAdvance;
		internal byte Page;
		internal byte Channel;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	private struct KerningRecord
	{
		internal uint First;
		internal uint Second;
		internal short Amount;
	}
}
