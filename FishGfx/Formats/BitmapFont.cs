using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace FishGfx.Formats {
	public unsafe class BitmapFont {
		[StructLayout(LayoutKind.Sequential, Pack = 1)]
		public struct Block_Info {
			public short FontSize;
			public byte BitField;
			public byte CharSet;
			public ushort StretchH;
			public byte AA;
			public byte PaddingUp;
			public byte PaddingRight;
			public byte PaddingDown;
			public byte PaddingLeft;
			public byte SpacingHoriz;
			public byte SpacingVert;
			public byte Outline;
		}

		[StructLayout(LayoutKind.Sequential, Pack = 1)]
		public struct Block_Common {
			public ushort LineHeight;
			public ushort Base;
			public ushort ScaleW;
			public ushort ScaleH;
			public ushort Pages;
			public byte BitField;
			public byte AlphaC;
			public byte RedC;
			public byte GreenC;
			public byte BlueC;
		}

		[StructLayout(LayoutKind.Sequential, Pack = 1)]
		public struct Block_Char {
			public uint ID;
			public ushort X;
			public ushort Y;
			public ushort Width;
			public ushort Height;
			public short XOffset;
			public short YOffset;
			public short XAdvance;
			public byte Page;
			public byte Channel;
		}

		public Block_Info Info;
		public string FontName;
		public Block_Common Common;
		public string[] PageNames;

		Dictionary<char, Block_Char> Chars;

		public BitmapFont(string FntFile) {
			using (MemoryStream MS = new MemoryStream()) {
				using (FileStream FS = File.OpenRead(FntFile)) {
					FS.CopyTo(MS);
				}

				MS.Position = 0;
				Read(MS);
			}
		}

		public void Read(MemoryStream S) {
			using (BinaryReader BR = new BinaryReader(S)) {
				string Magic = Encoding.ASCII.GetString(BR.ReadBytes(3));
				if (Magic != "BMF")
					throw new Exception("Invalid BMF font file");

				byte Ver = BR.ReadByte();
				if (Ver != 3)
					throw new Exception("Only BMF v3 is supported");

				while (S.Position < S.Length - 1) {
					byte BlockType = BR.ReadByte();
					int Len = BR.ReadInt32();

					switch (BlockType) {
						case 1: {
								Info = BR.ReadStruct<Block_Info>();
								FontName = Encoding.UTF8.GetString(BR.ReadBytes(Len - sizeof(Block_Info) - 1));
								BR.ReadByte();
								break;
							}

						case 2: {
								Common = BR.ReadStruct<Block_Common>();
								break;
							}

						case 3: {
								int Pages = Common.Pages;
								PageNames = new string[Pages];

								int PageNameLen = Len / Pages;
								for (int i = 0; i < PageNames.Length; i++)
									PageNames[i] = Encoding.UTF8.GetString(BR.ReadBytes(PageNameLen)).TrimEnd(new[] { '\0' });

								break;
							}

						case 4: {
								int CharCount = Len / sizeof(Block_Char);
								Chars = new Dictionary<char, Block_Char>();

								for (int i = 0; i < CharCount; i++) {
									Block_Char CharInfo = BR.ReadStruct<Block_Char>();
									Chars.Add((char)CharInfo.ID, CharInfo);
								}

								break;
							}

						default:
							throw new NotImplementedException("Invalid block type? " + BlockType);
					}
				}
			}
		}

		public Block_Char GetChar(char C) {
			if (Chars.ContainsKey(C))
				return Chars[C];

			return default(Block_Char);
		}
	}
}
