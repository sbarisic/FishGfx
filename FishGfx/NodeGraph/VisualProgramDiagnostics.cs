using System;
using System.Collections.Generic;
using System.Linq;

namespace FishGfx.NodeGraph;

public enum VisualDiagnosticSeverity
{
	Information,
	Warning,
	Error,
}

public sealed class VisualProgramDiagnostic
{
	public string Code { get; }
	public string Message { get; }
	public VisualDiagnosticSeverity Severity { get; }
	public Guid? FunctionId { get; }
	public Guid? NodeId { get; }
	public string PortName { get; }

	public VisualProgramDiagnostic(
		string code,
		string message,
		VisualDiagnosticSeverity severity,
		Guid? functionId = null,
		Guid? nodeId = null,
		string portName = null
	)
	{
		Code = code;
		Message = message;
		Severity = severity;
		FunctionId = functionId;
		NodeId = nodeId;
		PortName = portName;
	}
}

public sealed class VisualProgramValidationResult
{
	private readonly List<VisualProgramDiagnostic> diagnostics;

	public IReadOnlyList<VisualProgramDiagnostic> Diagnostics { get; }
	public bool Success => diagnostics.All(diagnostic => diagnostic.Severity != VisualDiagnosticSeverity.Error);

	internal VisualProgramValidationResult(List<VisualProgramDiagnostic> diagnostics)
	{
		this.diagnostics = diagnostics;
		Diagnostics = diagnostics.AsReadOnly();
	}
}
