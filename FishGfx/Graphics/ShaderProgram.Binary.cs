using System;
using System.Buffers.Binary;
using Silk.NET.OpenGL;

namespace FishGfx.Graphics;

public unsafe sealed partial class ShaderProgram
{
    internal ShaderProgram(GraphicsContext owner, ReadOnlySpan<byte> binary) : base(owner)
    {
        if (binary.Length is < 5 or > 64 * 1024 * 1024) throw new ArgumentException("Invalid shader binary size.");
        stages = Array.Empty<ShaderStage>();
        Handle = Internal_OpenGL.GL.CreateProgram();
        try
        {
            fixed (byte* data = binary)
                Internal_OpenGL.GL.ProgramBinary(Handle, (GLEnum)BinaryPrimitives.ReadUInt32LittleEndian(binary), data + 4, (uint)(binary.Length - 4));
            Internal_OpenGL.GL.GetProgram(Handle, ProgramPropertyARB.LinkStatus, out int status);
            if (status == 0) throw new InvalidOperationException("Driver rejected cached shader program.");
            RegisterResource();
        }
        catch
        {
            Internal_OpenGL.GL.DeleteProgram(Handle);
            Handle = 0;
            throw;
        }
    }

    public byte[] GetBinary()
    {
        EnsureCurrentOwner();
        if (Owner.Capabilities.Version < new OpenGlVersion(4, 1)) return Array.Empty<byte>();
        Internal_OpenGL.GL.GetProgram(Handle, (ProgramPropertyARB)0x8741, out int length);
        if (length is <= 0 or > 64 * 1024 * 1024) return Array.Empty<byte>();
        byte[] data = new byte[length + 4];
        uint actual;
        GLEnum format;
        fixed (byte* bytes = data)
            Internal_OpenGL.GL.GetProgramBinary(Handle, (uint)length, out actual, out format, bytes + 4);
        BinaryPrimitives.WriteUInt32LittleEndian(data, (uint)format);
        Array.Resize(ref data, checked((int)actual) + 4);
        return data;
    }
}
