using System.Text;

namespace FishGfx.ManifoldCad;

internal sealed class ApplicationLog : IDisposable
{
	private const string FileName = "FishGfx.ManifoldCad.log";
	private static readonly object fileLock = new();
	private readonly TextWriter originalOut;
	private readonly TextWriter originalError;
	private readonly CapturingTextWriter capturedOut;
	private readonly CapturingTextWriter capturedError;
	private readonly string previousNativeLogPath;
	private bool disposed;

	private ApplicationLog(string path)
	{
		Path = path;
		Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
		File.WriteAllText(path, string.Empty, Encoding.UTF8);
		originalOut = Console.Out;
		originalError = Console.Error;
		capturedOut = new CapturingTextWriter(originalOut, "stdout", AppendRecord);
		capturedError = new CapturingTextWriter(originalError, "stderr", AppendRecord);
		Console.SetOut(capturedOut);
		Console.SetError(capturedError);
		previousNativeLogPath = Environment.GetEnvironmentVariable("FGCAD_LOG_PATH");
		Environment.SetEnvironmentVariable("FGCAD_LOG_PATH", path);
		Current = this;
		AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
		TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
		AppendRecord(
			"startup",
			$"FishGfx Parametric Manifold CAD started. "
				+ $"PID={Environment.ProcessId}; "
				+ $"runtime={Environment.Version}; "
				+ $"OS={Environment.OSVersion}; "
				+ $"base={AppContext.BaseDirectory}; "
				+ $"cwd={Environment.CurrentDirectory}"
		);
	}

	internal string Path { get; }

	internal static ApplicationLog Current { get; private set; }

	internal static ApplicationLog Start()
	{
		string configured = Environment.GetEnvironmentVariable("FISHGFX_MANIFOLD_LOG");
		string path = string.IsNullOrWhiteSpace(configured)
			? System.IO.Path.Combine(Environment.CurrentDirectory, FileName)
			: System.IO.Path.GetFullPath(configured);
		return new ApplicationLog(System.IO.Path.GetFullPath(path));
	}

	internal void Info(string message)
	{
		AppendRecord("info", message);
	}

	internal void Error(string message)
	{
		AppendRecord("error", message);
	}

	internal void Exception(string context, Exception exception)
	{
		AppendRecord("exception", $"{context}{Environment.NewLine}{exception}");
	}

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}

		disposed = true;
		AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
		TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
		capturedOut.FlushPending();
		capturedError.FlushPending();
		AppendRecord("shutdown", "FishGfx Parametric Manifold CAD stopped.");
		Console.SetOut(originalOut);
		Console.SetError(originalError);
		Environment.SetEnvironmentVariable("FGCAD_LOG_PATH", previousNativeLogPath);
		if (ReferenceEquals(Current, this))
		{
			Current = null;
		}
	}

	private void OnUnhandledException(object sender, UnhandledExceptionEventArgs args)
	{
		AppendRecord(
			"fatal",
			$"Unhandled exception; terminating={args.IsTerminating}{Environment.NewLine}{args.ExceptionObject}"
		);
	}

	private void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs args)
	{
		AppendRecord("task", $"Unobserved task exception{Environment.NewLine}{args.Exception}");
	}

	private void AppendRecord(string source, string message)
	{
		if (string.IsNullOrEmpty(message))
		{
			return;
		}

		string record = $"{DateTimeOffset.Now:O} [{source}] {message}{Environment.NewLine}";
		try
		{
			lock (fileLock)
			{
				File.AppendAllText(Path, record, Encoding.UTF8);
			}
		}
		catch
		{
			// Logging must never turn a recoverable CAD error into an application failure.
		}
	}

	private sealed class CapturingTextWriter : TextWriter
	{
		private readonly TextWriter original;
		private readonly string source;
		private readonly Action<string, string> append;
		private readonly StringBuilder pending = new();

		internal CapturingTextWriter(
			TextWriter original,
			string source,
			Action<string, string> append
		)
		{
			this.original = original;
			this.source = source;
			this.append = append;
		}

		public override Encoding Encoding => original.Encoding;

		public override void Write(char value)
		{
			original.Write(value);
			Capture(value);
		}

		public override void Write(string value)
		{
			original.Write(value);
			if (value == null)
			{
				return;
			}

			lock (pending)
			{
				foreach (char character in value)
				{
					CaptureLocked(character);
				}
			}
		}

		public override void WriteLine(string value)
		{
			original.WriteLine(value);
			lock (pending)
			{
				if (value != null)
				{
					pending.Append(value);
				}
				FlushPendingLocked();
			}
		}

		public override void Flush()
		{
			original.Flush();
			FlushPending();
		}

		internal void FlushPending()
		{
			lock (pending)
			{
				FlushPendingLocked();
			}
		}

		private void Capture(char value)
		{
			lock (pending)
			{
				CaptureLocked(value);
			}
		}

		private void CaptureLocked(char value)
		{
			if (value == '\n')
			{
				FlushPendingLocked();
			}
			else if (value != '\r')
			{
				pending.Append(value);
			}
		}

		private void FlushPendingLocked()
		{
			if (pending.Length == 0)
			{
				return;
			}

			append(source, pending.ToString());
			pending.Clear();
		}
	}
}
