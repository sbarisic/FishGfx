using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FishGfx.NodeGraph;

public sealed class DotNetBuildDiagnostic
{
	public string Code { get; internal set; }
	public string Message { get; internal set; }
	public VisualDiagnosticSeverity Severity { get; internal set; }
	public int Line { get; internal set; }
	public int Column { get; internal set; }
	public Guid? FunctionId { get; internal set; }
	public Guid? NodeId { get; internal set; }
}

public sealed class DotNetProgramBuildResult : IDisposable
{
	private bool disposed;

	public bool Success { get; internal set; }
	public bool Cancelled { get; internal set; }
	public int ExitCode { get; internal set; }
	public string Output { get; internal set; } = "";
	public bool OutputTruncated { get; internal set; }
	public bool ErrorTruncated { get; internal set; }
	public string Error { get; internal set; } = "";
	public string WorkingDirectory { get; internal set; }
	public string AssemblyPath { get; internal set; }
	public IReadOnlyList<DotNetBuildDiagnostic> Diagnostics { get; internal set; } =
		Array.Empty<DotNetBuildDiagnostic>();

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}

		disposed = true;

		if (!string.IsNullOrEmpty(WorkingDirectory))
		{
			try
			{
				Directory.Delete(WorkingDirectory, true);
			}
			catch (IOException)
			{
			}
			catch (UnauthorizedAccessException)
			{
			}
		}
	}
}

public sealed class DotNetProgramRunResult
{
	public bool Success { get; internal set; }
	public bool Cancelled { get; internal set; }
	public int ExitCode { get; internal set; }
	public string Output { get; internal set; } = "";
	public bool OutputTruncated { get; internal set; }
	public bool ErrorTruncated { get; internal set; }
	public string Error { get; internal set; } = "";
	public IReadOnlyList<DotNetBuildDiagnostic> Diagnostics { get; internal set; } =
		Array.Empty<DotNetBuildDiagnostic>();
}

public sealed partial class DotNetProgramRunner
{
	private const string ProjectFileName = "VisualProgram.csproj";
	private const string SourceFileName = "Program.cs";

	[GeneratedRegex(@"Program\.cs\((\d+),(\d+)\):\s+(warning|error)\s+([A-Z]+\d+):\s+(.+?)(?:\s+\[.+\])?$", RegexOptions.IgnoreCase)]
	private static partial Regex DiagnosticPattern();

	public async Task<DotNetProgramBuildResult> BuildAsync(
		CSharpGenerationResult generation,
		CancellationToken cancellationToken = default
	)
	{
		if (generation == null)
		{
			throw new ArgumentNullException(nameof(generation));
		}

		if (!generation.Success)
		{
			throw new InvalidOperationException("Cannot build an invalid generated program.");
		}

		string workingDirectory = CreateWorkingDirectory();
		DotNetProgramBuildResult result = new DotNetProgramBuildResult
		{
			WorkingDirectory = workingDirectory,
		};

		try
		{
			await File.WriteAllTextAsync(
				Path.Combine(workingDirectory, ProjectFileName),
				ProjectFile(),
				cancellationToken
			).ConfigureAwait(false);
			await File.WriteAllTextAsync(
				Path.Combine(workingDirectory, SourceFileName),
				generation.Source,
				cancellationToken
			).ConfigureAwait(false);

			ProcessResult process = await RunProcessAsync(
				"dotnet",
				new[]
				{
					"build",
					ProjectFileName,
					"--nologo",
					"--verbosity",
					"quiet",
				},
				workingDirectory,
				null,
				cancellationToken
			).ConfigureAwait(false);

			result.ExitCode = process.ExitCode;
			result.Cancelled = process.Cancelled;
			result.Output = process.Output;
            result.OutputTruncated = process.OutputTruncated;
            result.ErrorTruncated = process.ErrorTruncated;
			result.Error = process.Error;
			result.Success = !process.Cancelled && process.ExitCode == 0;
			result.AssemblyPath = Path.Combine(
				workingDirectory,
				"bin",
				"Debug",
				"net10.0",
				"VisualProgram.dll"
			);
			result.Diagnostics = ParseDiagnostics(
				process.Output + Environment.NewLine + process.Error,
				generation.SourceMap
			);

			return result;
		}
		catch
		{
			result.Dispose();
			throw;
		}
	}

	public async Task<DotNetProgramRunResult> BuildAndRunAsync(
		CSharpGenerationResult generation,
		string standardInput = null,
		CancellationToken cancellationToken = default
	)
	{
		try
		{
			using DotNetProgramBuildResult build = await BuildAsync(generation, cancellationToken).ConfigureAwait(false);

			if (!build.Success)
			{
				return new DotNetProgramRunResult
				{
					Success = false,
					Cancelled = build.Cancelled,
					ExitCode = build.ExitCode,
					Output = build.Output,
                    OutputTruncated = build.OutputTruncated,
                    ErrorTruncated = build.ErrorTruncated,
					Error = build.Error,
					Diagnostics = build.Diagnostics,
				};
			}

			ProcessResult process = await RunProcessAsync(
				"dotnet",
				new[]
				{
					build.AssemblyPath,
				},
				build.WorkingDirectory,
				standardInput,
				cancellationToken
			).ConfigureAwait(false);

			return new DotNetProgramRunResult
			{
				Success = !process.Cancelled && process.ExitCode == 0,
				Cancelled = process.Cancelled,
				ExitCode = process.ExitCode,
				Output = process.Output,
                OutputTruncated = process.OutputTruncated,
                ErrorTruncated = process.ErrorTruncated,
				Error = process.Error,
				Diagnostics = build.Diagnostics,
			};
		}
		catch (OperationCanceledException)
		{
			return new DotNetProgramRunResult
			{
				Cancelled = true,
				ExitCode = -1,
				Error = "Execution cancelled.",
			};
		}
		catch (Exception exception) when (
			exception is IOException
			|| exception is UnauthorizedAccessException
			|| exception is InvalidOperationException
		)
		{
			return new DotNetProgramRunResult
			{
				ExitCode = -1,
				Error = exception.Message,
			};
		}
	}

	internal static async Task<ProcessResult> RunProcessAsync(
		string fileName,
		IEnumerable<string> arguments,
		string workingDirectory,
		string standardInput,
		CancellationToken cancellationToken
	)
	{
		ProcessStartInfo startInfo = new ProcessStartInfo
		{
			FileName = fileName,
			WorkingDirectory = workingDirectory,
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			RedirectStandardInput = true,
		};

		foreach (string argument in arguments)
		{
			startInfo.ArgumentList.Add(argument);
		}

		using Process process = new Process
		{
			StartInfo = startInfo,
			EnableRaisingEvents = true,
		};

		try
		{
			if (!process.Start())
			{
				throw new InvalidOperationException($"Could not start '{fileName}'.");
			}
		}
		catch (System.ComponentModel.Win32Exception exception)
		{
			throw new InvalidOperationException(
				"The .NET SDK could not be started. Install .NET 10 SDK and ensure dotnet is on PATH.",
				exception
			);
		}

        Task<CapturedOutput> output = CaptureAsync(process.StandardOutput.BaseStream);
        Task<CapturedOutput> error = CaptureAsync(process.StandardError.BaseStream);
        bool cancelled = false;
        try
        {
            if (standardInput != null)
                await process.StandardInput.WriteAsync(standardInput.AsMemory(), cancellationToken).ConfigureAwait(false);
            process.StandardInput.Close();
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancelled = true;
        }
        finally
        {
            // Covers stdin failures as well as cancellation while waiting for exit.
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) when (process.HasExited) { }
            }
            await process.WaitForExitAsync().ConfigureAwait(false);
            await Task.WhenAll(output, error).ConfigureAwait(false);
        }
        CapturedOutput stdout = await output.ConfigureAwait(false);
        CapturedOutput stderr = await error.ConfigureAwait(false);
        return new ProcessResult(cancelled ? -1 : process.ExitCode, stdout.Text, stderr.Text, cancelled,
            stdout.Truncated, stderr.Truncated);
    }

    private readonly record struct CapturedOutput(string Text, bool Truncated);

    private static async Task<CapturedOutput> CaptureAsync(Stream stream)
    {
        const int maximumBytes = 1024 * 1024;
        byte[] buffer = new byte[8192];
        using MemoryStream retained = new();
        bool truncated = false;
        int count;
        while ((count = await stream.ReadAsync(buffer).ConfigureAwait(false)) != 0)
        {
            int accepted = Math.Min(count, maximumBytes - (int)retained.Length);
            retained.Write(buffer, 0, accepted);
            truncated |= accepted != count;
        }
        return new CapturedOutput(Encoding.UTF8.GetString(retained.GetBuffer(), 0, (int)retained.Length), truncated);
    }

	private static IReadOnlyList<DotNetBuildDiagnostic> ParseDiagnostics(
		string text,
		GeneratedSourceMap sourceMap
	)
	{
		List<DotNetBuildDiagnostic> diagnostics = new List<DotNetBuildDiagnostic>();
		HashSet<(string Code, int Line, int Column, string Message)> seen =
			new HashSet<(string Code, int Line, int Column, string Message)>();

		foreach (string lineText in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
		{
			Match match = DiagnosticPattern().Match(lineText);

			if (!match.Success)
			{
				continue;
			}

			int line = int.Parse(match.Groups[1].Value);
			int column = int.Parse(match.Groups[2].Value);
			string code = match.Groups[4].Value;
			string message = match.Groups[5].Value.Trim();

			if (!seen.Add((code, line, column, message)))
			{
				continue;
			}

			GeneratedNodeSpan span = sourceMap.Find(line, column);

			diagnostics.Add(
				new DotNetBuildDiagnostic
				{
					Code = code,
					Message = message,
					Severity = string.Equals(match.Groups[3].Value, "error", StringComparison.OrdinalIgnoreCase)
						? VisualDiagnosticSeverity.Error
						: VisualDiagnosticSeverity.Warning,
					Line = line,
					Column = column,
					FunctionId = span?.FunctionId,
					NodeId = span?.NodeId,
				}
			);
		}

		return diagnostics;
	}

	private static string CreateWorkingDirectory()
	{
		string root = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			"FishGfx",
			"NodeEditor",
			"build"
		);
		string path = Path.Combine(root, Guid.NewGuid().ToString("N"));

		Directory.CreateDirectory(path);

		return path;
	}

	private static string ProjectFile()
	{
		return """
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup>
			    <OutputType>Exe</OutputType>
			    <TargetFramework>net10.0</TargetFramework>
			    <AssemblyName>VisualProgram</AssemblyName>
			    <Nullable>disable</Nullable>
			    <ImplicitUsings>disable</ImplicitUsings>
			  </PropertyGroup>
			</Project>
			""";
	}

	internal readonly struct ProcessResult
	{
		internal int ExitCode { get; }
		internal string Output { get; }
		internal string Error { get; }
		internal bool Cancelled { get; }
        internal bool OutputTruncated { get; }
        internal bool ErrorTruncated { get; }

		internal ProcessResult(int exitCode, string output, string error, bool cancelled, bool outputTruncated = false, bool errorTruncated = false)
		{
			ExitCode = exitCode;
			Output = output;
			Error = error;
			Cancelled = cancelled;
            OutputTruncated = outputTruncated;
            ErrorTruncated = errorTruncated;
		}
	}
}
