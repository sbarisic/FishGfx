using System.Globalization;
using FishGfx.Cad;

namespace FishGfx.ManifoldCad;

internal sealed class BezierDraftState
{
	private BezierDraftState(Guid runnerId, RunnerNode node, CadFrame entryFrame, CadFrame authoritativeExitFrame)
	{
		RunnerId = runnerId;
		NodeId = node.Id;
		EntryFrame = entryFrame;
		AuthoritativeExitFrame = authoritativeExitFrame;
		StartHandleLength = Read(node, "startHandleLength");
		Control2Local = new CadPoint3(
			Read(node, "control2T"),
			Read(node, "control2U"),
			Read(node, "control2V")
		);
		EndLocal = new CadPoint3(
			Read(node, "endT"),
			Read(node, "endU"),
			Read(node, "endV")
		);
	}

	internal Guid RunnerId { get; }
	internal Guid NodeId { get; }
	internal CadFrame EntryFrame { get; private set; }
	internal CadFrame AuthoritativeExitFrame { get; private set; }
	internal double StartHandleLength { get; private set; }
	internal CadPoint3 Control2Local { get; private set; }
	internal CadPoint3 EndLocal { get; private set; }
	internal bool IsDirty { get; private set; }
	internal bool IsInvalid { get; set; }

	internal CadPoint3 Start => EntryFrame.Origin;
	internal CadPoint3 Control1 => Start + EntryFrame.Tangent * StartHandleLength;
	internal CadPoint3 Control2 => ToWorld(Control2Local);
	internal CadPoint3 End => ToWorld(EndLocal);

	internal static BezierDraftState Create(Guid runnerId, RunnerNode node, RunnerFeature feature)
	{
		ArgumentNullException.ThrowIfNull(node);
		if (node.DefinitionId != RunnerNodes.CubicBezier)
		{
			throw new ArgumentException("A Bezier draft requires a Cubic Bezier node.", nameof(node));
		}
		return new BezierDraftState(runnerId, node, feature.EntryFrame, feature.ExitFrame);
	}

	internal CadPoint3 Point(RunnerPathPointKind kind)
	{
		return kind switch
		{
			RunnerPathPointKind.Start => Start,
			RunnerPathPointKind.Control1 => Control1,
			RunnerPathPointKind.Control2 => Control2,
			RunnerPathPointKind.End => End,
			_ => throw new ArgumentOutOfRangeException(nameof(kind)),
		};
	}

	internal bool MoveWorldPoint(RunnerPathPointKind kind, CadPoint3 worldPoint)
	{
		switch (kind)
		{
			case RunnerPathPointKind.Control1:
				double handleLength = Math.Max(
					CadPoint3.Dot(worldPoint - Start, EntryFrame.Tangent),
					1.0e-6
				);
				if (NearlyEqual(handleLength, StartHandleLength))
				{
					return false;
				}
				StartHandleLength = handleLength;
				break;
			case RunnerPathPointKind.Control2:
				CadPoint3 control2 = ToLocal(worldPoint);
				if (NearlyEqual(control2, Control2Local))
				{
					return false;
				}
				Control2Local = control2;
				break;
			case RunnerPathPointKind.End:
				CadPoint3 end = ToLocal(worldPoint);
				if (NearlyEqual(end, EndLocal))
				{
					return false;
				}
				EndLocal = end;
				break;
			default:
				return false;
		}
		IsDirty = true;
		IsInvalid = false;
		return true;
	}

	internal void UpdateFrames(CadFrame entryFrame, CadFrame authoritativeExitFrame)
	{
		EntryFrame = entryFrame;
		AuthoritativeExitFrame = authoritativeExitFrame;
	}

	internal void ReloadCommittedProperties(RunnerNode node)
	{
		StartHandleLength = Read(node, "startHandleLength");
		Control2Local = new CadPoint3(
			Read(node, "control2T"),
			Read(node, "control2U"),
			Read(node, "control2V")
		);
		EndLocal = new CadPoint3(
			Read(node, "endT"),
			Read(node, "endU"),
			Read(node, "endV")
		);
		IsDirty = false;
		IsInvalid = false;
	}

	internal void Commit(RunnerNode node)
	{
		if (node.Id != NodeId || node.DefinitionId != RunnerNodes.CubicBezier)
		{
			throw new InvalidOperationException("The Bezier draft no longer matches its graph node.");
		}
		Dictionary<string, string> staged = new(node.Properties)
		{
			["startHandleLength"] = Format(StartHandleLength),
			["control2T"] = Format(Control2Local.X),
			["control2U"] = Format(Control2Local.Y),
			["control2V"] = Format(Control2Local.Z),
			["endT"] = Format(EndLocal.X),
			["endU"] = Format(EndLocal.Y),
			["endV"] = Format(EndLocal.Z),
		};
		node.Properties.Clear();
		foreach ((string key, string value) in staged)
		{
			node.Properties[key] = value;
		}
		IsDirty = false;
	}

	internal CadPoint3 Sample(double parameter)
	{
		double inverse = 1 - parameter;
		return Start * (inverse * inverse * inverse)
			+ Control1 * (3 * inverse * inverse * parameter)
			+ Control2 * (3 * inverse * parameter * parameter)
			+ End * (parameter * parameter * parameter);
	}

	private CadPoint3 ToWorld(CadPoint3 local)
	{
		return Start
			+ EntryFrame.Tangent * local.X
			+ EntryFrame.Normal * local.Y
			+ EntryFrame.Binormal * local.Z;
	}

	private CadPoint3 ToLocal(CadPoint3 world)
	{
		CadPoint3 relative = world - Start;
		return new CadPoint3(
			CadPoint3.Dot(relative, EntryFrame.Tangent),
			CadPoint3.Dot(relative, EntryFrame.Normal),
			CadPoint3.Dot(relative, EntryFrame.Binormal)
		);
	}

	private static double Read(RunnerNode node, string name)
	{
		return double.Parse(node.Properties[name], NumberStyles.Float, CultureInfo.InvariantCulture);
	}

	private static string Format(double value)
	{
		return value.ToString("G17", CultureInfo.InvariantCulture);
	}

	private static bool NearlyEqual(double left, double right)
	{
		double scale = Math.Max(1, Math.Max(Math.Abs(left), Math.Abs(right)));
		return Math.Abs(left - right) <= scale * 1.0e-12;
	}

	private static bool NearlyEqual(CadPoint3 left, CadPoint3 right)
	{
		double scale = Math.Max(1, Math.Max(left.Length, right.Length));
		return (left - right).Length <= scale * 1.0e-12;
	}
}
