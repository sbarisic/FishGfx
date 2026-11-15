using System;
using System.Numerics;
using FishGfx.Graphics;

namespace FishGfx.FishUI;

public sealed partial class FishUIGraphicsBackend
{
    private Vertex2[] polylineVertices = Array.Empty<Vertex2>();

    /// <summary>Draws a connected path in UI coordinates in one submission, respecting the active clip.</summary>
    public void DrawPolyline(ReadOnlySpan<Vector2> points, float thickness, global::FishUI.FishColor color)
    {
        RenderPass activePass = RequireDrawing();
        if (points.Length < 2)
        {
            return;
        }
        if (polylineVertices.Length < points.Length)
        {
            polylineVertices = new Vertex2[Math.Max(points.Length, polylineVertices.Length * 2)];
        }
        int height = GetWindowHeight();
        Color convertedColor = FishUIConversions.ToFishGfxColor(color);
        for (int index = 0; index < points.Length; index++)
        {
            polylineVertices[index] = new Vertex2(FishUIConversions.ToFishGfxPoint(points[index], height), convertedColor);
        }
        activePass.DrawLineStrip(polylineVertices.AsSpan(0, points.Length), thickness);
        LastFrameDrawCallCount++;
    }
}
