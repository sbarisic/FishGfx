using System.Buffers;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FishGfx;
using FishGfx.Graphics;
using FishGfx.Formats;
using FishGfx.Voxels;
using Silk.NET.OpenGL;

internal static class GraphicsProbes
{
    internal static void Verify()
    {
        using RenderWindow modern = new(new RenderWindowOptions { Width = 320, Height = 180, Title = "FishGfx resource checks" });
        using RenderWindow legacy = new(new RenderWindowOptions { Width = 320, Height = 180, PreferredVersion = new(4, 0), MinimumVersion = new(4, 0), RequireExactVersion = true, Title = "FishGfx GL 4.0 checks" });
        using TrueTypeFont font = new(Path.Combine(AppContext.BaseDirectory, "data/fonts/Consolas-Regular.ttf"),
            new TrueTypeFontOptions { PreloadPrintableAscii = false });
        foreach (RenderWindow window in new[] { modern, legacy, modern, legacy })
        {
            window.Graphics.MakeCurrent();
            Check(Internal_OpenGL.MajorVersion == window.Graphics.Capabilities.Version.Major && Internal_OpenGL.MinorVersion == window.Graphics.Capabilities.Version.Minor, "Wrong context capabilities restored.");
            FontAtlas atlas = font.PrepareAtlas(window.Graphics, "Batch \U0001F600");
            for (int i = 0; i < 100; i++)
            {
                font.Measure("Batch \U0001F600", 16);
                font.Layout("Batch \U0001F600", 16);
                Check(ReferenceEquals(atlas, font.PrepareAtlas(window.Graphics, "Batch \U0001F600")), "Warm text replaced the atlas.");
            }
            foreach (bool vao in new[] { false, true })
            {
                WeakReference reference = AbandonBoundResource(window.Graphics, vao);
                for (int i = 0; i < 5 && reference.IsAlive; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
                Check(!reference.IsAlive, "Resource wrapper remained rooted.");
                window.Graphics.CollectGarbage();
                string field = vao ? "vertexArray" : "arrayBuffer";
                uint cached = (uint)typeof(OpenGlBindingCache).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window.Graphics.BindingCache)!;
                Check(cached == uint.MaxValue, "Finalized deletion did not invalidate bindings.");
                using GraphicsBuffer replacement = window.Graphics.CreateBuffer<int>(new[] { 42 }, BufferBindFlags.Vertex);
                window.Graphics.BindBuffer(BufferTargetARB.ArrayBuffer, replacement.Handle);
                Internal_OpenGL.GL.GetInteger(GetPName.ArrayBufferBinding, out int actual);
                Check((uint)actual == replacement.Handle, "Replacement buffer was not bound.");
            }
        }
        Console.WriteLine(JsonSerializer.Serialize(new { check = "resource-finalization-and-mixed-contexts", passed = true, renderer = modern.Graphics.Capabilities.Renderer }));
        Console.WriteLine(JsonSerializer.Serialize(new { check = "warm-atlas", requests = 400, atlasReplacements = 0 }));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AbandonBoundResource(GraphicsContext graphics, bool vao)
    {
        if (vao)
        {
            FishGfx.Graphics.VertexArray array = graphics.CreateVertexArray();
            graphics.BindVertexArray(array.Handle);
            return new WeakReference(array);
        }
        GraphicsBuffer buffer = graphics.CreateBuffer<int>(new[] { 17 }, BufferBindFlags.Vertex);
        graphics.BindBuffer(BufferTargetARB.ArrayBuffer, buffer.Handle);
        return new WeakReference(buffer);
    }

    internal static void Geometry(Action<string, Action, int> measure)
    {
        using RenderWindow window = new(new RenderWindowOptions { Width = 320, Height = 180, Title = "FishGfx geometry retention" });
        using VoxelGeometryPagePool pool = new(window.Graphics, 1024 * 1024);
        VoxelVertex[] vertices = new VoxelVertex[10002];
        measure("geometry-pages-fill-release", () =>
        {
            VoxelGeometryAllocation[] allocations = Enumerable.Range(0, 8).Select(_ => pool.Update(null, vertices, Vector3.Zero)).ToArray();
            allocations[0].Retain();
            foreach (var allocation in allocations) allocation.ReleaseOwner();
            Check(pool.AllocationCount == 1, "Queued allocation lost ownership.");
            allocations[0].ReleaseRetained();
            Check(pool.AllocationCount == 0, "Released allocations remain active.");
        }, 1);
        int before = pool.PageCount;
        var retained = pool.Update(null, vertices, Vector3.Zero);
        retained.Retain(); retained.ReleaseOwner();
        pool.TrimEmptyPages(0);
        for (int i = 0; i < before; i++) pool.TrimEmptyPages(3);
        Check(pool.AllocationCount == 1 && pool.PageCount == 2, "Trim released queued geometry or retained extra pages.");
        retained.ReleaseRetained();
        for (int i = 0; i < before; i++) pool.TrimEmptyPages(6);
        Check(pool.PageCount == 1, "Empty pages were not reclaimed.");
        Console.WriteLine(JsonSerializer.Serialize(new { name = "geometry-retention", beforePages = before, retainedPages = pool.PageCount, activeAllocations = pool.AllocationCount, nominalRetainedVertexBytes = pool.PageCount * 1024 * 1024, queuedOwnershipVerified = true }));
    }

    internal static void CpuExperiments(Action<string, Action, int> measure)
    {
        var store = (VoxelTransparentGeometryStore)RuntimeHelpers.GetUninitializedObject(typeof(VoxelTransparentGeometryStore));
        VoxelTransparentFaceRecord[] records = Enumerable.Range(0, 10000).Select(i => new VoxelTransparentFaceRecord(new(0, 0, -i), (uint)(i * 6), 6, default, i)).ToArray();
        VoxelTransparentAllocation allocation = new(store, 0, 60000);
        allocation.SetGeometry(60000, records);
        var source = new VoxelTransparentOrderingSource(new[] { new VoxelTransparentOrderingChunk(AxisAlignedBoundingBox.FromPositionAndSize(new(-1, -1, -10000), new(2, 2, 10001)), allocation) }, 1, 1);
        try
        {
            var request = new VoxelTransparentOrderingRequest(source, default, Vector3.Zero, -Vector3.UnitZ, 50000, false, VoxelTransparentInvalidationReason.Translation, 1);
            measure("transparent-10000", () => { using var result = VoxelTransparentOrderingScheduler.Build(request); Check(result.FaceCount == 10000, "Sort lost faces."); }, 5);
            measure("sort-scratch-full-clear", () => ArrayPool<SortScratch>.Shared.Return(ArrayPool<SortScratch>.Shared.Rent(10000), true), 100);
            measure("sort-scratch-visible-no-clear", () => ArrayPool<SortScratch>.Shared.Return(ArrayPool<SortScratch>.Shared.Rent(1000), false), 100);
        }
        finally { source.ReleaseOwner(); }
        using RenderQueue queue = new();
        RenderCommandBatch batch = new(new RenderCommand[] { new Noop() });
        measure("queue-1000-submit-clear", () => { for (int i = 0; i < 1000; i++) queue.SubmitOpaque(batch, Matrix4x4.Identity); queue.Clear(); }, 5);
        List<int> reusable = new(1024);
        measure("bucket-storage-new", () => { List<int> items = new(); for (int i = 0; i < 1000; i++) items.Add(i); }, 100);
        measure("bucket-storage-reuse", () => { reusable.Clear(); for (int i = 0; i < 1000; i++) reusable.Add(i); }, 100);
    }

    private readonly record struct SortScratch(VoxelTransparentFaceRecord Face, float Depth);
    private sealed class Noop : RenderCommand { public override void Execute(RenderPass pass) { } }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
