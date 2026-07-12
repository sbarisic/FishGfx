# FishGfx

FishGfx is a Windows-first C# graphics and game-framework library built on OpenGL 4, GLFW, and Silk.NET. The modern core targets .NET 10 and includes immediate 2D primitives, GPU resource abstractions, bitmap and SDF text, retained drawables, editable voxel chunks, reflected function-node graphs, and interactive validation applications.

- [Architecture and project information](INFO.md)
- [Bug history](BUGS.md)

## Supported configuration

- Windows x64
- .NET 10 SDK
- OpenGL 4.0–4.6 core profile
- Silk.NET.OpenGL 2.23.0
- The bundled native `glfw3.dll`
- `System.Drawing.Common` for the current Windows bitmap APIs

The core tries OpenGL 4.6 first and falls back version-by-version to 4.0. OpenGL 4.5 and newer use Direct State Access where available; older contexts use bind-to-edit fallbacks.

## Build and run

```powershell
dotnet restore FishGfx.Modern.sln
dotnet build FishGfx.Modern.sln -c Debug
dotnet test FishGfx.Modern.sln -c Debug
dotnet run --project FishGfx.SmokeTest/FishGfx.SmokeTest.csproj
dotnet run --project FishGfx.NodeEditor/FishGfx.NodeEditor.csproj
dotnet run --project FishGfx.VoxelTest/FishGfx.VoxelTest.csproj
```

`FishGfx.Modern.sln` contains the supported modern projects:

- `FishGfx`: core rendering, windowing, input, formats, fonts, and node-graph APIs.
- `FishGfx.SmokeTest`: interactive primitive gallery and automated screenshot validation.
- `FishGfx.NodeEditor`: reflected C# function-node editor with evaluation and JSON persistence.
- `FishGfx.VoxelTest`: editable, multi-chunk voxel-rendering validation application.
- `FishGfx.Tests`: context-free geometry, font, node-graph, persistence, and compatibility tests.

The older demos, tools, LiteTest, and Nuklear projects remain outside the modern solution pending separate migrations. Intel RealSense support and its test project have been removed.

## Capabilities

### Rendering and resources

- Automatic OpenGL 4.0–4.6 context creation through the custom GLFW binding.
- Silk.NET-backed shaders, buffers, vertex arrays, textures, framebuffers, render textures, queries, and render state.
- Context-thread GPU creation and deferred destruction of finalizer-released resources.
- Cameras, 2D and 3D meshes, terrain, models, sprites, tile maps, and parallax sprites.
- Alpha blending, depth/cull/color state, scissor regions, stencil functions/operations, and framebuffer depth-stencil attachments.
- Windows bitmap texture loading, readback, and deterministic gallery screenshots.

### Immediate 2D primitives

- Points, thick lines, and line strips.
- Filled, outlined, and textured rectangles.
- Filled, outlined, and textured rounded rectangles with asymmetric corner radii.
- Stretched nine-patch textures with source-pixel borders.
- Filled, outlined, and textured circles and ellipses.
- Filled rings and outlined annular sectors.
- Stroked quadratic and cubic Bézier curves.

All filled tessellated primitives use a single streaming-mesh upload and draw call per shape. Adaptive segment counts are available where appropriate, with explicit overrides for visual testing.

### Typed command lists

`CommandList` records inspectable, immutable `GraphicsCommand` objects and can replay them repeatedly on the active graphics-context thread. Mutable point and vertex arrays are copied at record time. Textures, shaders, and fonts are retained as caller-owned references and must remain valid through every replay. Camera, model transform, and `ShaderUniforms.Current` values are resolved when `Execute` runs.

```csharp
CommandList commands = new CommandList();
commands.RecordPushRenderState(Gfx.PeekRenderState());
commands.RecordFilledCircle(new Vector2(320, 240), 80, Color.CornflowerBlue);
commands.RecordDrawText(font, new Vector2(230, 120), "replay me", Color.White, 32);
commands.RecordPopRenderState();

commands.Execute();
```

Successful execution preserves every command. Replay stops at the first exception and resets `IsExecuting`, but earlier graphics or render-stack changes are not rolled back. Lists do not provide internal synchronization and cannot be mutated or executed recursively during replay.

### Deferred render submission

`DeferredRenderQueue` lets entity code submit immutable command snapshots into opaque, transparent, or application-defined buckets. Each submission captures its model transform, world-space sort position, layer, sort key, and optional owner tag. The render pass can inspect a bucket, apply a built-in camera-depth comparer or its own comparer, and execute the result later:

```csharp
queue.BeginFrame();
queue.SubmitTransparent(
	entity.Commands,
	entity.WorldMatrix,
	entity.BoundsCenter,
	layer: 0,
	sortKey: entity.MaterialKey,
	tag: entity
);

foreach (RenderSubmission item in queue.GetSorted(
	RenderBucket.Transparent,
	RenderSubmissionComparers.TransparentBackToFront(camera)
))
	item.Execute();
```

Opaque front-to-back, opaque state-first, and transparent back-to-front comparers are stable and respect explicit layers. Custom `RenderBucket` values and comparers support passes such as shadows, selection, or overlays. Submission snapshots copy command references, while textures, shaders, fonts, meshes, and models remain caller-owned. The model transform is restored after every item; camera and other shared uniforms are taken from the active render pass.

### Voxel chunks

`FishGfx.Voxels` provides editable 16³ chunks, negative-coordinate-safe world addressing, immutable material palettes, asynchronous culled-face meshing, and immutable custom block models. `MinecraftVoxelModelLoader` converts Blockbench/Minecraft element JSON into atlas-mapped `VoxelModel` geometry; `VoxelModelSet` supports deterministic coordinate-selected variants. Cube and custom geometry are baked into the same worker-generated chunk streams.

```csharp
VoxelPaletteBuilder paletteBuilder = new VoxelPaletteBuilder();
ushort stone = paletteBuilder.Add(
	new VoxelMaterial("Stone", VoxelRenderMode.Opaque, new VoxelFaceTiles(0))
);
VoxelPalette palette = paletteBuilder.Build();

VoxelWorld world = new VoxelWorld();
world.SetVoxel(-1, 0, -1, new VoxelCell(stone));

using VoxelRenderer renderer = new VoxelRenderer(
	world,
	palette,
	atlasTexture,
	new VoxelAtlasLayout(columns: 8, rows: 8, textureWidth: 512, textureHeight: 512)
);

renderer.UpdateMeshing();
queue.BeginFrame();
renderer.SubmitVisible(queue, camera);
queue.Execute(RenderBucket.Opaque, RenderSubmissionComparers.OpaqueFrontToBack(camera));
queue.Execute(VoxelRenderBuckets.Cutout, RenderSubmissionComparers.OpaqueFrontToBack(camera));
queue.Execute(RenderBucket.Transparent, RenderSubmissionComparers.TransparentBackToFront(camera));
```

The renderer owns its worker scheduler, shaders, per-chunk GPU meshes, and global transparent stream. The application retains ownership of the atlas texture and must keep it alive until the renderer is disposed. Opaque and cutout chunks are distance/frustum culled and submitted separately. Transparent faces are gathered across all visible chunks, stably sorted back-to-front in camera space, and uploaded as one world-space stream. Face occlusion, per-face atlas tiles, tint, normals, classic vertex ambient occlusion, alpha cutout, and optional double-sided materials are supported.

`VoxelRenderer.Fog` accepts immutable `VoxelFogSettings` for reusable distance fog and lighting attenuation without recreating voxel meshes. Applications decide which materials are liquid and switch fog as the camera enters or leaves them. The validation app detects its water material, applies blue-green exponential fog and reduced lighting, changes the clear color, and draws a subtle tint below its unaffected HUD.

`VoxelRaycast.Cast` performs bounded voxel-grid traversal and `VoxelMediumQuery` identifies the material containing a world position. `FishGfx.VoxelTest` demonstrates the complete RaylibGame visual block catalog using a copied, attributed asset snapshot: exact cube tiles, per-face grass/wood/crafting mappings, transparent materials, barrel/campfire/torch models, and deterministic foliage variants. The source atlas remains pixel-identical inside a runtime 1024² composite atlas; see `FishGfx/data/textures/voxels/raylibgame/PROVENANCE.md` and its bundled MIT license.

The test world streams a seven-chunk radius in a deterministic 1280×1280 terrain and preserves edits across unloading. Left click destroys, right click places the selected material, the wheel cycles all materials, and 1–9 select the visible hotbar slots. WASD/mouse fly, Space/Ctrl move vertically, Shift accelerates, E edits a fixed boundary voxel, and C toggles culling. The unattended validation mode is:

```powershell
dotnet run --project FishGfx.VoxelTest/FishGfx.VoxelTest.csproj -- --auto -debug
```

### Fonts and console

FishGfx supports binary AngelCode BMFont atlases and scalable SDF text generated from TrueType files:

```csharp
using FishGfx;
using FishGfx.Formats;
using FishGfx.Graphics;

using TTFFont font = new TTFFont("data/fonts/Aaargh.ttf");
Gfx.DrawText(font, new Vector2(100, 100), "Smooth SDF text", Color.White, 64);
```

`TTFFont` preloads printable ASCII and lazily adds Unicode BMP glyphs to a growable atlas. Layout supports multiline text, tabs, pair kerning, scaling, measurement, color, and alpha. Complex shaping, combining-mark handling, right-to-left layout, and supplementary Unicode planes are not supported yet.

The smoke gallery also integrates the tile/text-based developer console. Press F1 to toggle it and use `help` to list gallery commands.

### Function node graphs

Public static methods marked with `[NodeFunction]` become placeable, strongly typed graph nodes. Ordinary parameters become input ports, return values become outputs, and `[NodeBody]` parameters become inline editable values.

```csharp
using FishGfx.NodeGraph;

static class MathNodes
{
	[NodeFunction("Constant", Category = "Values")]
	public static float Constant([NodeBody] float value = 1) => value;

	[NodeFunction(Category = "Math")]
	public static float Add(float a, float b) => a + b;
}

NodeFunctionRegistry registry = new NodeFunctionRegistry();
registry.Register(typeof(MathNodes));
```

Connections require exact CLR type equality. Inputs accept one connection, outputs support fan-out, and evaluation runs in topological order while reporting cycles, invocation errors, and skipped dependents. Named `ValueTuple` returns are expanded into multiple outputs.

Layouts and canvas state can be saved and loaded through `NodeGraphJson`. The bundled editor uses Ctrl+S and Ctrl+O with `node-layout.json` beside the executable. A saved graph can also execute without creating an OpenGL window:

```powershell
dotnet FishGfx.NodeEditor.dll --execute node-layout.json
```

## Automated gallery screenshots

Run the complete primitive gallery unattended with:

```powershell
dotnet run --project FishGfx.SmokeTest/FishGfx.SmokeTest.csproj -- --auto
```

Automatic mode uses a fixed animation time, captures each complete 1920×1080 scene, and atomically overwrites its PNG under `FishGfx/pictures`. It also generates 640×360 documentation thumbnails under `FishGfx/pictures/thumbnails`.

## Roadmap

### Near term

- Complete stencil write-mask support and add focused scissor/stencil gallery scenes and tests.
- Implement SMD saving and define graceful handling for unsupported SMD parser segments.
- Expand OpenGL 4.0 fallback and render-state compatibility coverage.

### Later

- Add advanced text shaping, combining-mark handling, right-to-left layout, and supplementary Unicode support.
- Add a general 2D path/stroke API with configurable joins, caps, arcs, and filled paths.
- Add node-editor undo/redo, grouping, clipboard operations, and multi-selection.
- Add greedy voxel meshing, propagated block lighting, general biome/world generation, collision, and world serialization.
- Replace Windows-only bitmap dependencies as part of broader platform support.

### Deferred migrations

- Migrate the legacy demos, model converter, VectorPFM, LiteTest, and Nuklear integration to .NET 10.
- Re-evaluate retained legacy APIs and remove obsolete compatibility code after those migrations.

## Gallery

The 640×360 thumbnails below include the selected scene in the left-side menu. Select a thumbnail to open its full 1920×1080 capture.

| **Gfx.Line** | **Gfx.Rectangle** | **Gfx.FilledRectangle** |
|:---:|:---:|:---:|
| [![Gfx.Line primitive gallery scene](FishGfx/pictures/thumbnails/gfx-line.png)](FishGfx/pictures/gfx-line.png) | [![Gfx.Rectangle primitive gallery scene](FishGfx/pictures/thumbnails/gfx-rectangle.png)](FishGfx/pictures/gfx-rectangle.png) | [![Gfx.FilledRectangle primitive gallery scene](FishGfx/pictures/thumbnails/gfx-filledrectangle.png)](FishGfx/pictures/gfx-filledrectangle.png) |
| **Gfx.RoundedRectangle** | **Gfx.FilledRoundedRectangle** | **Gfx.LineStrip** |
| [![Gfx.RoundedRectangle primitive gallery scene](FishGfx/pictures/thumbnails/gfx-roundedrectangle.png)](FishGfx/pictures/gfx-roundedrectangle.png) | [![Gfx.FilledRoundedRectangle primitive gallery scene](FishGfx/pictures/thumbnails/gfx-filledroundedrectangle.png)](FishGfx/pictures/gfx-filledroundedrectangle.png) | [![Gfx.LineStrip primitive gallery scene](FishGfx/pictures/thumbnails/gfx-linestrip.png)](FishGfx/pictures/gfx-linestrip.png) |
| **Gfx.Point** | **Gfx.TexturedRectangle** | **Gfx.TexturedRoundedRectangle** |
| [![Gfx.Point primitive gallery scene](FishGfx/pictures/thumbnails/gfx-point.png)](FishGfx/pictures/gfx-point.png) | [![Gfx.TexturedRectangle primitive gallery scene](FishGfx/pictures/thumbnails/gfx-texturedrectangle.png)](FishGfx/pictures/gfx-texturedrectangle.png) | [![Gfx.TexturedRoundedRectangle primitive gallery scene](FishGfx/pictures/thumbnails/gfx-texturedroundedrectangle.png)](FishGfx/pictures/gfx-texturedroundedrectangle.png) |
| **Gfx.TexturedCircle** | **Gfx.TexturedEllipse** | **Gfx.NinePatch** |
| [![Gfx.TexturedCircle primitive gallery scene](FishGfx/pictures/thumbnails/gfx-texturedcircle.png)](FishGfx/pictures/gfx-texturedcircle.png) | [![Gfx.TexturedEllipse primitive gallery scene](FishGfx/pictures/thumbnails/gfx-texturedellipse.png)](FishGfx/pictures/gfx-texturedellipse.png) | [![Gfx.NinePatch primitive gallery scene](FishGfx/pictures/thumbnails/gfx-ninepatch.png)](FishGfx/pictures/gfx-ninepatch.png) |
| **Gfx.Circle** | **Gfx.FilledCircle** | **Gfx.Ring** |
| [![Gfx.Circle primitive gallery scene](FishGfx/pictures/thumbnails/gfx-circle.png)](FishGfx/pictures/gfx-circle.png) | [![Gfx.FilledCircle primitive gallery scene](FishGfx/pictures/thumbnails/gfx-filledcircle.png)](FishGfx/pictures/gfx-filledcircle.png) | [![Gfx.Ring primitive gallery scene](FishGfx/pictures/thumbnails/gfx-ring.png)](FishGfx/pictures/gfx-ring.png) |
| **Gfx.RingLines** | **Gfx.Ellipse** | **Gfx.FilledEllipse** |
| [![Gfx.RingLines primitive gallery scene](FishGfx/pictures/thumbnails/gfx-ringlines.png)](FishGfx/pictures/gfx-ringlines.png) | [![Gfx.Ellipse primitive gallery scene](FishGfx/pictures/thumbnails/gfx-ellipse.png)](FishGfx/pictures/gfx-ellipse.png) | [![Gfx.FilledEllipse primitive gallery scene](FishGfx/pictures/thumbnails/gfx-filledellipse.png)](FishGfx/pictures/gfx-filledellipse.png) |
| **Gfx.QuadraticBezier** | **Gfx.CubicBezier** | **Gfx.DrawText (TTF/SDF)** |
| [![Gfx.QuadraticBezier primitive gallery scene](FishGfx/pictures/thumbnails/gfx-quadraticbezier.png)](FishGfx/pictures/gfx-quadraticbezier.png) | [![Gfx.CubicBezier primitive gallery scene](FishGfx/pictures/thumbnails/gfx-cubicbezier.png)](FishGfx/pictures/gfx-cubicbezier.png) | [![Gfx.DrawText TTF SDF primitive gallery scene](FishGfx/pictures/thumbnails/gfx-drawtext-ttf-sdf.png)](FishGfx/pictures/gfx-drawtext-ttf-sdf.png) |
| **CommandList** | **DeferredRenderQueue** |  |
| [![Typed CommandList gallery scene](FishGfx/pictures/thumbnails/commandlist.png)](FishGfx/pictures/commandlist.png) | [![Deferred render queue gallery scene](FishGfx/pictures/thumbnails/deferredrenderqueue.png)](FishGfx/pictures/deferredrenderqueue.png) |  |
