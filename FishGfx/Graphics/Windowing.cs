using System;
using System.Collections.Generic;
using System.Numerics;

namespace FishGfx.Graphics;

public enum WindowMode
{
	Windowed,
	BorderlessFullscreen,
	ExclusiveFullscreen,
}

public readonly record struct MonitorVideoMode(
	int Width,
	int Height,
	int RefreshRate
)
{
	public Vector2 Size => new(Width, Height);
}

public sealed class MonitorInfo
{
	private readonly MonitorVideoMode[] videoModes;

	internal MonitorInfo(
		int index,
		IntPtr nativeHandle,
		string name,
		Vector2 position,
		Vector2 physicalSizeMillimeters,
		MonitorVideoMode currentVideoMode,
		MonitorVideoMode[] videoModes,
		bool isPrimary
	)
	{
		Index = index;
		NativeHandle = nativeHandle;
		Name = name ?? string.Empty;
		Position = position;
		PhysicalSizeMillimeters = physicalSizeMillimeters;
		CurrentVideoMode = currentVideoMode;
		this.videoModes = videoModes ?? Array.Empty<MonitorVideoMode>();
		VideoModes = Array.AsReadOnly(this.videoModes);
		IsPrimary = isPrimary;
	}

	public int Index { get; }

	public string Name { get; }

	public Vector2 Position { get; }

	public Vector2 Size => CurrentVideoMode.Size;

	public Vector2 PhysicalSizeMillimeters { get; }

	public MonitorVideoMode CurrentVideoMode { get; }

	public IReadOnlyList<MonitorVideoMode> VideoModes { get; }

	public bool IsPrimary { get; }

	internal IntPtr NativeHandle { get; }

	internal bool Supports(MonitorVideoMode videoMode)
	{
		return Array.IndexOf(videoModes, videoMode) >= 0;
	}
}
