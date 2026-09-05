using System;
using System.Linq;
using System.Numerics;
using FishGfx.Graphics;
using FishGfx.Voxels;
using Xunit;

namespace FishGfx.Tests;

public partial class VoxelTests
{
    [Fact]
    public void GreedyReadyBuffersRetainOnlyRoundedMergedCapacity()
    {
        var (world, palette, opaque, _, _) = CreateWorldAndPalette();
        world.SetChunk(default, Enumerable.Repeat(new VoxelCell(opaque), 4096).ToArray());
        var mesh = VoxelMesher.Build(world.CreateSnapshot(default), palette, new(2, 2, 32, 32),
            new() { CubeMeshingMode = VoxelCubeMeshingMode.GreedyOpaque }, null, true);
        try
        {
            var backing = (VoxelVertex[])typeof(VoxelMeshData).GetField("opaqueVertices", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(mesh)!;
            Assert.Equal(36, mesh.OpaqueVertexCount);
            Assert.InRange(backing.Length, 36, 72);
        }
        finally { mesh.ReleasePooledVertexBuffers(); }
    }

    [Fact]
    public void GreedyLeavesLightingGradientFacesUnchanged()
    {
        VoxelPaletteBuilder builder = new();
        ushort stone = builder.Add(new("stone", VoxelRenderMode.Opaque, new(0)));
        ushort lamp = builder.Add(new("lamp", VoxelRenderMode.Opaque, new(1),
            light: new VoxelMaterialLightSettings(15, new VoxelBlockLight(15, 0, 0))));
        var palette = builder.Build(); VoxelWorld world = new();
        for (int z = 0; z < 16; z++) for (int x = 0; x < 16; x++) world.SetVoxel(x, 0, z, new(stone));
        world.SetVoxel(1, 1, 1, new(lamp));
        using VoxelLighting lighting = new(world, palette);
        lighting.LoadChunk(default, true);
        while (!lighting.IsIdle) lighting.Update(int.MaxValue);
        Assert.True(lighting.TryCreateSnapshot(default, out var light));
        var snapshot = world.CreateSnapshot(default);
        var culled = VoxelMesher.Build(snapshot, palette, new(2, 2, 32, 32), new(), light);
        var greedy = VoxelMesher.Build(snapshot, palette, new(2, 2, 32, 32), new() { CubeMeshingMode = VoxelCubeMeshingMode.GreedyOpaque }, light);
        var vertices = greedy.OpaqueVertices.ToHashSet();
        int gradientFaces = 0;
        for (int i = 0; i < culled.OpaqueVertices.Length; i += 6)
        {
            var face = culled.OpaqueVertices.AsSpan(i, 6);
            if (face.ToArray().All(v => v.PackedLightChannels.Equals(culled.OpaqueVertices[i].PackedLightChannels))) continue;
            gradientFaces++;
            foreach (var vertex in face) Assert.Contains(vertex, vertices);
        }
        Assert.True(gradientFaces > 0);
    }

    [Fact]
    public void LightingDeadlineYieldsAndEventuallyPublishesIdenticalLight()
    {
        var (world, palette, opaque, _, _) = CreateWorldAndPalette();
        world.SetVoxel(1, 1, 1, new(opaque));
        using VoxelLighting bounded = new(world, palette, new() { TimeBudgetMilliseconds = .000001 });
        using VoxelLighting reference = new(world, palette);
        bounded.LoadChunk(default, true); reference.LoadChunk(default, true);
        Assert.InRange(bounded.Update(int.MaxValue), 1, 64);
        Assert.False(bounded.IsIdle);
        for (int i = 0; i < 100000 && !bounded.IsIdle; i++) bounded.Update(int.MaxValue);
        Assert.True(bounded.IsIdle);
        reference.Update(int.MaxValue);
        for (int z = 0; z < 16; z++) for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++)
            Assert.Equal(reference.GetLight(x, y, z), bounded.GetLight(x, y, z));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VoxelLightingOptions { TimeBudgetMilliseconds = double.NaN });
    }

    [Fact]
    public void GreedySolidChunkHasSixTiledFacesAndIdenticalBounds()
    {
        var (world, palette, opaque, _, _) = CreateWorldAndPalette();
        world.SetChunk(new(-1, 0, -1), Enumerable.Repeat(new VoxelCell(opaque), 4096).ToArray());
        var snapshot = world.CreateSnapshot(new(-1, 0, -1));
        var culled = VoxelMesher.Build(snapshot, palette, new(2, 2, 32, 32));
        var greedy = VoxelMesher.Build(snapshot, palette, new(2, 2, 32, 32), new() { CubeMeshingMode = VoxelCubeMeshingMode.GreedyOpaque });
        Assert.Equal(9216, culled.OpaqueVertices.Length);
        Assert.Equal(36, greedy.OpaqueVertices.Length);
        Assert.Equal(culled.Bounds, greedy.Bounds);
        Assert.Equal(16, greedy.OpaqueVertices.Max(v => Math.Max(v.TextureCoordinates.X, v.TextureCoordinates.Y)));
        Assert.Equal(SurfaceArea(culled.OpaqueVertices), SurfaceArea(greedy.OpaqueVertices));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void GreedyPreservesMixedSurfaceCoverageAndTransparentGeometry(int pattern)
    {
        var (world, palette, opaque, cutout, transparent) = CreateWorldAndPalette();
        Random random = new(714);
        for (int z = 0; z < 16; z++) for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++)
        {
            ushort material = pattern == 0 ? ((x + y + z) % 2 == 0 ? opaque : (ushort)0)
                : pattern == 1 ? (y < 4 ? opaque : (ushort)0)
                : new ushort[] { 0, opaque, cutout, transparent }[random.Next(4)];
            world.SetVoxel(x, y, z, new(material));
        }
        var snapshot = world.CreateSnapshot(default);
        var culled = VoxelMesher.Build(snapshot, palette, new(2, 2, 32, 32));
        var greedy = VoxelMesher.Build(snapshot, palette, new(2, 2, 32, 32), new() { CubeMeshingMode = VoxelCubeMeshingMode.GreedyOpaque });
        Assert.Equal(SurfaceArea(culled.OpaqueVertices), SurfaceArea(greedy.OpaqueVertices));
        Assert.Equal(culled.CutoutVertices, greedy.CutoutVertices);
        Assert.Equal(culled.TransparentFaces.SelectMany(f => f.Vertices), greedy.TransparentFaces.SelectMany(f => f.Vertices));
        Assert.True(greedy.OpaqueVertices.Length <= culled.OpaqueVertices.Length);
    }

    [Fact]
    public void DirtyIndexRetainsDistantWorkAndRemovesEmptyColumns()
    {
        DirtyChunkIndex index = new();
        index.Add(new(0, 0, 0)); index.Add(new(100, 0, 100)); index.Add(new(100, 1, 100));
        Camera camera = new() { Position = Vector3.Zero };
        Assert.Single(index.Candidates(new VoxelMeshingFocus(camera, 32, 0, true)));
        Assert.Equal(3, index.Candidates(new VoxelMeshingFocus(camera, 32, 0, false)).Count());
        index.Remove(new(100, 0, 100)); index.Remove(new(100, 1, 100));
        Assert.Equal(1, index.Count);
    }

    private static double SurfaceArea(VoxelVertex[] vertices)
    {
        double area = 0;
        for (int i = 0; i < vertices.Length; i += 3)
            area += Vector3.Cross(vertices[i + 1].Position - vertices[i].Position, vertices[i + 2].Position - vertices[i].Position).Length() / 2;
        return area;
    }
}
