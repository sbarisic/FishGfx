namespace FishGfx.Cad;

public sealed class CadCollectorBranchSpan
{
	public CadPoint3 Control1Local { get; set; }

	public CadPoint3 Control2Local { get; set; }

	public CadPoint3 EndLocal { get; set; }

	internal CadCollectorBranchSpan DeepClone()
	{
		return new CadCollectorBranchSpan
		{
			Control1Local = Control1Local,
			Control2Local = Control2Local,
			EndLocal = EndLocal,
		};
	}
}

public sealed class CadCollectorBranchPath
{
	public const int CurrentSolverVersion = 1;

	public int SolverVersion { get; set; } = CurrentSolverVersion;

	public double OuterRadiusMillimetres { get; set; } = 21.2;

	public bool IsFeasible { get; set; }

	public double MinimumRadiusMillimetres { get; set; }

	public string Diagnostic { get; set; }

	public List<CadCollectorBranchSpan> Spans { get; set; } = new();

	internal CadCollectorBranchPath DeepClone()
	{
		return new CadCollectorBranchPath
		{
			SolverVersion = SolverVersion,
			OuterRadiusMillimetres = OuterRadiusMillimetres,
			IsFeasible = IsFeasible,
			MinimumRadiusMillimetres = MinimumRadiusMillimetres,
			Diagnostic = Diagnostic,
			Spans = Spans?.Select(span => span.DeepClone()).ToList() ?? new(),
		};
	}
}

public readonly record struct CadCollectorWorldBranchSpan(
	CadPoint3 Start,
	CadPoint3 Control1,
	CadPoint3 Control2,
	CadPoint3 End
);

public static class CadCollectorBranchSolver
{
	private const int CurveSamples = 64;

	public static CadCollectorBranchPath Solve(
		CadFrame inletFrame,
		CadFrame outletFrame,
		double outerRadiusMillimetres,
		double preferredStartHandleLength,
		double preferredEndHandleLength,
		CadCollectorBranchPath previous = null
	)
	{
		if (!double.IsFinite(outerRadiusMillimetres) || outerRadiusMillimetres <= 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(outerRadiusMillimetres),
				"The collector branch outer radius must be finite and positive."
			);
		}
		if (!double.IsFinite(preferredStartHandleLength)
			|| preferredStartHandleLength <= 0
			|| !double.IsFinite(preferredEndHandleLength)
			|| preferredEndHandleLength <= 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(preferredStartHandleLength),
				"Collector branch handle preferences must be finite and positive."
			);
		}

		CadPoint3 chord = outletFrame.Origin - inletFrame.Origin;
		double chordLength = chord.Length;
		if (!double.IsFinite(chordLength) || chordLength <= 1e-6)
		{
			return InvalidFallback(
				inletFrame,
				outletFrame,
				outerRadiusMillimetres,
				preferredStartHandleLength,
				preferredEndHandleLength,
				"The collector inlet and outlet are too close to form a branch."
			);
		}

		double previousStart = PreviousStartHandle(previous, outletFrame, inletFrame);
		double previousEnd = PreviousEndHandle(previous, outletFrame);
		PathCandidate single = SolveSingle(
			inletFrame,
			outletFrame,
			outerRadiusMillimetres,
			preferredStartHandleLength,
			preferredEndHandleLength,
			previousStart,
			previousEnd,
			lockStart: true,
			lockEnd: true
		);
		if (single.IsFeasible)
		{
			return ToPath(single, outletFrame, outerRadiusMillimetres, null);
		}
		PathCandidate adjustedSingle = SolveAdaptiveSingle(
			inletFrame,
			outletFrame,
			outerRadiusMillimetres,
			preferredStartHandleLength,
			preferredEndHandleLength,
			previousStart,
			previousEnd
		);
		if (adjustedSingle.IsFeasible)
		{
			return ToPath(adjustedSingle, outletFrame, outerRadiusMillimetres, null);
		}

		PathCandidate split = SolveSplit(
			inletFrame,
			outletFrame,
			outerRadiusMillimetres,
			preferredStartHandleLength,
			preferredEndHandleLength
		);
		if (split.IsFeasible)
		{
			return ToPath(split, outletFrame, outerRadiusMillimetres, null);
		}

		PathCandidate best = new[] { single, adjustedSingle, split }
			.MaxBy(candidate => candidate.MinimumRadius);
		string diagnostic = $"The bounded one- and two-span solve could not satisfy the "
			+ $"{outerRadiusMillimetres:F2} mm tube radius. Best estimated bend radius: "
			+ $"{Math.Max(0, best.MinimumRadius):F2} mm.";
		return ToPath(best, outletFrame, outerRadiusMillimetres, diagnostic);
	}

	private static PathCandidate SolveAdaptiveSingle(
		CadFrame entry,
		CadFrame target,
		double radius,
		double preferredStart,
		double preferredEnd,
		double previousStart,
		double previousEnd
	)
	{
		double chordLength = (target.Origin - entry.Origin).Length;
		double[] starts = HandleCandidates(chordLength, preferredStart, previousStart);
		double[] ends = HandleCandidates(chordLength, preferredEnd, previousEnd);
		PathCandidate bestFeasible = default;
		PathCandidate bestInvalid = default;
		bool hasFeasible = false;
		bool hasInvalid = false;
		foreach (double start in starts)
		{
			foreach (double end in ends)
			{
				WorldSpan span = new(
					entry.Origin,
					entry.Origin + entry.Tangent * start,
					target.Origin - target.Tangent * end,
					target.Origin
				);
				PathCandidate candidate = Evaluate(
					new[] { span },
					radius,
					20,
					checkSelfIntersection: false
				);
				double stability = Math.Abs(start - previousStart) + Math.Abs(end - previousEnd);
				candidate.Score = candidate.Length
					+ 0.02 * (start + end)
					+ 0.01 * stability;
				if (candidate.IsFeasible
					&& (!hasFeasible || candidate.Score < bestFeasible.Score))
				{
					bestFeasible = candidate;
					hasFeasible = true;
				}
				if (!candidate.IsFeasible
					&& (!hasInvalid || candidate.MinimumRadius > bestInvalid.MinimumRadius))
				{
					bestInvalid = candidate;
					hasInvalid = true;
				}
			}
		}
		if (!hasFeasible)
		{
			return bestInvalid;
		}
		PathCandidate certified = Evaluate(bestFeasible.Spans, radius);
		certified.Score = bestFeasible.Score;
		return certified;
	}

	public static IReadOnlyList<CadCollectorWorldBranchSpan> ToWorldSpans(
		CadCollectorBranchPath path,
		CadFrame outletFrame,
		CadPoint3 start
	)
	{
		ArgumentNullException.ThrowIfNull(path);
		List<CadCollectorWorldBranchSpan> result = new();
		CadPoint3 current = start;
		foreach (CadCollectorBranchSpan span in path.Spans ?? Enumerable.Empty<CadCollectorBranchSpan>())
		{
			CadPoint3 control1 = outletFrame.TransformLocalPoint(span.Control1Local);
			CadPoint3 control2 = outletFrame.TransformLocalPoint(span.Control2Local);
			CadPoint3 end = outletFrame.TransformLocalPoint(span.EndLocal);
			result.Add(new CadCollectorWorldBranchSpan(current, control1, control2, end));
			current = end;
		}
		return result;
	}

	public static CadPoint3[] Sample(
		CadCollectorBranchPath path,
		CadFrame outletFrame,
		CadPoint3 start,
		int samplesPerSpan = 25
	)
	{
		if (path?.Spans == null || path.Spans.Count == 0 || samplesPerSpan < 2)
		{
			return Array.Empty<CadPoint3>();
		}
		IReadOnlyList<CadCollectorWorldBranchSpan> spans = ToWorldSpans(path, outletFrame, start);
		List<CadPoint3> samples = new(spans.Count * (samplesPerSpan - 1) + 1);
		for (int spanIndex = 0; spanIndex < spans.Count; ++spanIndex)
		{
			CadCollectorWorldBranchSpan span = spans[spanIndex];
			for (int sample = spanIndex == 0 ? 0 : 1; sample < samplesPerSpan; ++sample)
			{
				double parameter = sample / (double)(samplesPerSpan - 1);
				samples.Add(Point(span, parameter));
			}
		}
		return samples.ToArray();
	}

	internal static bool ValidatePath(
		CadCollectorBranchPath path,
		CadFrame outletFrame,
		CadFrame inletFrame,
		out string error
	)
	{
		if (path == null
			|| path.SolverVersion != CadCollectorBranchPath.CurrentSolverVersion
			|| !double.IsFinite(path.OuterRadiusMillimetres)
			|| path.OuterRadiusMillimetres <= 0
			|| path.Spans == null
			|| path.Spans.Count is < 1 or > 2)
		{
			error = "A collector inlet requires one or two finite solved Bézier spans.";
			return false;
		}
		IReadOnlyList<CadCollectorWorldBranchSpan> spans = ToWorldSpans(
			path,
			outletFrame,
			inletFrame.Origin
		);
		double tolerance = Math.Max(1e-6, (outletFrame.Origin - inletFrame.Origin).Length * 1e-9);
		for (int index = 0; index < spans.Count; ++index)
		{
			CadCollectorWorldBranchSpan span = spans[index];
			if (!span.Start.IsFinite || !span.Control1.IsFinite
				|| !span.Control2.IsFinite || !span.End.IsFinite
				|| (span.Control1 - span.Start).Length <= tolerance
				|| (span.End - span.Control2).Length <= tolerance)
			{
				error = "A collector branch contains a non-finite or zero-length Bézier handle.";
				return false;
			}
			if (index > 0)
			{
				CadCollectorWorldBranchSpan previous = spans[index - 1];
				CadPoint3 incoming = (previous.End - previous.Control2).Normalized();
				CadPoint3 outgoing = (span.Control1 - span.Start).Normalized();
				if ((previous.End - span.Start).Length > tolerance
					|| CadPoint3.Dot(incoming, outgoing) < 1 - 1e-6)
				{
					error = "Adjacent collector Bézier spans must meet with G1 continuity.";
					return false;
				}
			}
		}
		CadCollectorWorldBranchSpan first = spans[0];
		CadCollectorWorldBranchSpan last = spans[^1];
		if (CadPoint3.Dot(
				(first.Control1 - first.Start).Normalized(),
				inletFrame.Tangent
			) < 1 - 1e-6
			|| (last.End - outletFrame.Origin).Length > tolerance
			|| CadPoint3.Dot(
				(last.End - last.Control2).Normalized(),
				outletFrame.Tangent
			) < 1 - 1e-6)
		{
			error = "A collector branch must match its inlet and outlet position and tangent constraints.";
			return false;
		}
		error = null;
		return true;
	}

	private static CadCollectorBranchPath InvalidFallback(
		CadFrame inlet,
		CadFrame outlet,
		double radius,
		double startHandle,
		double endHandle,
		string diagnostic
	)
	{
		PathCandidate candidate = CreateSingle(
			inlet,
			outlet,
			Math.Max(1, startHandle),
			Math.Max(1, endHandle),
			radius
		);
		return ToPath(candidate, outlet, radius, diagnostic);
	}

	private static PathCandidate SolveSingle(
		CadFrame entry,
		CadFrame target,
		double radius,
		double preferredStart,
		double preferredEnd,
		double previousStart,
		double previousEnd,
		bool lockStart = false,
		bool lockEnd = false
	)
	{
		double chordLength = (target.Origin - entry.Origin).Length;
		double[] starts = lockStart
			? new[] { preferredStart }
			: HandleCandidates(chordLength, preferredStart, previousStart);
		double[] ends = lockEnd
			? new[] { preferredEnd }
			: HandleCandidates(chordLength, preferredEnd, previousEnd);
		PathCandidate bestFeasible = default;
		PathCandidate bestInvalid = default;
		bool hasFeasible = false;
		bool hasInvalid = false;
		foreach (double start in starts)
		{
			foreach (double end in ends)
			{
				PathCandidate candidate = CreateSingle(entry, target, start, end, radius);
				double stability = Math.Abs(start - previousStart) + Math.Abs(end - previousEnd);
				candidate.Score = candidate.Length + 0.02 * (start + end) + 0.01 * stability;
				if (candidate.IsFeasible
					&& (!hasFeasible || candidate.Score < bestFeasible.Score))
				{
					bestFeasible = candidate;
					hasFeasible = true;
				}
				if (!candidate.IsFeasible
					&& (!hasInvalid || candidate.MinimumRadius > bestInvalid.MinimumRadius))
				{
					bestInvalid = candidate;
					hasInvalid = true;
				}
			}
		}
		return hasFeasible ? bestFeasible : bestInvalid;
	}

	private static PathCandidate SolveSplit(
		CadFrame entry,
		CadFrame target,
		double radius,
		double preferredStart,
		double preferredEnd
	)
	{
		CadPoint3 chord = target.Origin - entry.Origin;
		double length = chord.Length;
		CadPoint3 entryGuide = entry.Origin + entry.Tangent * preferredStart;
		CadPoint3 targetGuide = target.Origin - target.Tangent * preferredEnd;
		CadPoint3 guideVector = targetGuide - entryGuide;
		CadPoint3 guideDirection = guideVector.LengthSquared > 1e-12
			? guideVector.Normalized()
			: chord.Normalized();
		List<CadPoint3> detourDirections = new();
		AddPerpendicularDirection(detourDirections, entry.Normal, guideDirection);
		AddPerpendicularDirection(detourDirections, entry.Binormal, guideDirection);
		AddPerpendicularDirection(detourDirections, target.Normal, guideDirection);
		AddPerpendicularDirection(detourDirections, target.Binormal, guideDirection);
		AddPerpendicularDirection(
			detourDirections,
			CadPoint3.Cross(entry.Tangent, target.Tangent),
			guideDirection
		);
		if (detourDirections.Count == 0)
		{
			AddPerpendicularDirection(detourDirections, entry.Normal, guideDirection);
		}

		PathCandidate bestInvalid = default;
		bool hasInvalid = false;
		double[] fractions = { 0.35, 0.5, 0.65 };
		double[] detourScales = { 0, 0.3, 0.65, 1.1, 1.75, 2.5, 3.5 };
		foreach (double detourScale in detourScales)
		{
			PathCandidate bestLayer = default;
			bool hasLayerFeasible = false;
			foreach (double fraction in fractions)
			{
				CadPoint3 baseJoint = CadPoint3.Lerp(entryGuide, targetGuide, fraction);
				IEnumerable<CadPoint3> directions = detourScale == 0
					? new[] { CadPoint3.Zero }
					: detourDirections.SelectMany(direction => new[] { direction, -direction });
				foreach (CadPoint3 direction in directions)
				{
					CadPoint3 joint = baseJoint + direction * (length * detourScale);
					CadPoint3 incoming = joint - entryGuide;
					CadPoint3 outgoing = targetGuide - joint;
					if (incoming.LengthSquared <= 1e-12 || outgoing.LengthSquared <= 1e-12)
					{
						continue;
					}
					CadPoint3 incomingDirection = incoming.Normalized();
					CadPoint3 outgoingDirection = outgoing.Normalized();
					List<CadPoint3> jointTangents = new();
					AddUnitDirection(
						jointTangents,
						incomingDirection + outgoingDirection
					);
					AddUnitDirection(
						jointTangents,
						2 * incomingDirection + outgoingDirection
					);
					AddUnitDirection(
						jointTangents,
						incomingDirection + 2 * outgoingDirection
					);
					AddUnitDirection(jointTangents, guideDirection);
					AddUnitDirection(jointTangents, entry.Tangent + target.Tangent);
					foreach (CadPoint3 jointTangent in jointTangents)
					{
						PathCandidate candidate = EvaluateSplitCandidate(
							entry,
							target,
							joint,
							jointTangent,
							preferredStart,
							preferredEnd,
							radius
						);
						candidate.Score += length * detourScale * 0.05;
						if (candidate.IsFeasible
							&& (!hasLayerFeasible || candidate.Score < bestLayer.Score))
						{
							bestLayer = candidate;
							hasLayerFeasible = true;
						}
						if (!candidate.IsFeasible
							&& (!hasInvalid
								|| candidate.MinimumRadius > bestInvalid.MinimumRadius))
						{
							bestInvalid = candidate;
							hasInvalid = true;
						}
					}
				}
			}
			if (hasLayerFeasible)
			{
				PathCandidate certified = Evaluate(bestLayer.Spans, radius);
				certified.Score = bestLayer.Score;
				if (certified.IsFeasible)
				{
					return certified;
				}
				if (!hasInvalid || certified.MinimumRadius > bestInvalid.MinimumRadius)
				{
					bestInvalid = certified;
					hasInvalid = true;
				}
			}
		}
		return bestInvalid;
	}

	private static PathCandidate EvaluateSplitCandidate(
		CadFrame entry,
		CadFrame target,
		CadPoint3 joint,
		CadPoint3 jointTangent,
		double startHandle,
		double endHandle,
		double radius
	)
	{
		CadFrame jointFrame = new(joint, jointTangent, entry.Normal);
		PathCandidate first = SolveHalf(
			entry,
			jointFrame,
			radius,
			startHandle,
			Math.Max(1, (joint - entry.Origin).Length / 3)
		);
		PathCandidate second = SolveHalf(
			jointFrame,
			target,
			radius,
			Math.Max(1, (target.Origin - joint).Length / 3),
			endHandle
		);
		PathCandidate combined = Evaluate(
			first.Spans.Concat(second.Spans).ToArray(),
			radius,
			20,
			checkSelfIntersection: false
		);
		combined.Score = combined.Length + first.Score + second.Score;
		return combined;
	}

	private static PathCandidate SolveHalf(
		CadFrame entry,
		CadFrame target,
		double radius,
		double preferredStart,
		double preferredEnd
	)
	{
		double length = (target.Origin - entry.Origin).Length;
		double[] starts = SplitHandleCandidates(length, preferredStart);
		double[] ends = SplitHandleCandidates(length, preferredEnd);
		PathCandidate bestFeasible = default;
		PathCandidate bestInvalid = default;
		bool hasFeasible = false;
		bool hasInvalid = false;
		foreach (double start in starts)
		{
			foreach (double end in ends)
			{
				WorldSpan span = new(
					entry.Origin,
					entry.Origin + entry.Tangent * start,
					target.Origin - target.Tangent * end,
					target.Origin
				);
				PathCandidate candidate = Evaluate(
					new[] { span },
					radius,
					16,
					checkSelfIntersection: false
				);
				candidate.Score = 0.01 * (
					Math.Abs(start - preferredStart)
					+ Math.Abs(end - preferredEnd)
				);
				if (candidate.IsFeasible
					&& (!hasFeasible || candidate.Score < bestFeasible.Score))
				{
					bestFeasible = candidate;
					hasFeasible = true;
				}
				if (!candidate.IsFeasible
					&& (!hasInvalid || candidate.MinimumRadius > bestInvalid.MinimumRadius))
				{
					bestInvalid = candidate;
					hasInvalid = true;
				}
			}
		}
		return hasFeasible ? bestFeasible : bestInvalid;
	}

	private static double[] SplitHandleCandidates(double spanLength, double preferred)
	{
		return new[] { 0.1, 0.18, 0.25, 1.0 / 3.0, 0.5, 0.75, 1.0, 1.4, 2.0 }
			.Select(scale => Math.Max(1e-3, spanLength * scale))
			.Append(preferred)
			.DistinctBy(value => Math.Round(value, 9))
			.ToArray();
	}

	private static void AddPerpendicularDirection(
		ICollection<CadPoint3> directions,
		CadPoint3 candidate,
		CadPoint3 axis
	)
	{
		CadPoint3 rejected = candidate - CadPoint3.Dot(candidate, axis) * axis;
		AddUnitDirection(directions, rejected);
	}

	private static void AddUnitDirection(
		ICollection<CadPoint3> directions,
		CadPoint3 candidate
	)
	{
		if (!candidate.IsFinite || candidate.LengthSquared <= 1e-12)
		{
			return;
		}
		CadPoint3 normalized = candidate.Normalized();
		if (directions.Any(existing => Math.Abs(CadPoint3.Dot(existing, normalized)) > 1 - 1e-6))
		{
			return;
		}
		directions.Add(normalized);
	}

	private static PathCandidate CreateSingle(
		CadFrame entry,
		CadFrame target,
		double startHandle,
		double endHandle,
		double radius
	)
	{
		WorldSpan span = new(
			entry.Origin,
			entry.Origin + entry.Tangent * startHandle,
			target.Origin - target.Tangent * endHandle,
			target.Origin
		);
		return Evaluate(new[] { span }, radius);
	}

	private static PathCandidate Evaluate(
		IReadOnlyList<WorldSpan> spans,
		double radius,
		int sampleCount = CurveSamples,
		bool checkSelfIntersection = true
	)
	{
		List<CadPoint3> samples = new();
		double minimumRadius = double.PositiveInfinity;
		double length = 0;
		bool regular = true;
		for (int spanIndex = 0; spanIndex < spans.Count; ++spanIndex)
		{
			WorldSpan span = spans[spanIndex];
			CadPoint3 previous = span.Start;
			for (int index = 0; index <= sampleCount; ++index)
			{
				double t = index / (double)sampleCount;
				CadPoint3 point = Point(span, t);
				if (spanIndex == 0 || index > 0)
				{
					samples.Add(point);
				}
				if (index > 0)
				{
					length += (point - previous).Length;
				}
				previous = point;

				CadPoint3 derivative = Derivative(span, t);
				CadPoint3 secondDerivative = SecondDerivative(span, t);
				double speed = derivative.Length;
				if (!double.IsFinite(speed) || speed <= 1e-8)
				{
					regular = false;
					continue;
				}
				double cross = CadPoint3.Cross(derivative, secondDerivative).Length;
				if (cross > 1e-12)
				{
					minimumRadius = Math.Min(minimumRadius, speed * speed * speed / cross);
				}
			}
		}
		bool selfIntersects = checkSelfIntersection && HasPolylineSelfIntersection(samples);
		double requiredRadius = radius + Math.Max(1e-4, radius * 1e-9);
		double reportedMinimumRadius = double.IsPositiveInfinity(minimumRadius)
			? double.MaxValue
			: minimumRadius;
		return new PathCandidate
		{
			Spans = spans.ToArray(),
			MinimumRadius = reportedMinimumRadius,
			Length = length,
			IsFeasible = regular && !selfIntersects && reportedMinimumRadius >= requiredRadius,
		};
	}

	private static bool HasPolylineSelfIntersection(IReadOnlyList<CadPoint3> points)
	{
		if (points.Count < 5)
		{
			return false;
		}
		double scale = (points[^1] - points[0]).Length;
		double toleranceSquared = Math.Pow(Math.Max(1e-6, scale * 1e-6), 2);
		for (int left = 0; left < points.Count - 1; ++left)
		{
			for (int right = left + 3; right < points.Count - 1; ++right)
			{
				if (SegmentDistanceSquared(
					points[left],
					points[left + 1],
					points[right],
					points[right + 1]
				) <= toleranceSquared)
				{
					return true;
				}
			}
		}
		return false;
	}

	private static double SegmentDistanceSquared(
		CadPoint3 p1,
		CadPoint3 q1,
		CadPoint3 p2,
		CadPoint3 q2
	)
	{
		CadPoint3 d1 = q1 - p1;
		CadPoint3 d2 = q2 - p2;
		CadPoint3 r = p1 - p2;
		double a = CadPoint3.Dot(d1, d1);
		double e = CadPoint3.Dot(d2, d2);
		double f = CadPoint3.Dot(d2, r);
		double s;
		double t;
		if (a <= 1e-18 && e <= 1e-18)
		{
			return r.LengthSquared;
		}
		if (a <= 1e-18)
		{
			s = 0;
			t = Math.Clamp(f / e, 0, 1);
		}
		else
		{
			double c = CadPoint3.Dot(d1, r);
			if (e <= 1e-18)
			{
				t = 0;
				s = Math.Clamp(-c / a, 0, 1);
			}
			else
			{
				double b = CadPoint3.Dot(d1, d2);
				double denominator = a * e - b * b;
				s = denominator == 0 ? 0 : Math.Clamp((b * f - c * e) / denominator, 0, 1);
				t = (b * s + f) / e;
				if (t < 0)
				{
					t = 0;
					s = Math.Clamp(-c / a, 0, 1);
				}
				else if (t > 1)
				{
					t = 1;
					s = Math.Clamp((b - c) / a, 0, 1);
				}
			}
		}
		CadPoint3 closest = r + d1 * s - d2 * t;
		return closest.LengthSquared;
	}

	private static double[] HandleCandidates(double chordLength, double preferred, double previous)
	{
		double minimum = Math.Max(1e-3, chordLength * 0.03);
		double maximum = Math.Max(minimum * 2, chordLength * 2.5);
		double[] scales = { 0.08, 0.15, 0.25, 1.0 / 3.0, 0.5, 0.75, 1.0, 1.4, 2.0 };
		return scales.Select(scale => chordLength * scale)
			.Append(preferred)
			.Append(previous)
			.Where(double.IsFinite)
			.Select(value => Math.Clamp(value, minimum, maximum))
			.DistinctBy(value => Math.Round(value, 9))
			.ToArray();
	}

	private static double PreviousStartHandle(
		CadCollectorBranchPath path,
		CadFrame outlet,
		CadFrame inlet
	)
	{
		if (path?.Spans == null || path.Spans.Count == 0)
		{
			return 0;
		}
		return (outlet.TransformLocalPoint(path.Spans[0].Control1Local) - inlet.Origin).Length;
	}

	private static double PreviousEndHandle(CadCollectorBranchPath path, CadFrame outlet)
	{
		if (path?.Spans == null || path.Spans.Count == 0)
		{
			return 0;
		}
		CadCollectorBranchSpan last = path.Spans[^1];
		return (outlet.TransformLocalPoint(last.EndLocal)
			- outlet.TransformLocalPoint(last.Control2Local)).Length;
	}

	private static CadCollectorBranchPath ToPath(
		PathCandidate candidate,
		CadFrame outlet,
		double radius,
		string diagnostic
	)
	{
		CadCollectorBranchPath path = new()
		{
			SolverVersion = CadCollectorBranchPath.CurrentSolverVersion,
			OuterRadiusMillimetres = radius,
			IsFeasible = candidate.IsFeasible,
			MinimumRadiusMillimetres = candidate.MinimumRadius,
			Diagnostic = diagnostic,
		};
		foreach (WorldSpan span in candidate.Spans ?? Array.Empty<WorldSpan>())
		{
			path.Spans.Add(new CadCollectorBranchSpan
			{
				Control1Local = outlet.InverseTransformPoint(span.Control1),
				Control2Local = outlet.InverseTransformPoint(span.Control2),
				EndLocal = outlet.InverseTransformPoint(span.End),
			});
		}
		return path;
	}

	private static CadPoint3 Point(CadCollectorWorldBranchSpan span, double t)
	{
		return Point(new WorldSpan(span.Start, span.Control1, span.Control2, span.End), t);
	}

	private static CadPoint3 Point(WorldSpan span, double t)
	{
		double inverse = 1 - t;
		return span.Start * (inverse * inverse * inverse)
			+ span.Control1 * (3 * inverse * inverse * t)
			+ span.Control2 * (3 * inverse * t * t)
			+ span.End * (t * t * t);
	}

	private static CadPoint3 Derivative(WorldSpan span, double t)
	{
		double inverse = 1 - t;
		return 3 * (
			inverse * inverse * (span.Control1 - span.Start)
			+ 2 * inverse * t * (span.Control2 - span.Control1)
			+ t * t * (span.End - span.Control2)
		);
	}

	private static CadPoint3 SecondDerivative(WorldSpan span, double t)
	{
		return 6 * (
			(1 - t) * (span.Control2 - 2 * span.Control1 + span.Start)
			+ t * (span.End - 2 * span.Control2 + span.Control1)
		);
	}

	private readonly record struct WorldSpan(
		CadPoint3 Start,
		CadPoint3 Control1,
		CadPoint3 Control2,
		CadPoint3 End
	);

	private struct PathCandidate
	{
		internal WorldSpan[] Spans;
		internal bool IsFeasible;
		internal double MinimumRadius;
		internal double Length;
		internal double Score;
	}
}
