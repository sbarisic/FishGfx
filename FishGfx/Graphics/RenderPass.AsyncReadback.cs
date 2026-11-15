using System;
using System.Numerics;
using Silk.NET.OpenGL;

namespace FishGfx.Graphics;

public sealed partial class RenderPass
{
    /// <summary>Queues one RGBA float pixel from a 1x1 target without waiting for the GPU.</summary>
    public AsyncPixelReadback ReadPixelAsync()
    {
        EnsureActive();
        if (target.IsBackbuffer || target.Width != 1 || target.Height != 1 || target.SampleCount != 1 || target.ColorAttachments.Count == 0)
            throw new InvalidOperationException("Async pixel readback requires a single-sample 1x1 color target.");
        return new AsyncPixelReadback();
    }
}

/// <summary>A graphics-thread-only readback. Poll without waiting, then dispose.</summary>
public sealed class AsyncPixelReadback : IDisposable
{
    uint buffer;
    nint fence;
    bool finished;
    public bool Failed { get; private set; }

    internal unsafe AsyncPixelReadback()
    {
        var gl = Internal_OpenGL.GL;
        gl.GetInteger((GetPName)0x88ED, out int previousBuffer);
        gl.GetInteger((GetPName)0x0C02, out int previousReadBuffer);
        int[] names = [0x0D05, 0x0D02, 0x0D03, 0x0D04];
        int[] values = new int[4];
        for (int i = 0; i < names.Length; i++) gl.GetInteger((GetPName)names[i], out values[i]);
        try
        {
            buffer = gl.GenBuffer();
            gl.BindBuffer(BufferTargetARB.PixelPackBuffer, buffer);
            gl.BufferData(BufferTargetARB.PixelPackBuffer, 16, null, BufferUsageARB.StreamRead);
            for (int i = 0; i < names.Length; i++) gl.PixelStore((PixelStoreParameter)names[i], i == 0 ? 1 : 0);
            gl.ReadBuffer(ReadBufferMode.ColorAttachment0);
            gl.ReadPixels(0, 0, 1, 1, PixelFormat.Rgba, PixelType.Float, null);
            fence = gl.FenceSync(SyncCondition.SyncGpuCommandsComplete, 0u);
            if (fence == 0) throw new InvalidOperationException("Could not create pixel readback fence.");
        }
        catch { Dispose(); throw; }
        finally
        {
            for (int i = 0; i < names.Length; i++) gl.PixelStore((PixelStoreParameter)names[i], values[i]);
            gl.ReadBuffer((ReadBufferMode)previousReadBuffer);
            gl.BindBuffer(BufferTargetARB.PixelPackBuffer, (uint)previousBuffer);
        }
    }

    public unsafe bool TryGetResult(out Vector4 value)
    {
        value = default;
        if (finished || fence == 0) return false;
        var gl = Internal_OpenGL.GL;
        var status = gl.ClientWaitSync(fence, 0u, 0);
        if (status == GLEnum.TimeoutExpired) return false;
        if (status == GLEnum.WaitFailed) { Failed = true; finished = true; return false; }
        gl.GetInteger((GetPName)0x88ED, out int previousBuffer);
        try
        {
            gl.BindBuffer(BufferTargetARB.PixelPackBuffer, buffer);
            Vector4 pixel = default;
            gl.GetBufferSubData(BufferTargetARB.PixelPackBuffer, 0, 16, &pixel);
            value = pixel;
            finished = true;
            return true;
        }
        finally { gl.BindBuffer(BufferTargetARB.PixelPackBuffer, (uint)previousBuffer); }
    }

    public void Dispose()
    {
        if (fence != 0) Internal_OpenGL.GL.DeleteSync(fence);
        if (buffer != 0) Internal_OpenGL.GL.DeleteBuffer(buffer);
        fence = 0;
        buffer = 0;
        finished = true;
    }
}
