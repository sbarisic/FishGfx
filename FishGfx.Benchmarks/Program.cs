using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using FishGfx.Formats;
using FishUI.Controls;
using UnitTest;

string filter = args.FirstOrDefault() ?? "";
if (filter == "verify-graphics") { GraphicsProbes.Verify(); return; }
if (filter.Contains("geometry")) { GraphicsProbes.Geometry(Run); return; }
GraphicsProbes.CpuExperiments(Run);
string fontPath = Path.Combine(AppContext.BaseDirectory, "data", "fonts", "Consolas-Regular.ttf");
if (!File.Exists(fontPath)) fontPath = Path.GetFullPath("thirdparty/FishGfx/FishGfx/data/fonts/Consolas-Regular.ttf");
if (filter.Contains("context", StringComparison.OrdinalIgnoreCase))
{
    using FishGfx.Graphics.RenderWindow first = new(new FishGfx.Graphics.RenderWindowOptions { Width = 320, Height = 180, Title = "FishGfx context benchmark" });
    using FishGfx.Graphics.RenderWindow second = new(new FishGfx.Graphics.RenderWindowOptions { Width = 320, Height = 180, Title = "FishGfx context benchmark" });
    Run("context-same", () => first.Graphics.MakeCurrent(), 100);
    Run("context-alternate", () => { first.Graphics.MakeCurrent(); second.Graphics.MakeCurrent(); }, 100);
    return;
}
using TrueTypeFont font = new(fontPath);
string text = new('A', 1024);
Run("measure-1024", () => font.Measure(text, 16), 100);
Run("glyph-batch-96", () =>
{
    using TrueTypeFont batch = new(fontPath, new TrueTypeFontOptions { PreloadPrintableAscii = false });
    batch.Layout(new string(Enumerable.Range(33, 96).Select(value => (char)value).ToArray()), 16);
}, 1);
using FishUITestFixture fixture = new();
MultiLineEditbox editor = new() { Size = new(20000, 600), WordWrap = true };
fixture.UI.AddControl(editor);
Run("wrap-2048", () =>
{
    editor.Text = editor.Text.Length == 2048 ? new string('B', 2049) : new string('A', 2048);
    fixture.UI.TickUpdate(.016f, 1);
}, 1);

void Run(string name, Action action, int repetitions)
{
    if (!name.Contains(filter, StringComparison.OrdinalIgnoreCase)) return;
    for (int i = 0; i < 5; i++) action();
    double[] timings = new double[30];
    long allocated = 0;
    for (int sample = 0; sample < timings.Length; sample++)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        long started = Stopwatch.GetTimestamp();
        for (int i = 0; i < repetitions; i++) action();
        timings[sample] = Stopwatch.GetElapsedTime(started).TotalMilliseconds / repetitions;
        allocated += GC.GetAllocatedBytesForCurrentThread() - before;
    }
    Array.Sort(timings);
    Console.WriteLine(JsonSerializer.Serialize(new { name, medianMs = timings[15], p95Ms = timings[28], p99Ms = timings[29], bytesPerOperation = allocated / (30.0 * repetitions) }));
}
