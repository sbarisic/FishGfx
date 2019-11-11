using FishGfx.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace FishGfx.Formats {
	public unsafe class BMFont : GfxFont {
		[StructLayout(LayoutKind.Sequential, Pack = 1)]
		public struct InfoBlock {
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
		public struct CommonBlock {
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
		public struct CharBlock {
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

		public InfoBlock Info;
		public string FntName;
		public CommonBlock Common;
		public Dictionary<string, Texture> PageNames;
		Dictionary<char, CharBlock> Chars;

		public BMFont(string FntFile = null, float FontSize = -1) {
			if (FntFile != null) {
				using (MemoryStream MS = new MemoryStream()) {
					using (FileStream FS = File.OpenRead(FntFile)) {
						FS.CopyTo(MS);
					}

					MS.Position = 0;
					Read(MS);

					LoadTextures(Path.GetDirectoryName(FntFile));
				}
			}

			if (FontSize <= 0)
				this.ScaledFontSize = this.FontSize;
			else
				this.ScaledFontSize = FontSize;
		}

		public void LoadTextures(string TextureDirectory, TextureFilter Filter = TextureFilter.Nearest) {
			string[] TextureNames = PageNames.Select(KV => KV.Key).ToArray();

			foreach (var TexName in TextureNames) {
				Texture T = Texture.FromFile(Path.Combine(TextureDirectory, TexName));
				T.SetFilter(Filter);
				PageNames[TexName] = T;

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
								Info = BR.ReadStruct<InfoBlock>();
								FntName = Encoding.UTF8.GetString(BR.ReadBytes(Len - sizeof(InfoBlock) - 1));
								BR.ReadByte();
								break;
							}

						case 2: {
								Common = BR.ReadStruct<CommonBlock>();
								break;
							}

						case 3: {
								int Pages = Common.Pages;
								PageNames = new Dictionary<string, Texture>();

								int PageNameLen = Len / Pages;
								for (int i = 0; i < Pages; i++)
									PageNames.Add(Encoding.UTF8.GetString(BR.ReadBytes(PageNameLen)).TrimEnd(new[] { '\0' }), null);

								break;
							}

						case 4: {
								int CharCount = Len / sizeof(CharBlock);
								Chars = new Dictionary<char, CharBlock>();

								for (int i = 0; i < CharCount; i++) {
									CharBlock CharInfo = BR.ReadStruct<CharBlock>();
									Chars.Add((char)CharInfo.ID, CharInfo);
								}

								break;
							}

						case 5: {
								throw new NotImplementedException("Kerning pairs not implemented");
							}

						default:
							throw new NotImplementedException("Invalid block type? " + BlockType);
					}
				}
			}
		}

		public CharBlock GetChar(char C) {
			if (Chars.ContainsKey(C))
				return Chars[C];

			return default(CharBlock);
		}

		public override string FontName => FntName;

		public override int LineHeight => Common.LineHeight;

		public override int FontSize {
			get {
				if (Info.FontSize < 0)
					return -Info.FontSize;

				return Info.FontSize;
			}
		}

		public override int TabSize => LineHeight;

		public override CharOrigin? GetCharInfo(char C) {
			if (Chars.ContainsKey(C)) {
				CharBlock CBlock = Chars[C];

				CharOrigin CInfo = new CharOrigin();
				CInfo.Char = C;
				CInfo.Owner = this;
				CInfo.W = CBlock.Width;
				CInfo.H = CBlock.Height;
				CInfo.XOffset = CBlock.XOffset;
				CInfo.YOffset = CBlock.YOffset;
				CInfo.XAdvance = CBlock.XAdvance;
				CInfo.X = CBlock.X;
				CInfo.Y = CBlock.Y;

				return CInfo;
			}

			return null;
		}
	}
}
