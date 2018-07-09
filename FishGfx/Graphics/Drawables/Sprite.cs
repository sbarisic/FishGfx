using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;

namespace FishGfx.Graphics.Drawables {
	public class Sprite : IDrawable {
		Mesh2D Mesh;
		bool Dirty;

		Vector2 _Size;
		public Vector2 Size {
			get {
				return _Size;
			}
			set {
				_Size = value;
				Dirty = true;
			}
		}

		Vector2 _Position;
		public Vector2 Position {
			get {
				return _Position;
			}
			set {
				_Position = value;
				Dirty = true;
			}
		}

		public Texture Texture;

		public Sprite(Vector2 Position, Vector2 Size) {
			this.Size = Size;
			this.Position = Position;
			Mesh = new Mesh2D();

			Mesh.SetUVs(new Vector2[] {
				new Vector2(0, 0),
				new Vector2(1, 0),
				new Vector2(1, 1),
				new Vector2(0, 0),
				new Vector2(1, 1),
				new Vector2(0, 1)
			});
		}

		public Sprite(float X, float Y, float W, float H) : this(new Vector2(X, Y), new Vector2(W, H)) {
		}

		public void SetColors(Color[] Colors) {
			if (Colors.Length != 6)
				throw new Exception("Colors has to have 6 elements");

			Mesh.SetColors(Colors);
		}

		public void SetColors(Color C) {
			SetColors(new Color[] { C, C, C, C, C, C });
		}

		public void Draw() {
			if (Dirty) {
				Dirty = false;

				Mesh.SetVertices(new Vector2[] {
					new Vector2(0, 0) + Position,
					new Vector2(Size.X, 0) + Position,
					new Vector2(Size.X, Size.Y) + Position,
					new Vector2(0, 0) + Position,
					new Vector2(Size.X, Size.Y) + Position,
					new Vector2(0, Size.Y) + Position,
				});
			}

			Texture?.BindTextureUnit();
			Mesh.Draw();
			Texture?.UnbindTextureUnit();
		}
	}
}
