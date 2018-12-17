using FishGfx;
using FishGfx.Graphics;
using NuklearDotNet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FishGfx_Nuklear {
	public class FishGfxDevice : NuklearDeviceTex<Texture> {
		public override Texture CreateTexture(int W, int H, IntPtr Data) {
			return Texture.FromPixels(W, H, Data);
		}

		public override void SetBuffer(NkVertex[] VertexBuffer, ushort[] IndexBuffer) {
			throw new NotImplementedException();
		}

		public override void Render(NkHandle Userdata, Texture Texture, NkRect ClipRect, uint Offset, uint Count) {
			throw new NotImplementedException();
		}
	}
}
