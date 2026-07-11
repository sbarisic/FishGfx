# FishGfx Project Information

This document describes the supported modern FishGfx code as verified from the repository source. For build commands and examples, see [README.md](README.md). For resolved and open defects, see [BUGS.md](BUGS.md).

## Supported baseline

The supported configuration is Windows x64 on .NET 10. FishGfx creates an OpenGL core-profile context through its custom GLFW binding and sends OpenGL calls through Silk.NET.OpenGL.

The context negotiator tries OpenGL 4.6 first and falls back one minor version at a time to OpenGL 4.0. Direct State Access is used on OpenGL 4.5 and newer, with bind-to-edit implementations retained for older 4.x drivers.

Current core dependencies are:

- Silk.NET.OpenGL 2.23.0 for OpenGL entry points and types.
- System.Drawing.Common 10.0.0 for the Windows bitmap-loading, readback, and screenshot APIs.
- StbTrueTypeSharp 1.26.12 for TrueType metrics and SDF glyph generation.
- The bundled Windows x64 `glfw3.dll` and managed bindings under `FishGfx/Glfw3`.

Intel RealSense support has been removed. Linux and macOS are not supported by the modern baseline.

## Modern solution

`FishGfx.Modern.sln` is the supported entry point and contains five projects:

- `FishGfx`: the core library.
- `FishGfx.SmokeTest`: interactive primitive gallery, developer console, and automated screenshot validation.
- `FishGfx.NodeEditor`: reflected function-node editor and headless JSON graph runner.
- `FishGfx.VoxelTest`: interactive and automated voxel chunk renderer validation.
- `FishGfx.Tests`: xUnit tests for geometry, fonts, graphs, persistence, UI models, and compatibility mappings.

The older demos, tools, LiteTest, and Nuklear projects are intentionally outside this solution. They remain migration candidates and are not part of modern build acceptance.

## Context and windowing

`RenderWindow` owns the GLFW window and active OpenGL context. It exposes keyboard, character, mouse, scroll, resize, clipboard, cursor, buffer-swap, close, and framebuffer-readback operations.

`Internal_OpenGL` owns the single Silk.NET `GL` instance. It resolves functions through the current GLFW context, records the renderer and actual OpenGL version, configures debug callbacks, and exposes the capability checks used by resource wrappers.

`InputManager` turns window callbacks into frame-coherent held, pressed, and released state. Applications call `BeginNewFrame` before polling events.

## Render state and resource lifetime

`RenderState` describes fixed-function state including depth testing and writes, culling, winding, blending, channel masks, scissoring, and front/back stencil functions and operations. `Gfx.PushRenderState` and `Gfx.PopRenderState` isolate temporary rendering changes.

GPU wrappers include:

- `BufferObject` and `VertexArray`.
- `ShaderStage` and `ShaderProgram`.
- `Texture`, `Framebuffer`, `RenderTexture`, and `Renderbuffer`.
- `OcclusionQuery`.
- Streaming `Mesh2D` and `Mesh3D` drawables.

GPU objects must be created and explicitly disposed on the context thread. If a finalizer observes an undisposed graphics object, deletion is queued through `RenderAPI` and collected during a later context-thread buffer swap or explicit garbage collection.

## Shaders and shared uniforms

FishGfx ships 2D, 3D, line, point, textured/color, and SDF text shaders under `FishGfx/data/shaders`.

`ShaderUniforms.Current` provides the active camera, model transforms, resolution, texture size, clip planes, and related shared values. Shader binding uploads the applicable common uniforms and caches uniform locations.

Standard interleaved vertex attributes are:

- Location 0: position.
- Location 1: color.
- Location 2: texture coordinates.

`VoxelVertex` additionally uses location 3 for its normal and intentionally does not alter the established `Vertex3` layout.

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

Arrays are cloned during command construction, while textures, shaders, and fonts remain caller-owned references. Recording performs no OpenGL work. `Execute` must run on the active context thread and uses the camera, model transform, and shared uniforms active at replay time. Lists are reusable and retain all commands after success or failure; replay stops on the first exception, prevents concurrent mutation or recursive execution, and does not roll back state changed by earlier commands. Callers are responsible for synchronization, live resources, and balanced render-state commands.

`CommandList.Snapshot` creates an immutable `GraphicsCommandBatch`. `DeferredRenderQueue` groups those batches into built-in opaque/transparent or custom render buckets. A submission captures its model matrix, representative world position, layer, sort key, owner tag, and stable sequence while retaining caller ownership of referenced resources. Typed mesh and render-model commands extend deferred submission to retained 3D geometry.

Render passes can query buckets in insertion order or request stable opaque front-to-back, opaque state-first, and transparent back-to-front sorting using camera-space depth. Custom comparers support bounds-aware or application-specific policies. Executing a submission temporarily applies its captured model matrix and restores the previous matrix through `finally`; the camera and remaining shared uniforms stay controlled by the render pass. Queues retain submissions until `BeginFrame` or `Clear` and require external synchronization.

## Voxel rendering

The `FishGfx.Voxels` namespace implements an editable chunk-rendering core with fixed 16³ chunks. `ChunkCoordinate` uses floor division and positive modulo so negative world coordinates map correctly. `VoxelWorld` synchronizes individual reads and edits, maintains monotonically increasing chunk revisions, and invalidates the owning chunk plus relevant Cartesian and diagonal neighbors when boundary voxels change.

`VoxelPaletteBuilder` reserves material ID zero for air and freezes immutable render metadata for worker access. Materials select opaque, alpha-cutout, or transparent rendering; uniform or per-face atlas tiles; tint; face occlusion; and double-sided output. The first mesher emits exposed cube faces rather than greedy quads. It reads an immutable 18³ neighborhood, produces normals and inset atlas UVs, bakes classic three-neighbor ambient occlusion into vertex color, and separates opaque, cutout, and transparent geometry.

`VoxelMeshingScheduler` snapshots on the calling thread and performs CPU meshing on bounded workers. Every job carries the source revision. The context thread accepts only current results and uploads at most the configured budget per update; stale results are discarded and the newest revision is rescheduled. Workers never create or destroy OpenGL resources.

`VoxelRenderer` distance- and frustum-culls chunk AABBs. Opaque chunks enter `RenderBucket.Opaque`, cutout chunks enter `VoxelRenderBuckets.Cutout`, and both retain captured chunk translations. Transparent faces from all visible chunks are transformed to world space, stably sorted by camera-space depth, streamed into one persistent growable `VoxelMesh`, and submitted once with depth writes disabled. Applications execute opaque front-to-back, cutout front-to-back, then transparent back-to-front. The atlas texture is caller-owned; the renderer owns workers, shaders, GPU meshes, and its transparent stream.

## Retained drawables and formats

The core retains higher-level drawables for sprites, tile maps, terrain, parallax backgrounds, and multi-mesh models. `Camera` supports perspective and orthographic projection, orientation vectors, lazy matrix updates, and screen/world conversion.

Model and geometry support includes `GenericMesh`, Wavefront OBJ, Valve SMD loading, and the FishGfx Foam format. SMD saving and some unsupported parser segments remain roadmap items.

Texture APIs support files, `Bitmap`, raw uploads, cubemaps, multisampling, atlas splitting, filters, wrapping, mipmaps, and anisotropy. The bitmap-facing APIs are intentionally Windows-specific in this release.

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

`VoxelRaycast` provides bounded DDA traversal through positive and negative voxel coordinates. A hit reports the occupied cell, entry-face normal, distance, world position, and adjacent cell used for placement.

`FishGfx.VoxelTest` generates a deterministic 128×128 terrain spanning 8×8 horizontal chunks and negative coordinates. A priority-flood pass treats the height-field boundary as drainage outlets, calculates the lowest spill elevation of each depression, and retains connected lakes of at least 24 columns. Water is generated only above solid lakebeds and behind solid banks; submerged grass becomes dirt. Trees and the glass demonstration are placed on dry terrain using height-derived elevations.

The application demonstrates opaque blocks, cutout foliage, glass/water transparency, ambient occlusion, neighbor culling, boundary edits, and block raycasting. Interactive controls use WASD and mouse flight, Space/Ctrl vertical motion, Shift acceleration, left click to destroy, right click to place stone, E fixed boundary editing, and C culling disable/enable. `--auto -debug` forces stale revisions and neighbor rebuilds, waits for accepted GPU meshes, renders all three passes, and exits without user input.

The test project covers tessellation, UV orientation, rings, rounded rectangles, nine-patch geometry, TTF layout and atlas behavior, voxel coordinates/palettes/meshing/revisions/culling/sorting, priority-flood lake drainage and containment, reflected graph registration/evaluation, JSON persistence, editor models, screenshot filename stability, and public enum compatibility.

## Roadmap and known defects

The prioritized roadmap is maintained in [README.md](README.md#roadmap). Resolved and open defects are maintained in [BUGS.md](BUGS.md).
