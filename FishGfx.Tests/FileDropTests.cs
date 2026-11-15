using System.Runtime.InteropServices;
using FishGfx.Graphics;
using Xunit;

namespace FishGfx.Tests;

public sealed class FileDropTests
{
    [Fact]
    public void CopiesUtf8PathsBeforeNativeCallbackReturns()
    {
        string[] expected = [@"C:\logs\vožnja.mlg", @"C:\logs\測試 log.mlg"];
        IntPtr pointers = Marshal.AllocHGlobal(IntPtr.Size * expected.Length);
        IntPtr[] strings = expected.Select(Marshal.StringToCoTaskMemUTF8).ToArray();
        FilesDroppedEventArgs args;
        try
        {
            for (int index = 0; index < strings.Length; index++)
            {
                Marshal.WriteIntPtr(pointers, index * IntPtr.Size, strings[index]);
            }
            args = FilesDroppedEventArgs.FromNativePaths(strings.Length, pointers);
        }
        finally
        {
            foreach (IntPtr value in strings)
            {
                Marshal.FreeCoTaskMem(value);
            }
            Marshal.FreeHGlobal(pointers);
        }
        Assert.Equal(expected, args.Paths);
    }

    [Fact]
    public void EventPathsDoNotChangeWhenSourceArrayChanges()
    {
        string[] paths = ["first.mlg"];
        var args = new FilesDroppedEventArgs(paths);
        paths[0] = "other.mlg";
        Assert.Equal("first.mlg", args.Paths[0]);
    }
}
