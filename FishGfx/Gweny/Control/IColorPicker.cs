using System;
using System.Drawing;

namespace FishGfx.Gweny.Control {
	using Color = System.Drawing.Color;

	public interface IColorPicker {
		Color SelectedColor { get; }
	}
}
