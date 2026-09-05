using System;
using System.Numerics;
using FishGfx.Graphics;
using Xunit;

namespace FishGfx.Tests;

public class RenderQueueLifetimeTests
{
    [Fact]
    public void DisposeReleasesEverySubmissionEvenWhenOneReleaseThrows()
    {
        RenderQueue queue = new();
        RenderCommandBatch batch = new(new RenderCommand[] { new Noop() });
        Resource first = new(true), second = new(false);
        queue.SubmitRetained(RenderQueueBucket.Opaque, batch, Matrix4x4.Identity, first);
        queue.SubmitRetained(RenderQueueBucket.Opaque, batch, Matrix4x4.Identity, second);
        Assert.Throws<AggregateException>(() => queue.Dispose());
        queue.Dispose(); Assert.Equal(1, first.Calls); Assert.Equal(1, second.Calls);
        Assert.Throws<ObjectDisposedException>(() => queue.SubmitOpaque(batch, Matrix4x4.Identity));
        Assert.Throws<ObjectDisposedException>(() => queue.BeginExecution());
    }
    private sealed class Noop : RenderCommand { public override void Execute(RenderPass pass) { } }
    private sealed class Resource(bool fail) : IDisposable
    {
        internal int Calls;
        public void Dispose() { Calls++; if (fail) throw new InvalidOperationException("test"); }
    }
}
