using System;
using System.Drawing;
using FishGfx.Gweny.Control;

namespace FishGfx.Gweny.DragDrop
{
    public class Package
    {
        public string Name;
        public object UserData;
        public bool IsDraggable;
        public Base DrawControl;
        public Point HoldOffset;
    }
}
