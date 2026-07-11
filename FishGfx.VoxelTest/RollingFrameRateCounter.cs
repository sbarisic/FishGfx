using System;
using System.Collections.Generic;

namespace FishGfx.VoxelTest
{
	internal sealed class RollingFrameRateCounter
	{
		private readonly double windowSeconds;
		private readonly Queue<FrameSample> samples = new Queue<FrameSample>();
		private double durationTotal;

		internal RollingFrameRateCounter(double windowSeconds = 0.5)
		{
			if (!double.IsFinite(windowSeconds) || windowSeconds <= 0)
				throw new ArgumentOutOfRangeException(nameof(windowSeconds));

			this.windowSeconds = windowSeconds;
		}

		internal double FramesPerSecond => samples.Count == 0 || durationTotal <= 0
			? 0
			: samples.Count / durationTotal;

		internal double FrameMilliseconds => samples.Count == 0
			? 0
			: durationTotal * 1000 / samples.Count;

		internal int SampleCount => samples.Count;

		internal void Update(double timestamp, double frameDuration)
		{
			if (!double.IsFinite(timestamp))
				throw new ArgumentOutOfRangeException(nameof(timestamp));
			if (!double.IsFinite(frameDuration) || frameDuration <= 0)
				throw new ArgumentOutOfRangeException(nameof(frameDuration));

			samples.Enqueue(new FrameSample(timestamp, frameDuration));
			durationTotal += frameDuration;
			double cutoff = timestamp - windowSeconds;

			while (samples.Count > 1 && samples.Peek().Timestamp < cutoff)
				durationTotal -= samples.Dequeue().Duration;
		}

		private readonly struct FrameSample
		{
			internal FrameSample(double timestamp, double duration)
			{
				Timestamp = timestamp;
				Duration = duration;
			}

			internal double Timestamp { get; }
			internal double Duration { get; }
		}
	}
}
