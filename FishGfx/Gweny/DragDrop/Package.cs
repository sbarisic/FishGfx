using System;
using System.Drawing;
using Gweny.Control;

namespace Gweny.DragDrop
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
