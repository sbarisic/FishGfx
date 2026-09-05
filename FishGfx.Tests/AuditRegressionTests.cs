using System.Reflection;
using System.Collections;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using FishGfx.Formats;
using FishGfx.Voxels;
using Xunit;

namespace FishGfx.Tests;

public sealed class AuditRegressionTests
{
    private enum Small : byte { Value = 7 }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
    private struct Packed { public byte Tag; public int Value; }

    [Fact]
    public void InitialAtlasMustFitFallbackAndBatchPublishesOnce()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "data/fonts/Consolas-Regular.ttf");
        Assert.Throws<ArgumentException>(() => new TrueTypeFont(path,
            new TrueTypeFontOptions { BasePixelHeight = 128, InitialAtlasSize = 32, MaximumAtlasSize = 32, PreloadPrintableAscii = false }));
        using TrueTypeFont font = new(path, new TrueTypeFontOptions { PreloadPrintableAscii = false });
        long version = font.MetricsVersion;
        var layout = font.Layout("abcdefgh", 16);
        Assert.Equal(version + 1, font.MetricsVersion);
        foreach (var positioned in layout)
            Assert.Equal(font.GetGlyph(positioned.Glyph.Character).Value.AtlasPosition, positioned.Glyph.AtlasPosition);
    }

    [Fact]
    public void FullGeometryPageDoesNotReportCapacity()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var page = (VoxelGeometryPage)RuntimeHelpers.GetUninitializedObject(typeof(VoxelGeometryPage));
        typeof(VoxelGeometryPage).GetField("sync", flags).SetValue(page, new object());
        FieldInfo ranges = typeof(VoxelGeometryPage).GetField("freeRanges", flags);
        ranges.SetValue(page, Activator.CreateInstance(ranges.FieldType));
        var pool = (VoxelGeometryPagePool)RuntimeHelpers.GetUninitializedObject(typeof(VoxelGeometryPagePool));
        typeof(VoxelGeometryPagePool).GetField("pages", flags).SetValue(pool, new List<VoxelGeometryPage> { page });
        Assert.False(pool.HasCapacityFor(6));
    }

    [Fact]
    public void DisablingCullingActivatesDistantResidentChunksAndInvalidatesTransparency()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        Type type = typeof(VoxelRenderer);
        VoxelRenderer renderer = (VoxelRenderer)RuntimeHelpers.GetUninitializedObject(type);
        foreach (FieldInfo field in type.GetFields(flags))
        {
            if (field.FieldType.IsGenericType && new[] { typeof(List<>), typeof(HashSet<>), typeof(Dictionary<,>) }.Contains(field.FieldType.GetGenericTypeDefinition()))
                field.SetValue(renderer, Activator.CreateInstance(field.FieldType));
        }
        type.GetField("options", flags).SetValue(renderer, new VoxelRendererOptions());
        Type chunkType = type.GetNestedType("GpuChunk", BindingFlags.NonPublic);
        ChunkCoordinate coordinate = new(1000, 0, 1000);
        object chunk = Activator.CreateInstance(chunkType, flags, null, new object[] { coordinate }, null);
        var store = (VoxelTransparentGeometryStore)RuntimeHelpers.GetUninitializedObject(typeof(VoxelTransparentGeometryStore));
        VoxelTransparentAllocation allocation = new(store, 0, 6);
        allocation.SetGeometry(6, new[] { new VoxelTransparentFaceRecord(Vector3.Zero, 0, 6, coordinate, 0) });
        chunkType.GetField("Transparent", flags).SetValue(chunk, allocation);
        ((IDictionary)type.GetField("gpuChunks", flags).GetValue(renderer)).Add(coordinate, chunk);
        MethodInfo refresh = type.GetMethod("RefreshActiveSetIfNeeded", flags);
        renderer.IsCullingEnabled = false;
        refresh.Invoke(renderer, new object[] { Vector3.Zero, 10f });
        Assert.Single((IEnumerable)type.GetField("activeGpuChunks", flags).GetValue(renderer));
        Assert.Equal(1L, type.GetField("transparentActiveSetGeneration", flags).GetValue(renderer));
        renderer.IsCullingEnabled = true;
        refresh.Invoke(renderer, new object[] { Vector3.Zero, 10f });
        Assert.Empty((IEnumerable)type.GetField("activeGpuChunks", flags).GetValue(renderer));
    }

    [Fact]
    public void MeasurementDoesNotPopulateAtlasAndMatchesScalarLayout()
    {
        using TrueTypeFont font = new(Path.Combine(AppContext.BaseDirectory, "data/fonts/Consolas-Regular.ttf"),
            new TrueTypeFontOptions { PreloadPrintableAscii = false });
        int count = font.GlyphCount;
        const string text = "A\U0001F600e\u0301";
        Vector2 measured = font.Measure(text, 16);
        Assert.Equal(count, font.GlyphCount);
        var glyphs = font.Layout(text, 16);
        Assert.Equal(4, glyphs.Length);
        Assert.Equal(new Rune(0x1F600), glyphs[1].Glyph.Character);
        Assert.Equal(measured, font.Measure(text, 16));
        Assert.Equal(measured.X, glyphs[^1].Position.X - glyphs[^1].Glyph.Offset.X * 16 / font.BaseSize + glyphs[^1].Advance, 3);
        const string withControls = "A\rB\tC";
        float[] advances = new float[withControls.Length + 1], leading = new float[withControls.Length + 1];
        font.MeasureAdvances(withControls, 16, 1, advances, leading);
        Assert.Equal(font.Measure(withControls, 16, 1).X, advances[^1], 3);
    }

    [Fact]
    public void BinaryHelpersUseManagedSizesAndRejectTruncatedInput()
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);
        writer.WriteStruct(true);
        writer.WriteStruct(Small.Value);
        writer.WriteStruct(new Packed { Tag = 3, Value = 123456 });
        writer.WriteStruct((42, 19L));
        stream.Position = 0;
        using BinaryReader reader = new(stream);
        Assert.True(reader.ReadStruct<bool>());
        Assert.Equal(Small.Value, reader.ReadStruct<Small>());
        Packed packed = reader.ReadStruct<Packed>();
        Assert.Equal(3, packed.Tag);
        Assert.Equal(123456, packed.Value);
        Assert.Equal((42, 19L), reader.ReadStruct<(int, long)>());
        Assert.Throws<EndOfStreamException>(() => reader.ReadStruct<int>());
    }

    [Fact]
    public void FailedAtlasInsertionPreservesPublishedPositions()
    {
        using TrueTypeFont font = new(Path.Combine(AppContext.BaseDirectory, "data/fonts/Consolas-Regular.ttf"),
            new TrueTypeFontOptions { InitialAtlasSize = 128, MaximumAtlasSize = 128, PreloadPrintableAscii = false });
        FieldInfo version = typeof(TrueTypeFont).GetField("atlasVersion", BindingFlags.NonPublic | BindingFlags.Instance);
        List<char> known = new();
        bool exhausted = false;
        for (char character = 'z'; character >= '!'; character--)
        {
            var positions = known.ToDictionary(c => c, c => font.GetGlyph(c).Value.AtlasPosition);
            int previous = (int)version.GetValue(font);
            font.GetGlyph(character);
            if ((int)version.GetValue(font) == previous)
            {
                exhausted = true;
                foreach (var pair in positions) Assert.Equal(pair.Value, font.GetGlyph(pair.Key).Value.AtlasPosition);
            }
            known.Add(character);
        }
        Assert.True(exhausted);
    }
}
