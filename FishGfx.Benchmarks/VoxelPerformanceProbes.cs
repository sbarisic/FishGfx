using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using FishGfx;
using FishGfx.Graphics;
using FishGfx.Voxels;

internal static class VoxelPerformanceProbes
{
    internal static void Run(Action<string, Action, int> measure)
    {
        VoxelPaletteBuilder builder = new();
        ushort stone = builder.Add(new VoxelMaterial("Stone", VoxelRenderMode.Opaque, new(0)));
        var palette = builder.Build();
        using (VoxelMeshingScheduler scheduler = new(new VoxelWorld(), palette, new(1, 1, 16, 16), maxWorkers: 1))
        {
            for (int x = 32; x < 160; x++) for (int z = 32; z < 160; z++) for (int y = 0; y < 4; y++) scheduler.MarkDirty(new(x, y, z));
            Camera camera = new(); camera.SetPerspective(1920, 1080);
            var focus = new VoxelMeshingFocus(camera, 160, 32, true);
            measure("schedule-deferred-65536", () => { if (scheduler.SchedulePending(focus, 1) != 0) throw new InvalidOperationException("Scheduled deferred work"); }, 10);
        }
        foreach (Type commandType in new[] { typeof(DrawVoxelPagesCommand), typeof(DrawVoxelShadowPagesCommand) })
        {
            var pages = Enumerable.Range(0, 128).Select(_ => (VoxelGeometryPage)RuntimeHelpers.GetUninitializedObject(typeof(VoxelGeometryPage))).ToArray();
            var entries = Enumerable.Range(0, 4096).Select(i => new VoxelPassEntry(new(pages[i % pages.Length], i * 6, 6, 6, 0), new(i, 0, 0), i)).ToArray();
            var create = commandType.GetMethod("CreateGroups", BindingFlags.Static | BindingFlags.NonPublic)!;
            var release = commandType.GetMethods(BindingFlags.Static | BindingFlags.NonPublic).Single(m => m.Name == "ReleaseGroups" && m.GetParameters()[0].ParameterType == create.ReturnType);
            object[] input = { entries }, output = new object[1];
            measure($"group-{commandType.Name}-4096", () => { output[0] = create.Invoke(null, input); release.Invoke(null, output); output[0] = null; }, 10);
        }
        foreach (string shape in new[] { "solid", "slab", "checker" })
        {
            VoxelWorld world = new(); VoxelCell[] cells = new VoxelCell[4096];
            for (int z = 0; z < 16; z++) for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++)
                if (shape == "solid" || shape == "slab" && y < 4 || shape == "checker" && (x + y + z) % 2 == 0)
                    cells[x + 16 * (y + 16 * z)] = new(stone);
            world.SetChunk(default, cells); var snapshot = world.CreateSnapshot(default);
            foreach (string mode in new[] { "Culled", "GreedyOpaque" })
            {
                VoxelMeshingOptions options = new();
                // Reflection keeps this same benchmark source usable against the original baseline.
                var property = typeof(VoxelMeshingOptions).GetProperty("CubeMeshingMode");
                if (mode != "Culled" && property == null) continue;
                property?.SetValue(options, Enum.Parse(property.PropertyType, mode));
                measure($"mesh-{shape}-{mode}", () => {
                    var mesh = VoxelMesher.Build(snapshot, palette, new(1, 1, 16, 16), options, null, true);
                    mesh.ReleasePooledVertexBuffers();
                }, 10);
            }
        }

        foreach (int total in new[] { 10000, 100000 })
        foreach (int visiblePercent in new[] { 0, 1, 10, 100 })
        {
            var store = (VoxelTransparentGeometryStore)RuntimeHelpers.GetUninitializedObject(typeof(VoxelTransparentGeometryStore));
            var chunks = new VoxelTransparentOrderingChunk[100];
            for (int chunk = 0; chunk < chunks.Length; chunk++)
            {
                bool visible = chunk < visiblePercent;
                Vector3 position = new(visible ? 0 : 100000, 0, -20);
                var faces = Enumerable.Range(0, total / 100).Select(i => new VoxelTransparentFaceRecord(position + new Vector3(0, 0, -i * .001f), (uint)((chunk * (total / 100) + i) * 6), 6, new(chunk, 0, 0), i)).ToArray();
                VoxelTransparentAllocation allocation = new(store, 0, faces.Length * 6);
                allocation.SetGeometry(faces.Length * 6, faces);
                chunks[chunk] = new(AxisAlignedBoundingBox.FromPositionAndSize(position - Vector3.One, new(2)), allocation);
            }
            var source = new VoxelTransparentOrderingSource(chunks, 1, 1);
            Camera camera = new() { Position = Vector3.Zero };
            camera.SetPerspective(1920, 1080, 1.2f, .1f, 1000);
            var request = new VoxelTransparentOrderingRequest(source, ViewFrustum.FromCamera(camera), Vector3.Zero, -Vector3.UnitZ, 100, true, VoxelTransparentInvalidationReason.Translation, 1);
            try { measure($"sort-{total}-visible-{visiblePercent}", () => { using var result = VoxelTransparentOrderingScheduler.Build(request); if (result.FaceCount != total * visiblePercent / 100) throw new InvalidOperationException("Visibility mismatch"); }, 5); }
            finally { source.ReleaseOwner(); }
        }
    }
}
