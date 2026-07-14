# Graphics API Guide

This document describes the supported rendering contract in `FishGfx.Modern.sln`. The public flow is explicit: a `RenderWindow` owns a `GraphicsContext`, a context creates a `RenderFrame`, and a frame contains sequential `RenderPass` instances. Drawing and command replay happen through an active pass.

The former global `Gfx`, `RenderAPI`, `ShaderUniforms`, `RenderTexture`, and public raw-binding APIs have been removed. `GraphicsContext.Current` remains a read-only escape hatch for factories that cannot receive their owner directly; application rendering should use `RenderWindow.Graphics`.

## Context, frame, and pass lifetime

- `RenderWindow.Graphics` owns the OpenGL context, capabilities, backbuffer, state cache, immediate renderer, and registered GPU resources.
- A context can only be used from its creating thread. Call `MakeCurrent` after switching windows on that thread.
- GPU resources retain their owning context. Cross-context uploads, copies, attachments, and draws are rejected.
- A context allows one active `RenderFrame`, and a frame allows one active `RenderPass`. A frame can contain multiple sequential passes.
- `RenderFrame.Present` is the only frame operation that swaps buffers. Disposing an unpresented frame does not present it.
- Pass scopes for state, model transforms, views, and queries must be disposed in reverse order before their pass closes.
- The context-owned backbuffer cannot be disposed. An offscreen target cannot be disposed while a pass uses it.

```csharp
using RenderWindow window = new(new RenderWindowOptions
{
	Width = 1280,
	Height = 720,
	Title = "FishGfx",
	MinimumVersion = new OpenGlVersion(4, 0),
});

GraphicsContext graphics = window.Graphics;
Camera camera = new();
camera.SetOrthogonal(0, 0, window.Width, window.Height);

using RenderFrame frame = graphics.BeginFrame();
using (RenderPass pass = frame.BeginPass(
	graphics.Backbuffer,
	new RenderPassDescriptor
	{
		View = new RenderView(camera),
		State = RenderState.Default,
		ColorLoadAction = RenderLoadAction.Clear,
		DepthLoadAction = RenderLoadAction.Clear,
		ClearColor = new Color(24, 25, 27),
	}
))
{
	pass.FillCircle(new Vector2(320, 240), 80, Color.Blue);
	pass.DrawText(font, new Vector2(230, 120), "explicit passes", Color.White, 32);
}

frame.Present();
```

## Pass state and uniforms

`RenderPassDescriptor` supplies an immutable `RenderView`, initial `RenderState`, attachment load actions, clear values, and finite shader time. Model, view, texture-size, alpha-cutoff, sample-count, and time uniforms are internal pass state uploaded by `ShaderProgram`; callers no longer mutate shared global uniforms.

`RenderState` covers culling, winding, depth comparison and writes, blending, color masks, point size, depth clamp, scissoring, and independent front/back stencil state. Use a scoped replacement when drawing overlays or special materials:

```csharp
RenderState overlay = pass.State with
{
	DepthTestEnabled = false,
	DepthWriteEnabled = false,
};

using (pass.PushState(overlay))
using (pass.PushView(hudView))
{
	pass.FillRectangle(8, 8, 240, 48, new Color(0, 0, 0, 180));
}
```

## Resource ownership

Create GPU resources through their owning `GraphicsContext`. Public factories cover textures and image loading, buffers, shader stages/programs, queries, meshes, and render targets. `TrueTypeFont` and `BitmapFont` are CPU-side objects; `PrepareAtlas` creates and caches a `FontAtlas` per context when text is drawn.

`GraphicsResource.Dispose` invalidates a resource immediately and queues its OpenGL deletion for the owner thread. Frame start, presentation, `CollectGarbage`, and context shutdown drain the queue. Explicit disposal is recommended for predictable memory use.

Ownership rules:

- `RenderTarget` owns its private framebuffer and attachment textures; the context owns the backbuffer.
- `Mesh2D`, `Mesh3D`, and internal voxel meshes own their vertex arrays and buffers.
- `Sprite`, `Tilemap`, `Terrain`, and `RenderModel` own their internal meshes, but not assigned textures or shaders.
- `VoxelRenderer` owns its workers, shaders, generated meshes, and transparent stream, but not its world, palette, atlas, or required compatible `VoxelLighting` solver.
- Commands, command batches, render items, and queues never own referenced resources.

## Buffers, textures, and targets

`GraphicsBufferDescriptor` defines a positive byte size, `BufferBindFlags`, and a `BufferUsage` hint. `Write<T>` accepts a tightly packed unmanaged span, `ResizeDiscard` reallocates without preserving contents, and `CopyTo` performs checked same-context copies when the source and destination have the required transfer flags.

`TextureDescriptor` defines extent, format, usage, dimension, mip count, sample count, fixed sample locations, and sampling. Texture writes use an explicit `TextureDataFormat` and checked region/subresource. Mipmap generation is explicit. Copies require compatible same-context resources; multisample color resolution uses `RenderFrame.ResolveColor`.

`RenderTargetDescriptor` creates single-sample or multisample color/depth targets. All attachments have identical extents and sample counts, and descriptor validation checks the current context limits. Call `BeginPass` with the target; framebuffer binding remains internal.

The Windows `System.Drawing` bridge is exposed through context methods such as `LoadTexture`, `CreateTextureFromImage`, `LoadTextureAtlas`, and `LoadCubemap`. `TextureLoadOptions.FlipY` defaults to `true` for OpenGL texture coordinates.

## Immediate pass drawing

`RenderPass` exposes the immediate drawing vocabulary directly:

- points, lines, and line strips;
- filled, outlined, textured, rounded, and nine-patch rectangles;
- filled, outlined, and textured circles and ellipses;
- filled and outlined rings;
- quadratic and cubic Bézier curves;
- 2D/3D meshes, retained `IRenderable` objects, models, and text.

The implementation uses context-owned streaming meshes and shaders. Public methods validate that the pass is active and that every resource belongs to the same context.

## Commands and deferred queues

`RenderCommand` has one replay method: `Execute(RenderPass)`. `RenderCommandList`, immutable `RenderCommandBatch`, `RenderItem`, and `RenderQueue` deliberately do not expose no-argument or self-directed execution. The active pass is the sole replay authority:

```csharp
RenderCommandList commands = new();
commands.RecordFillCircle(new Vector2(320, 240), 80, Color.Blue);
commands.RecordDrawText(font, new Vector2(230, 120), "replay me", Color.White, 32);

RenderCommandBatch batch = commands.Snapshot();
pass.Execute(batch);
```

Recorded arrays are copied. Referenced textures, fonts, shaders, meshes, and models remain caller-owned and must stay alive through replay. Lists reject mutation and recursive replay while executing.

`RenderQueue` snapshots a command batch with its model matrix, sort position, layer, 64-bit sort key, tag, bucket, and stable sequence. `pass.Execute(queue)` renders opaque items first, transparent items second, and custom buckets in insertion order. Use the bucket overload with `RenderItemComparers` when a pass needs an explicit comparer.

## Compatibility boundary

The supported build boundary is `FishGfx.Modern.sln`. Legacy demos and tools outside it still target the removed global/static API and are intentionally excluded until separately migrated. No obsolete aliases or forwarding shims are provided.

See [README.md](README.md) for runnable examples, [INFO.md](INFO.md) for project architecture, and [BUGS.md](BUGS.md) for correctness history.
