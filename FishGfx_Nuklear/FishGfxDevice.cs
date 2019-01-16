using FishGfx;
using FishGfx.Graphics;
using FishGfx.Graphics.Drawables;
using NuklearDotNet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace FishGfx_Nuklear {
	public class FishGfxDevice : NuklearDeviceTex<Texture> {
		Mesh2D Msh;
		ShaderProgram GUIShader;

		public Vector2 WindowSize;
		public bool EventsEnabled;

		public FishGfxDevice(Vector2 WindowSize, ShaderProgram GUIShader) {
			this.GUIShader = GUIShader;
			this.WindowSize = WindowSize;

			EventsEnabled = true;
			Msh = new Mesh2D(BufferUsage.DynamicDraw);
			Msh.PrimitiveType = PrimitiveType.Triangles;
		}

		public override Texture CreateTexture(int W, int H, IntPtr Data) {
			return Texture.FromPixels(W, H, Data);
		}

		public override void SetBuffer(NkVertex[] VertexBuffer, ushort[] IndexBuffer) {
			Vertex2[] Verts = new Vertex2[VertexBuffer.Length];

			for (int i = 0; i < VertexBuffer.Length; i++) {
				NkVertex V = VertexBuffer[i];
				Verts[i] = new Vertex2(V.Position * new Vector2(1, -1) + new Vector2(0, WindowSize.Y), V.UV, new Color(V.Color.R, V.Color.G, V.Color.B, V.Color.A));
			}

			Msh.SetVertices(Verts);
			Msh.SetElements(IndexBuffer.Select(I => (uint)I).ToArray());
		}

		public override void BeginRender() {
			RenderState RS = Gfx.PeekRenderState();
			RS.EnableCullFace = false;
			RS.EnableDepthTest = false;
			RS.EnableBlend = true;
			Gfx.PushRenderState(RS);
		}

		public override void Render(NkHandle Userdata, Texture Texture, NkRect ClipRect, uint Offset, uint Count) {
			//Gfx.Rectangle(ClipRect.X, WindowSize.Y - ClipRect.Y - ClipRect.H, ClipRect.W, ClipRect.H);
			RenderState RS = Gfx.PeekRenderState();
			RS.EnableScissorTest = true;
			RS.ScissorRegion = new AABB((int)ClipRect.X, (int)(WindowSize.Y - ClipRect.Y - ClipRect.H), (int)ClipRect.W, (int)ClipRect.H);
			Gfx.PushRenderState(RS);

			GUIShader.Bind(ShaderUniforms.Current);
			Texture.BindTextureUnit();
			Msh.DrawEx((int)Offset, (int)Count);
			Texture.UnbindTextureUnit();
			GUIShader.Unbind();

			Gfx.PopRenderState();
		}

		public override void EndRender() {
			Gfx.PopRenderState();
		}

		// Events

		public virtual void RegisterEvents(RenderWindow RWind) {
			int MouseX = 0;
			int MouseY = 0;

			RWind.OnMouseMove += (Wnd, X, Y) => {
				if (!EventsEnabled)
					return;

				OnMouseMove(MouseX = (int)X, MouseY = (int)Y);
			};

			RWind.OnKey += (Wnd, Key, Scancode, Pressed, Repeat, Mods) => {
				if (!EventsEnabled)
					return;

				if (TryConvertMouseButton(Key, out NuklearEvent.MouseButton B))
					OnMouseButton(B, MouseX, MouseY, Pressed);
				else if (TryConvertOnKey(Key, Pressed, Repeat, Mods, out NkKeys K))
					OnKey(K, Pressed);
			};

			RWind.OnChar += (Wnd, Char, Unicode) => {
				if (!EventsEnabled)
					return;

				OnText(Char.ToString());
			};
		}

		static bool TryConvertMouseButton(Key K, out NuklearEvent.MouseButton B) {
			B = 0;

			switch (K) {
				case Key.MouseLeft:
					B = NuklearEvent.MouseButton.Left;
					return true;

				case Key.MouseMiddle:
					B = NuklearEvent.MouseButton.Middle;
					return true;

				case Key.MouseRight:
					B = NuklearEvent.MouseButton.Right;
					return true;
			}

			return false;
		}

		static bool TryConvertOnKey(Key K, bool Down, bool Repeat, KeyMods Mods, out NkKeys NkKey) {
			NkKey = NkKeys.Enter;

			switch (K) {
				case Key.LeftShift:
				case Key.RightShift:
					NkKey = NkKeys.Shift;
					return true;

				case Key.LeftControl:
				case Key.RightControl:
					NkKey = NkKeys.Ctrl;
					return true;

				case Key.Delete:
					NkKey = NkKeys.Del;
					return true;

				case Key.Enter:
				case Key.NumpadEnter:
					NkKey = NkKeys.Enter;
					return true;

				case Key.Tab:
					NkKey = NkKeys.Tab;
					return true;

				case Key.Backspace:
					NkKey = NkKeys.Backspace;
					return true;

				case Key.C: {
						if (Mods == KeyMods.Control) {
							NkKey = NkKeys.Copy;
							return true;
						}

						return false;
					}

				case Key.X: {
						if (Mods == KeyMods.Control) {
							NkKey = NkKeys.Cut;
							return true;
						}

						return false;
					}

				case Key.V: {
						if (Mods == KeyMods.Control) {
							NkKey = NkKeys.Paste;
							return true;
						}

						return false;
					}

				case Key.Up: {
						if (Mods == KeyMods.Control) {
							NkKey = NkKeys.ScrollUp;
							return true;
						} else if (Mods == 0) {
							NkKey = NkKeys.Up;
							return true;
						}

						return false;
					}

				case Key.Down: {
						if (Mods == KeyMods.Control) {
							NkKey = NkKeys.ScrollDown;
							return true;
						} else if (Mods == 0) {
							NkKey = NkKeys.Down;
							return true;
						}

						return false;
					}

				case Key.Left: {
						if (Mods == 0) {
							NkKey = NkKeys.Left;
							return true;
						} else if (Mods == KeyMods.Control) {
							NkKey = NkKeys.TextWordLeft;
							return true;
						}

						return false;
					}

				case Key.Right: {
						if (Mods == 0) {
							NkKey = NkKeys.Right;
							return true;
						} else if (Mods == KeyMods.Control) {
							NkKey = NkKeys.TextWordRight;
							return true;
						}

						return false;
					}

				case Key.Insert:
					NkKey = NkKeys.ReplaceMode;
					NkKey = NkKeys.InserMode;
					return true;

				case Key.Home: {
						if (Mods == KeyMods.Control) {
							NkKey = NkKeys.TextStart;
							return true;
						} else if (Mods == 0) {
							NkKey = NkKeys.LineStart;
							return true;
						}

						return false;
					}

				case Key.End: {
						if (Mods == KeyMods.Control) {
							NkKey = NkKeys.TextEnd;
							return true;
						} else if (Mods == 0) {
							NkKey = NkKeys.LineEnd;
							return true;
						}
						return false;
					}

				case Key.A: {
						if (Mods == KeyMods.Control) {
							NkKey = NkKeys.TextSelectAll;
							return true;
						}

						return false;
					}

				case Key.Z: {
						if (Mods == KeyMods.Control) {
							NkKey = NkKeys.TextUndo;
							return true;
						}

						return false;
					}

				case Key.Y: {
						if (Mods == KeyMods.Control) {
							NkKey = NkKeys.TextRedo;
							return true;
						}

						return false;
					}

				case Key.PageUp:
					NkKey = NkKeys.ScrollStart;
					return true;

				case Key.PageDown:
					NkKey = NkKeys.ScrollEnd;
					return true;
			}

			return false;
		}
	}
}