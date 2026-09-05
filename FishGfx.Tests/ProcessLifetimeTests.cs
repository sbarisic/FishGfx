using System.Diagnostics;
using System.Reflection;
using FishGfx.NodeEditor;
using FishGfx.NodeGraph;
using Xunit;

namespace FishGfx.Tests;

public sealed class ProcessLifetimeTests
{
    [Fact]
    public async Task BuildCancellationIsReported()
    {
        using VisualEditorSession editor = new();
        using CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(200));
        DotNetProgramRunResult result = await new DotNetProgramRunner().BuildAndRunAsync(editor.Generation,
            cancellationToken: cancellation.Token);
        Assert.True(result.Cancelled);
        Assert.False(result.Success);
    }

    [Fact]
    public void EditorShutdownAwaitsItsExecutionTask()
    {
        using VisualEditorSession editor = new();
        editor.Run();
        Task execution = (Task)typeof(VisualEditorSession).GetField("execution", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(editor);
        Assert.NotNull(execution);
        editor.Dispose();
        Assert.True(execution.IsCompleted);
        Assert.False(editor.IsRunning);
    }

    [Fact]
    public async Task CancellationTerminatesOwnedDescendants()
    {
        using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(3));
        var result = await DotNetProgramRunner.RunProcessAsync("powershell.exe",
            new[] { "-NoProfile", "-NonInteractive", "-Command", "$child = Start-Process powershell.exe -WindowStyle Hidden -PassThru -ArgumentList '-NoProfile -NonInteractive -Command Start-Sleep -Seconds 60'; [Console]::WriteLine($child.Id); [Console]::Out.Flush(); Start-Sleep -Seconds 60" },
            Path.GetTempPath(), null, cancellation.Token);
        Assert.True(result.Cancelled);
        Assert.True(int.TryParse(result.Output.Trim(), out int pid), result.Output);
        try { using Process child = Process.GetProcessById(pid); Assert.True(child.HasExited); }
        catch (ArgumentException) { /* Descendant was reaped. */ }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationReapsChildIncludingBlockedStdin(bool blockedInput)
    {
        using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(2));
        var result = await DotNetProgramRunner.RunProcessAsync("powershell.exe",
            new[] { "-NoProfile", "-NonInteractive", "-Command", "[Console]::WriteLine($PID); [Console]::Out.Flush(); Start-Sleep -Seconds 60" },
            Path.GetTempPath(), blockedInput ? new string('x', 8 * 1024 * 1024) : null, cancellation.Token);
        Assert.True(result.Cancelled);
        Assert.True(int.TryParse(result.Output.Trim(), out int pid), result.Output);
        try { using Process child = Process.GetProcessById(pid); Assert.True(child.HasExited); }
        catch (ArgumentException) { /* PID has already been reaped. */ }
    }

    [Fact]
    public async Task ExcessOutputIsDrainedAndMarkedTruncated()
    {
        var result = await DotNetProgramRunner.RunProcessAsync("powershell.exe",
            new[] { "-NoProfile", "-NonInteractive", "-Command", "[Console]::Write(('x' * 2097152)); [Console]::Error.Write(('y' * 2097152))" },
            Path.GetTempPath(), null, TestContext.Current.CancellationToken);
        Assert.Equal(0, result.ExitCode);
        Assert.True(result.OutputTruncated);
        Assert.True(result.ErrorTruncated);
        Assert.Equal(1024 * 1024, result.Output.Length);
        Assert.Equal(1024 * 1024, result.Error.Length);
    }
}
