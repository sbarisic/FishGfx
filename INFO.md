# FishGfx Project Information

The public rendering contract, ownership rules, descriptor behavior, image orientation, and migration boundary are documented in [GRAPHICS_API.md](GRAPHICS_API.md).

This document describes the supported modern FishGfx code as verified from the repository source. For build commands and examples, see [README.md](README.md). For resolved and open defects, see [BUGS.md](BUGS.md).

## Supported baseline

The supported configuration is Windows x64 on .NET 10. FishGfx creates an OpenGL core-profile context through its custom GLFW binding and sends OpenGL calls through Silk.NET.OpenGL.

Context creation prefers OpenGL 4.6 and falls back one minor version at a time to OpenGL 4.0. OpenGL 4.5 and newer use Direct State Access where available; older 4.x contexts use bind-to-edit implementations.

Current core dependencies are:

- Silk.NET.OpenGL 2.23.0 for OpenGL entry points and types.
- System.Drawing.Common 10.0.0 for Windows image loading and screenshots.
- StbTrueTypeSharp 1.26.12 for TrueType metrics and SDF glyph generation.
- The bundled Windows x64 `glfw3.dll` and managed bindings under `FishGfx/Glfw3`.

The optional `FishGfx.FishUI` integration references the MIT-licensed FishUI repository through `thirdparty/FishUI`, pinned to commit `fc2b733e34c3769e5510abde2820c323a69d1448`. FishUI targets .NET 9 and brings YamlDotNet 16.3.0 for themes and layouts; the adapter and VoxelTest target .NET 10.

Intel RealSense support has been removed. Linux and macOS are not part of the modern supported baseline.

## Modern solution

`FishGfx.Modern.sln` is the supported entry point and contains six projects:

- `FishGfx`: rendering, windowing, input, formats, fonts, voxel rendering, and node-graph APIs.
- `FishGfx.FishUI`: reusable FishUI graphics, input, rooted-file-system, and event adapters.
- `FishGfx.SmokeTest`: interactive primitive gallery, developer console, and automated screenshot validation.
- `FishGfx.NodeEditor`: function-node editor, automatic graphical validation, and headless JSON graph execution.
- `FishGfx.VoxelTest`: interactive and automatic voxel renderer validation.
- `FishGfx.Tests`: xUnit coverage for geometry, resources, commands, fonts, graphs, persistence, UI models, and voxels.

Older demos and tools outside this solution still depend on legacy APIs. They are intentionally excluded from modern build acceptance and do not define the supported public contract.

## Context, frame, and pass model

`RenderWindow` owns one `GraphicsContext` and exposes it through `Graphics`. The context owns its capabilities, resize-aware backbuffer, immediate renderer, state cache, and registered GPU resources. Context operations run on the creating thread; applications switching between windows call `GraphicsContext.MakeCurrent`. `GraphicsContext.Current` is a read-only factory escape hatch, not the normal application rendering path.

`GraphicsContext.BeginFrame` creates one active `RenderFrame`. A frame contains sequential `RenderPass` instances targeting either `GraphicsContext.Backbuffer` or a context-created `RenderTarget`. `RenderFrame.Present` is the only frame operation that swaps buffers, and disposing an unpresented frame does not present it.

`RenderPassDescriptor` captures an immutable `RenderView`, initial `RenderState`, color/depth/stencil load actions, clear values, and finite shader time. A pass exposes scoped state, model, view, and graphics-query changes. These scopes must close in reverse order before the pass is disposed.

`RenderView` snapshots camera view/projection matrices, position, viewport size, and clip planes. Internal pass uniform state carries the view, model transform, texture size, alpha cutoff, sample count, and time to `ShaderProgram`; the removed `ShaderUniforms` global is not part of the public API.

`InputManager` turns window callbacks into held, pressed, and released state. Applications call `InputManager.BeginFrame` before polling window events.

## Render state and resource ownership

`RenderState` describes culling, winding, depth comparison and writes, blending, color masks, point size, depth clamping, scissoring, and independent front/back stencil behavior. `RenderPass.PushState` applies a scoped replacement and restores the previous state on disposal.

Public GPU resources are created through their owning `GraphicsContext`:

- `GraphicsBuffer` from `GraphicsBufferDescriptor` or an initial unmanaged span.
- `Texture` from `TextureDescriptor` or the context image-loading helpers.
- `ShaderStage` and `ShaderProgram`.
- `GraphicsQuery`.
- `Mesh2D` and `Mesh3D`.
- `RenderTarget` from `RenderTargetDescriptor`.

Every `GraphicsResource` retains its owner and rejects cross-context use. Disposal invalidates it immediately and queues OpenGL deletion for the owner thread. Frame start, presentation, `CollectGarbage`, and context shutdown drain that queue. The context-owned backbuffer cannot be disposed.

`RenderTarget` owns its private framebuffer and attachment textures. Single-sample attachments can be sampled and copied; multisample color attachments are resolved between compatible targets with `RenderFrame.ResolveColor`. Framebuffer binding remains internal.

`GraphicsBufferDescriptor` defines byte size, `BufferBindFlags`, and `BufferUsage`. Writes use checked unmanaged spans, `ResizeDiscard` reallocates without preserving data, and `CopyTo` requires compatible same-context transfer flags.

`TextureDescriptor` defines dimension, storage format, usage, mip count, sample count, fixed-sample behavior, and sampling. Writes select a `TextureDataFormat`, checked region, and subresource. Mipmap generation is explicit. Same-format, non-multisampled 2D regions and cubemap faces can be copied on the GPU.

The context's `LoadTexture`, `CreateTextureFromImage`, `LoadTextureAtlas`, and `LoadCubemap` methods use the Windows `System.Drawing` bridge. `TextureLoadOptions.FlipY` defaults to `true` for FishGfx's bottom-left texture convention. Temporary images are disposed deterministically.

## Pass drawing

`RenderPass` owns immediate drawing and command replay. Its drawing vocabulary includes:

- Points, thick lines, and line strips.
- Filled, outlined, textured, rounded, and nine-patch rectangles.
- Filled, outlined, and textured circles and ellipses.
- Filled and outlined rings.
- Quadratic and cubic Bézier curves.
- 2D and 3D meshes, `RenderModel`, other `IRenderable` objects, and text.

The implementation reuses context-owned streaming meshes and built-in shaders. CPU tessellators remain context-free and are tested independently. Draw calls validate the active pass and the ownership of referenced GPU resources.

Retained types include `Sprite`, `Tilemap`, `Terrain`, `ParallaxSprite`, and `RenderModel`. These objects own their internal meshes but do not take ownership of assigned textures or shaders.

## Commands and render queues

`RenderCommand` defines exactly one replay method: `Execute(RenderPass)`. `RenderCommandList` is a mutable, inspectable recorder; `Snapshot` creates an immutable `RenderCommandBatch`. Recording does no OpenGL work, recorded arrays are copied, and referenced resources remain caller-owned.

Only an active `RenderPass` can execute a command, list, batch, item, or queue. Lists and batches reject recursive replay, lists reject mutation during replay, and replay stops at the first exception. `RenderStateScopeCommand` owns a nested batch so callers do not manually balance state-stack commands.

`RenderQueue` partitions immutable command batches into `RenderQueueBucket.Opaque`, `RenderQueueBucket.Transparent`, or caller-defined buckets. Each `RenderItem` captures a model matrix, sort position, layer, 64-bit sort key, optional tag, and stable sequence. `RenderItemComparers` supplies opaque front-to-back, opaque state-then-front-to-back, and transparent back-to-front ordering.

`RenderPass.Execute(RenderQueue)` draws opaque items first, transparent items second, and custom buckets in insertion order. The bucket overload accepts an explicit comparer. Queue execution temporarily applies each item's model matrix and restores it afterward. `BeginFrame` or `Clear` releases all submissions; queues do not own referenced resources and are not thread-safe.

## Fonts and text

`GraphicsFont` is the text layout and measurement abstraction. `Layout` and `Measure` take an explicit size, so fonts have no mutable global scale. `RenderPass.DrawText` asks the font for a context-specific `FontAtlas`, lays out the text, and emits one triangle batch.

Two implementations are supported:

- `BitmapFont` parses binary AngelCode BMFont v3 descriptors, pages, glyph metrics, and kerning pairs.
- `TrueTypeFont` uses stb_truetype to generate SDF glyphs, grows and repacks CPU atlas data, and caches an uploaded `FontAtlas` per `GraphicsContext`.

TrueType layout handles individual Unicode BMP characters, multiline text, tabs, fallback glyphs, and pair kerning. Complex shaping, combining-mark behavior, right-to-left layout, and supplementary Unicode planes are not implemented.

## FishUI integration

`FishUIGraphicsBackend` derives from FishUI's `SimpleFishUIGfx`. It never owns a frame or pass; the application binds a caller-owned active `RenderPass`, `RenderView`, and overlay `RenderState` through `UseRenderPass` around `FishUI.TickDraw`.

The backend converts FishUI's top-left coordinates and atlas rectangles to FishGfx's bottom-left geometry and texture convention. It supports clipping, images, atlas regions, rotation, scaling, nine-patches, filtering, text, lines, rectangles, and circles. Loaded images retain both a shared GPU texture and CPU bitmap for pixel queries. Loaded textures and fonts are disposed with the backend.

`FishUIInputAdapter` subscribes to `RenderWindow` keyboard, mouse, text, and scroll events. It preserves held state, queues per-frame transitions and Unicode input, exposes clipboard text, and clears transitions through `BeginFrame`. Its `Enabled` gate hides interaction without corrupting physical held state.

`RootedFishUIFileSystem` resolves relative resources from `AppContext.BaseDirectory` by default. Applications must copy FishUI's `data` tree to their output; `FishGfx.VoxelTest.csproj` demonstrates the linked content and publish settings.

## Voxel rendering

`FishGfx.Voxels` provides editable 16³ chunks with negative-coordinate-safe addressing. `VoxelWorld` tracks per-chunk revisions and residency generations, snapshots chunks for worker jobs, and invalidates the changed chunk plus affected neighbors after boundary edits. `VoxelPaletteBuilder` reserves material ID zero for air and builds immutable render metadata.

Voxel materials select opaque, alpha-cutout, or transparent rendering; uniform or per-face atlas tiles; tint; face occlusion; double-sided output; optional `VoxelModelSet` geometry; optional transparent-cube `VoxelWaveSettings`; and independent light opacity and RGB emission. `MinecraftVoxelModelLoader` converts Minecraft/Blockbench element JSON into local triangle geometry mapped through `VoxelTextureRegion`.

`VoxelLighting` is an explicit, caller-owned RGB block-light and skylight solver. Lighting residency is separate from voxel occupancy: applications call `LoadChunk` and `UnloadChunk`, register known air chunks, and mark open positive-Y boundaries with `SetSkyExposedAbove` or `skyExposedAbove`. Unknown space blocks propagation. `Update` performs budgeted work and publishes completed light transactions atomically; `GetLight` exposes only the last published solution.

`VoxelMeshingScheduler` captures immutable world and published-light neighborhood references on the calling thread. Persistent below-normal-priority workers materialize padded snapshots, prove suppression, and build geometry. Scheduling examines a bounded visible-first candidate window only when a worker slot is free. Results carry world/light revisions and residency generations. The context thread discards stale results and creates or updates GPU meshes through `VoxelRenderer.UpdateMeshes`; workers never create or destroy OpenGL resources.

`VoxelRenderer` requires an owning `GraphicsContext`, world, palette, atlas texture/layout, and compatible `VoxelLighting`. It owns its scheduler, shaders, generated GPU meshes, and transparent stream, while the application owns the world, palette, atlas, and lighting solver. `VoxelRendererOptions` configures worker count, upload-count and upload-time budgets, render distance, alpha cutoff, meshing, and initial sun settings. The upload-time budget defaults to positive infinity and always permits one eligible upload. `UpdateMeshes(camera)` prioritizes visible and nearby work without changing lighting residency; empty and provably enclosed occluding chunks are completed by workers without materializing padded snapshots.

`EnqueueVisible` distance- and frustum-culls GPU chunks and submits retained batches to a caller-owned `RenderQueue`. Opaque and cutout passes share the opaque bucket with stable sort keys. Transparent faces from all visible chunks are transformed to world space, stably sorted back-to-front, uploaded to one persistent stream, and submitted once to the transparent bucket. `pass.Execute(queue)` applies the required opaque-then-transparent order.

`VoxelRenderer.SunSettings` changes directional-light uniforms without relighting or remeshing. `VoxelRenderer.FogSettings` changes reusable distance fog and lighting attenuation without recreating geometry. Transparent cube waves use `RenderPassDescriptor.Time` and do not rebuild meshes. `IsCullingEnabled`, `Statistics`, and `FrameDiagnostics` expose runtime control and diagnostics.

`VoxelRaycast` provides bounded DDA traversal through positive and negative coordinates. `VoxelMediumQuery` identifies the voxel material containing a world-space position.

## Models and formats

`GenericMesh` is the CPU interchange model for retained 3D geometry. `ObjModelSerializer` loads and saves Wavefront OBJ/MTL data. `SmdModelLoader` loads Valve SMD triangle data. The removed Foam and MicroConfig formats are not part of the modern solution.

`Camera` supports perspective and orthographic projection, orientation vectors, lazy matrix updates, and screen/world conversion. Canonical bounds are `AxisAlignedBoundingBox` and `BoundingSphere`.

## Function-node graphs

The `FishGfx.NodeGraph` namespace exposes reflection-driven function graphs:

- `[NodeFunction("stable.id")]` opts a public static method into `NodeFunctionRegistry` and can set its title and category.
- Ordinary parameters become typed input ports.
- `[NodeInline]` parameters become typed inline values.
- Non-void returns become outputs; named `ValueTuple` results expand into named outputs.
- Connections require exact CLR type equality, allow output fan-out, and replace an occupied input.

`FunctionGraph` exposes read-only node and connection collections. `FunctionGraphEvaluator` evaluates dependencies first, supplies defaults to unconnected inputs, detects cycles, reports invocation or inline-parse errors, skips failed dependents, and continues independent branches.

`NodeGraphJson` schema v2 persists stable function IDs, node GUIDs, named port endpoints, inline text, node positions and widths, canvas pan, and zoom. Loading is strict: unknown fields, unresolved functions or ports, duplicate IDs, invalid values, and incompatible connections produce a failed `NodeGraphLoadResult` rather than a partially accepted graph. Serialization and execution output are deterministic.

`FishGfx.NodeEditor` provides node selection and dragging, connection creation and rewiring, canvas pan/zoom, inline editing, categorized search, F5 evaluation, and Ctrl+S/Ctrl+O persistence. `--auto` opens a deterministic graphical scene and exits after validation. `--execute <layout.json>` loads and evaluates a graph without creating an OpenGL window and writes structured JSON to standard output.

## Validation applications

The smoke gallery contains one scene per immediate `RenderPass` primitive plus `RenderCommandList` and `RenderQueue` scenes. Space and Backspace navigate interactively; F1 toggles `DevConsole`. `--auto` visits every scene at a fixed animation time, captures a 1920×1080 PNG before presentation, and generates a 640×360 thumbnail. `--gl40` additionally requires an exact OpenGL 4.0 context.

`FishGfx.VoxelTest` demonstrates streamed terrain, on-demand visible-halo lighting residency, opaque/cutout/transparent materials, custom models, RGB emission, skylight attenuation, water waves, ambient occlusion, block raycasting, underwater fog, and a FishUI statistics panel and hotbar. Background voxel columns remain loaded but unlit and unmeshed until they enter the camera frustum or its one-chunk propagation halo. Lit columns remain cached until normal world unload. `--auto` waits for intentionally resident lighting and meshing convergence; exercises stale-result rejection, sun changes, normal and underwater rendering, and disabled UI input; then exits without user interaction.

The automatic and headless entry points are:

```powershell
dotnet run --project FishGfx.SmokeTest/FishGfx.SmokeTest.csproj -- --auto
dotnet run --project FishGfx.SmokeTest/FishGfx.SmokeTest.csproj -- --auto --gl40
dotnet run --project FishGfx.NodeEditor/FishGfx.NodeEditor.csproj -- --auto
dotnet run --project FishGfx.NodeEditor/FishGfx.NodeEditor.csproj -- --execute node-layout.json
dotnet run --project FishGfx.VoxelTest/FishGfx.VoxelTest.csproj -- --auto
dotnet run --project FishGfx.VoxelTest/FishGfx.VoxelTest.csproj -c Release -- --streaming-benchmark
```

The prioritized roadmap is maintained in [README.md](README.md#roadmap). Resolved and open defects are maintained in [BUGS.md](BUGS.md).
