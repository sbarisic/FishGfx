# FishGfx Project Information

The public rendering contract, ownership rules, descriptor behavior, image orientation, and migration boundary are documented in [GRAPHICS_API.md](GRAPHICS_API.md).

This document describes the supported modern FishGfx code as verified from the repository source. For build commands and examples, see [README.md](README.md). For resolved and open defects, see [BUGS.md](BUGS.md).

## Supported baseline

The supported configuration is Windows x64 on .NET 10. FishGfx creates an OpenGL core-profile context through its custom GLFW binding and sends OpenGL calls through Silk.NET.OpenGL.

The context negotiator tries OpenGL 4.6 first and falls back one minor version at a time to OpenGL 4.0. Direct State Access is used on OpenGL 4.5 and newer, with bind-to-edit implementations retained for older 4.x drivers.

Current core dependencies are:

- Silk.NET.OpenGL 2.23.0 for OpenGL entry points and types.
- System.Drawing.Common 10.0.0 for the Windows bitmap-loading, readback, and screenshot APIs.
- StbTrueTypeSharp 1.26.12 for TrueType metrics and SDF glyph generation.
- The bundled Windows x64 `glfw3.dll` and managed bindings under `FishGfx/Glfw3`.

The optional `FishGfx.FishUI` integration references the MIT-licensed FishUI repository through `thirdparty/FishUI`, pinned to commit `fc2b733e34c3769e5510abde2820c323a69d1448`. FishUI targets .NET 9 and brings YamlDotNet 16.3.0 for its theme and layout formats; the adapter and VoxelTest remain .NET 10 applications.

Intel RealSense support has been removed. Linux and macOS are not supported by the modern baseline.

## Modern solution

`FishGfx.Modern.sln` is the supported entry point and contains six projects:

- `FishGfx`: the core library.
- `FishGfx.FishUI`: reusable FishUI rendering, input, resource-path, and event adapters.
- `FishGfx.SmokeTest`: interactive primitive gallery, developer console, and automated screenshot validation.
- `FishGfx.NodeEditor`: reflected function-node editor and headless JSON graph runner.
- `FishGfx.VoxelTest`: interactive and automated voxel chunk renderer validation.
- `FishGfx.Tests`: xUnit tests for geometry, fonts, graphs, persistence, UI models, and compatibility mappings.

The older demos, tools, LiteTest, and Nuklear projects are intentionally outside this solution. They remain migration candidates and are not part of modern build acceptance.

## Context and windowing

`RenderWindow` owns a GLFW window and exposes its `GraphicsContext` through `Graphics`. `RenderWindowOptions` selects preferred/minimum OpenGL versions and exact-version behavior. The context negotiator records immutable per-context capabilities, owns the resize-aware backbuffer, and is explicitly made current when switching windows on its owning thread. `RenderWindow.Focus()` exposes GLFW window focus for UI backends.

`GraphicsContext.BeginFrame` creates one active `GraphicsFrame`. Frames contain ordered `RenderPass` instances targeting the backbuffer or a context-created `RenderTarget`; `GraphicsFrame.Present` is the only frame API that swaps buffers. `RenderPassDescriptor` captures view, fixed-function state, load actions, clear values, texture size, alpha test, and multisample count. Pass-local state, model, view, and occlusion-query scopes enforce reverse-order disposal.

`Internal_OpenGL` owns the Silk.NET `GL` dispatch table. Capability queries refresh for the current GLFW context, while debug callback initialization remains one-time. `InputManager` turns window callbacks into frame-coherent held, pressed, and released state; applications call `BeginNewFrame` before polling events.

## FishUI integration

`FishGfxFishUIGraphics` derives from FishUI's `SimpleFishUIGfx` and never owns a frame or render pass. `UseRenderPass` temporarily binds an active pass, `RenderView`, and overlay `RenderState`; FishUI's `BeginDrawing`/`EndDrawing` callbacks push and restore those scopes. Top-left FishUI coordinates and source-atlas rectangles are converted to FishGfx's bottom-left geometry and flipped texture convention. Scissor scopes are intersected by FishUI and mapped to pass-local render states. Rotated images use the pass model scope, while rotated atlas nine-patches share one screen-space transform across their nine regions.

Images and fonts resolve through `RootedFishUIFileSystem`, whose default root is `AppContext.BaseDirectory`. Image resources retain both a shared GPU texture and CPU bitmap for `GetImageColor`; fonts and images are disposed with the graphics backend. The full upstream `FishUI/FishUI/data` tree is linked into VoxelTest build and publish output because direct project-reference content does not propagate reliably and FishUI initializes its default skins eagerly.

`FishGfxFishUIInput` subscribes to `RenderWindow` key, character, and scroll events. It maps the shared GLFW keyboard values explicitly, separately maps mouse buttons, preserves held state, queues per-frame key and Unicode input, exposes clipboard text, and clears transitions through `BeginFrame` before event polling. Its `Enabled` gate hides all interactions without corrupting physical held state, which lets captured-mouse rendering applications draw a disabled UI safely.

## Render state and resource lifetime

`RenderState` describes fixed-function state including depth testing and writes, culling, winding, blending, channel masks, scissoring, and independent front/back stencil functions, operations, and write masks. State application compares the complete requested state against a per-context cache. `RenderPass.PushState` is the preferred scoped API; `Gfx.PushRenderState` and `Gfx.PopRenderState` remain context-aware compatibility calls.
GPU wrappers include:

- Descriptor-created `GraphicsBuffer` and `VertexArray`.
- `ShaderStage` and `ShaderProgram`.
- `Texture`, `Framebuffer`, `RenderTexture`, and `Renderbuffer`.
- `OcclusionQuery`.
- Streaming `Mesh2D` and `Mesh3D` drawables.

GPU objects created through a context factory capture that owning context. Cross-context use is rejected. Explicit disposal or finalization queues deletion to the owner, and `BeginFrame`, `Present`, explicit garbage collection, and context shutdown drain the queue on the owning thread. Built-in immediate-renderer meshes, shaders, render-state stacks, uniforms, render-target stacks, and active queries are also context-owned.

## Shaders and shared uniforms

FishGfx ships 2D, 3D, line, point, textured/color, and SDF text shaders under `FishGfx/data/shaders`.

`RenderView` snapshots view/projection matrices, camera position, viewport, and clip planes for a pass. `RenderPassDescriptor.Time` supplies a finite time in seconds, exposed as `ShaderUniforms.Time` and uploaded to the standard `Time` uniform. `ShaderUniforms.Current` is a context-local compatibility view over the active pass uniforms and model transform. Shader binding uploads the applicable common uniforms and caches uniform locations.

Standard interleaved vertex attributes are:

- Location 0: position.
- Location 1: color.
- Location 2: texture coordinates.

`VoxelVertex` additionally uses location 3 for its normal, location 4 for internal wave parameters, and location 5 for packed normalized voxel lighting. Lighting stores RGB block-light levels in the first three bytes and skylight in the fourth. Wave parameters remain amplitude, spatial angular frequency, temporal angular frequency, and surface influence. The dedicated layout intentionally does not alter the established `Vertex3` layout.

## Immediate 2D API

`Gfx` provides an immediate-style 2D API backed by a reusable streaming mesh. Available primitives include:

- Points, lines, and line strips with thickness support.
- Filled and outlined rectangles.
- Filled and outlined rounded rectangles with per-corner radii.
- Textured rectangles and rounded rectangles.
- Nine-patch stretching with fixed source-pixel borders.
- Filled, outlined, and textured circles and ellipses.
- Filled rings and outlined annular sectors.
- Stroked quadratic and cubic Bézier curves.
- Batched bitmap and SDF text.

CPU tessellators are context-free and independently tested. Adaptive circle, ellipse, curve, rounded-corner, and ring segment counts are bounded, while explicit segment counts support debugging and low-poly rendering.

## Typed command lists

`CommandList` is an explicit command buffer for every current `Gfx` clear, state, 2D, 3D, textured-shape, and text operation. Its public `GraphicsCommand` objects expose immutable recorded parameters for inspection. Convenience `Record*` methods canonicalize equivalent overloads, such as circles to ellipse commands and single points to array-backed point commands.

Arrays are cloned during command construction, while textures, shaders, and fonts remain caller-owned references. Recording performs no OpenGL work. `Execute` must run on the active context thread and uses the camera, model transform, and shared uniforms active at replay time. Lists are reusable and retain all commands after success or failure; replay stops on the first exception and prevents concurrent mutation or recursive execution. Built-in state commands must balance, and replay unwinds outstanding pushes after failures. Completed draws are not transactional. Callers remain responsible for synchronization and live resources.

`CommandList.Snapshot` creates an immutable `GraphicsCommandBatch`. `DeferredRenderQueue` groups those batches into built-in opaque/transparent or custom render buckets. A submission captures its model matrix, representative world position, layer, sort key, owner tag, and stable sequence while retaining caller ownership of referenced resources. Typed mesh and render-model commands extend deferred submission to retained 3D geometry.

Render passes can query buckets in insertion order or request stable opaque front-to-back, opaque state-first, and transparent back-to-front sorting using camera-space depth. Custom comparers support bounds-aware or application-specific policies. Executing a submission temporarily applies its captured model matrix and restores the previous matrix through `finally`; the camera and remaining shared uniforms stay controlled by the render pass. Queues retain submissions until `BeginFrame` or `Clear` and require external synchronization.

## Voxel rendering

The `FishGfx.Voxels` namespace implements an editable chunk-rendering core with fixed 16³ chunks. `ChunkCoordinate` uses floor division and positive modulo so negative world coordinates map correctly. `VoxelWorld` synchronizes individual reads and edits, maintains monotonically increasing per-instance chunk revisions plus non-repeating residency generations, and invalidates the owning chunk plus relevant Cartesian and diagonal neighbors when boundary voxels change. `SetChunk` copies a complete 4096-cell span atomically, coalesces invalidation to one neighborhood update, and removes an existing chunk when the replacement is entirely air.

`VoxelPaletteBuilder` reserves material ID zero for air and freezes immutable render metadata for worker access. Materials select opaque, alpha-cutout, or transparent rendering; uniform/per-face atlas tiles; tint; face occlusion; double-sided output; an optional immutable `VoxelModelSet`; optional `VoxelWaveSettings`; and independent light opacity/RGB emission. Default light opacity is 15 for face-occluding materials and zero otherwise, but transmission deliberately does not reuse face-occlusion decisions. Wave amplitude uses a lowered midpoint so the undeformed block top is the maximum crest, wavelength is measured in world units, and speed is measured in cycles per second. Custom models and non-transparent materials reject wave settings. `MinecraftVoxelModelLoader` parses Blockbench/Minecraft element JSON, including texture references and element rotations, into local-space triangle data mapped through `VoxelTextureRegion`. Model variants are selected deterministically from world coordinates and baked into normal chunk meshes on workers.

`VoxelLighting` is an optional stateful RGB block-light and skylight solver. It owns sparse 16-bit light chunks separately from voxel occupancy so applications can register known all-air chunks while unknown space remains an opaque propagation boundary. Applications explicitly load/unload lighting chunks and mark positive-Y sky boundaries. Direct skylight preserves level 15 through opacity-zero cells; attenuating cells subtract their opacity, while lateral sky and block light lose at least one level per step. Single-cell world notifications carry exact old/new material IDs, allowing equal opacity/emission signatures to stay idle; bulk changes are diffed by signature. Preparation, direct-sky traversal, four-channel relaxation, comparison, and tombstone halos are all resumable under the update budget. Copy-on-write transactions publish only after convergence, coalescing one revision and invalidation per changed light chunk. Queries and meshing see only the last published solution.

`VoxelMeshingScheduler` snapshots on the calling thread and performs CPU meshing on bounded workers. Every lighting-enabled job carries occupancy and light revisions plus non-repeating world/light residency generations. The context thread accepts only results whose revisions and generations are current and uploads at most the configured budget per update; stale results, including same-coordinate unload/reload ABA results, are discarded and rescheduled. Existing geometry edits may use the last published lighting immediately, while newly resident chunks wait for their first complete light solution. Workers never create or destroy OpenGL resources.

`VoxelRenderer` distance- and frustum-culls chunk AABBs. Opaque chunks enter `RenderBucket.Opaque`, cutout chunks enter `VoxelRenderBuckets.Cutout`, and both retain captured chunk translations. Transparent faces from all visible chunks are transformed to world space, stably sorted by camera-space depth, streamed into one persistent growable `VoxelMesh`, and submitted once with depth writes disabled. Smooth lighting averages four outside-face samples at each cube corner; custom models use trilinear light samples and retain at least their own emission. The fragment shader takes the channel-wise maximum of colored block light and directional, ambient-weighted skylight. `VoxelRenderer.Sun` changes direction, color, intensity, and ambient weighting as uniforms without recalculation or mesh upload. Without a lighting provider, full skylight preserves the original renderer output.

The transparent vertex shader combines two bounded world-space sine waves from render-pass time. Only exposed top faces and their upper side rims receive influence; bottoms, buried joins, glass, ice, and other ordinary transparent geometry remain stationary. Time is not part of either the lighting state or transparent cache key, so animation does not rebuild or upload geometry. Applications execute opaque front-to-back, cutout front-to-back, then transparent back-to-front. The atlas texture and optional lighting solver are caller-owned; the renderer owns workers, shaders, GPU meshes, and its transparent stream.

`VoxelFogSettings` configures reusable exponential distance fog, fog color, and a lighting multiplier. Changing `VoxelRenderer.Fog` updates the retained voxel draw commands; it does not upload or recreate geometry. Liquid classification remains application-owned so transparent glass does not implicitly behave as water.

## Retained drawables and formats

The core retains higher-level drawables for sprites, tile maps, terrain, parallax backgrounds, and multi-mesh models. `Camera` supports perspective and orthographic projection, orientation vectors, lazy matrix updates, and screen/world conversion.

Model and geometry support includes `GenericMesh`, Wavefront OBJ, Valve SMD loading, and the FishGfx Foam format. SMD saving and some unsupported parser segments remain roadmap items.

`GraphicsBufferDescriptor` records byte size, vertex/index/uniform/storage/transfer binding flags, and static/dynamic/stream usage. `GraphicsBuffer` supports checked unmanaged-span writes, discard-resize, and same-context GPU copies. Vertex arrays reject buffers missing their corresponding vertex or index flag; meshes own their logical draw counts rather than inferring them from byte storage.

`TextureDescriptor` records 2D/cube/multisample dimension, curated color/HDR/depth format, usage flags, mip and sample counts, fixed sample locations, and initial sampling state. Texture writes are tightly packed, bounds checked, and never regenerate mipmaps implicitly. Same-format non-multisampled 2D regions and cubemap faces can be copied on the GPU; multisample resolve remains a framebuffer blit. Texture constructors, pointer uploads, public CPU readback, and static bitmap helpers have been removed.

`TextureLoader` owns file, `Image`, atlas, update, and cubemap decoding through `System.Drawing`; all temporary images are disposed deterministically. `FlipY` defaults to true for OpenGL's bottom-left convention, and cubemap paths map right/left/top/bottom/front/back to positive X/negative X/positive Y/negative Y/positive Z/negative Z. The bitmap-facing loader and framebuffer screenshot paths are intentionally Windows-specific in this release.

## Fonts and text

`GfxFont` is the shared text layout and measurement abstraction. `Gfx.DrawText` prepares the selected atlas, lays out the string, emits one triangle batch, selects the appropriate shader, and restores the caller's font scale after drawing.

Two atlas implementations are available:

- `BMFont` parses binary AngelCode BMFont v3 data and uses its texture pages and kerning pairs.
- `TTFFont` parses `.ttf` bytes with stb_truetype, generates SDF glyphs lazily, grows and repacks one atlas, and uses an `fwidth`-based SDF shader.

The current TTF layout handles individual Unicode BMP characters, multiline text, tabs, fallback glyphs, and pair kerning. Complex-script shaping, right-to-left layout, combining behavior, and supplementary Unicode characters are deferred.

## Developer console

`DevConsole` renders a scrollable text buffer, command prompt, and semi-transparent background. It supports character/key input and host-provided command execution.

The smoke gallery wraps it with commands for help, scene listing and selection, next/previous navigation, renderer information, and quitting. F1 toggles the overlay; Escape closes it before exiting the gallery.

## Function-node graphs

The `FishGfx.NodeGraph` namespace exposes reflection-driven function graphs:

- `[NodeFunction]` opts a public static method into a registry and can assign a display name/category.
- Ordinary parameters become typed input ports.
- `[NodeBody]` parameters become inline editable values.
- Non-void returns become outputs; named `ValueTuple` results expand into multiple outputs.
- Connections require exact CLR type equality, support output fan-out, and replace an occupied input.

`FunctionNodeEvaluator` performs dependency-first evaluation, supplies default values to unconnected inputs, detects cycles, catches per-node exceptions, skips failed dependents, and continues independent branches.

`NodeGraphJson` persists function identity, node GUIDs, body text, positions, widths, connections, canvas pan, and zoom. Loading resolves functions only from a caller-supplied registry and validates the complete document before returning a replacement graph. The execution APIs return structured per-node output data suitable for command-line hosts.

## Node editor

`FishGfx.NodeEditor` is an interaction-focused editor built entirely with FishGfx rendering and input. It supports:

- Node selection and header dragging.
- Typed connection creation, replacement, removal, and rewiring.
- Canvas panning and cursor-centered zoom.
- Inline typed value editing.
- Categorized, searchable function creation with mouse and keyboard control.
- Evaluation through F5 or the toolbar.
- JSON save/reload through Ctrl+S and Ctrl+O.
- Headless execution through `--execute <layout.json>`.

The editor registers bundled sample functions for values, math, vectors, outputs, and debugging. Undo/redo, grouping, clipboard operations, and multi-selection are not implemented yet.

## Validation applications

The smoke gallery contains one scene per immediate primitive plus reusable command-list and deferred-submission scenes. Space and Backspace navigate interactively; automatic mode visits every scene sequentially with a fixed animation time.

Automatic mode captures the complete 1920×1080 frame before buffer swap, writes an atomic full-size PNG, and generates a 640×360 thumbnail. The files under `FishGfx/pictures` are used directly by the README gallery.

`VoxelRaycast` provides bounded DDA traversal through positive and negative voxel coordinates. A hit reports the occupied cell, entry-face normal, distance, world position, and adjacent cell used for placement. `VoxelMediumQuery` uses the same floor-based coordinate convention to identify the voxel material containing a camera or other world position.

`FishGfx.VoxelTest` defines a deterministic 1280×1280 terrain spanning logical chunk coordinates −40 through 39, with terrain depth/elevation scaled to approximately −80 through +160. The complete height and priority-flood lake maps are calculated once, while voxel chunks are generated within eight horizontal chunks of the camera and unloaded beyond ten. Each horizontal stream position registers every vertical lighting chunk, including known air, and marks its highest chunk as sky-exposed. Water volumes whose floor and spill elevation cross multiple vertical chunks remain continuous. Rendering is limited to approximately 108 blocks, leaving at least the solver's 15-block propagation radius inside resident terrain. Generation is nearest-first and budgeted to four horizontal positions per frame. An in-memory override map reapplies placed and destroyed blocks after regeneration. Procedural trees are evaluated across chunk borders without forcing neighboring positions to load.

VoxelTest imports a fixed RaylibGame asset snapshot with provenance and license text. Its runtime compatibility texture preserves the native 512², 16×16 cube-tile layout and packs the original barrel, campfire, torch, and foliage texture sheets—with duplicated edge padding—into unused middle rows. The palette mirrors all 22 RaylibGame visual block definitions. A world-space showcase and nine-slot hotbar exercise exact face mappings, custom models, transparency, selection, and placement without accessing RaylibGame at runtime.

The application demonstrates opaque blocks, cutout foliage, glass/water transparency, RGB glowstone/torch/campfire emission, attenuated skylight, GPU water waves with a 0.1-unit amplitude and six-unit wavelength, ambient occlusion, neighbor culling, boundary edits, block raycasting, and material-specific underwater fog. Water crests stop at the original block height so shore waves remain below adjacent land. FishUI owns the Gwen-themed statistics panel and clickable nine-slot hotbar; the crosshair and underwater tint remain direct FishGfx effects. The HUD includes lighting residency/backlog plus rolling FPS and average frame milliseconds over the latest 0.5 seconds. Interactive controls start in captured FPS mode and use Tab to release the cursor for UI input. UI mode blocks camera look and voxel mouse edits, while 1–9 selection remains available. Other controls use WASD and mouse flight, Space/Ctrl vertical motion, Shift acceleration, left click to destroy, right click to place, E fixed boundary editing, and C culling disable/enable. `--auto -debug` waits for lighting and meshing convergence, forces stale revisions and neighbor rebuilds, verifies resident chunk bounds, compiles and draws FishUI, renders normal and underwater frames, and exits without user input.

The test project covers tessellation, UV orientation, rings, rounded rectangles, nine-patch geometry, TTF layout and atlas behavior, FishUI coordinate/UV/color conversion, rooted paths, key/mouse mappings and gated input transitions, voxel coordinates/palettes/meshing/revisions/culling/sorting, priority-flood lake drainage and containment, reflected graph registration/evaluation, JSON persistence, editor models, screenshot filename stability, and public enum compatibility.

## Roadmap and known defects

The prioritized roadmap is maintained in [README.md](README.md#roadmap). Resolved and open defects are maintained in [BUGS.md](BUGS.md).
