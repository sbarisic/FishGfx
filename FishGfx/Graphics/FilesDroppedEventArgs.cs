using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace FishGfx.Graphics;

public sealed class FilesDroppedEventArgs : EventArgs
{
    public IReadOnlyList<string> Paths { get; }

    public FilesDroppedEventArgs(string[] paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        Paths = Array.AsReadOnly((string[])paths.Clone());
    }

    internal static FilesDroppedEventArgs FromNativePaths(int count, IntPtr paths)
    {
        string[] files = new string[count];
        for (int index = 0; index < count; index++)
        {
            files[index] = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(paths, index * IntPtr.Size)) ?? string.Empty;
        }
        return new FilesDroppedEventArgs(files);
    }
}
