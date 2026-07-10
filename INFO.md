# FishGfx Project Information

> Consolidated from the authenticated Devin Wiki for `sbarisic/FishGfx`, branch `master`, on 2026-07-10. Wiki claims are generated documentation and should be verified against source before making critical changes.

## Project summary

FishGfx is a managed C# graphics abstraction layer and 2D/3D game framework built over OpenGL 4.x and GLFW. It is presented as a modern, more capable alternative to SFML. The library combines rendering, GPU resource management, window/input handling, asset loading, cameras and spatial math, common drawable objects, application lifecycle management, GUI integration, demo games, and optional hardware support.

The core library targets .NET Framework 4.6.1. Other projects target framework versions from 4.6.2 through 4.8.1. Both x86 and x64 configurations exist; AnyCPU configurations in the main library default to x64.

Primary technologies:

- C# and .NET Framework
- OpenGL 4.0–4.6 through OpenGL.Net
- GLFW 3 through custom C# bindings
- System.Numerics for vectors, matrices, and quaternions
- System.Drawing for bitmap-backed texture loading
- Newtonsoft.Json for configuration and level data
- NuklearDotNet for immediate-mode GUI
- Humper for AABB-based 2D physics
- Intel RealSense SDK for optional depth-camera support

## Repository and solutions

- `FishGfx/`: core graphics and game-framework library.
- `FishGfx/Graphics/`: OpenGL initialization, rendering state, shaders, textures, buffers, cameras, framebuffers, meshes, and drawables.
- `FishGfx/Glfw3/`: managed GLFW bindings.
- `FishGfx/Formats/`: OBJ, SMD, Foam, generic mesh, and BMFont handling.
- `FishGfx/data/`: built-in shaders, models, fonts, and other engine assets.
- `FishGfx_Nuklear/`: FishGfx rendering/input backend for Nuklear.
- `Test/`: complete 2D side-scrolling demo with levels, entities, particles, and Humper physics.
- `Test2/`: validation/demo project for 3D and parallax features.
- `Test_Nuklear/`: Nuklear integration demo.
- `FishGfx_LiteTest/`: minimal-overhead test configuration.
- `RealSenseTest/`: visualization and validation for RealSense depth/point-cloud data.
- `ModelConv/`: command-line 3D model conversion tool.
- `VectorPFM/`: auxiliary low-level/vector experimentation tool.
- `BMFont/`: bundled font assets/tooling.
- `thirdparty/`: external managed and native dependencies.
- `submodule_NuklearDotNet/`: NuklearDotNet submodule.

Solutions:

- `FishGfx.sln`: primary solution containing the core library, Test game, ModelConv, and Nuklear integrations.
- `FishGfx_Only.sln`: reduced solution containing FishGfx and the primary Test project.
- `FishGfx_LiteTest/FishGfx_LiteTest.sln`: minimal test solution.

Build output is directed to folders including `bin/` and `bin_fishgfx/`; intermediate files go under `obj/`.

## Application lifecycle

`FishGfxGame` is the abstract application framework. Applications implement the high-level `Init`, `Update`, and `Draw` lifecycle while the base class handles:

1. GLFW/OpenGL window creation.
2. `InputManager` initialization.
3. A default orthographic camera in `ShaderUniforms.Current`.
4. shared resource creation and application initialization.
5. the continuous update/draw loop.
6. buffer swapping, event polling, time tracking, and orderly resource cleanup.

The main rendering sequence is conceptually:

`Run → InitGLFW → create context → SetupOpenGL → Init → [Update(dt) → Draw(dt) → SwapBuffers]`

`InputManager.BeginNewFrame` clears one-frame pressed/released state at the start of each frame. The framework maintains both variable frame delta and accumulated game time.

## Core graphics architecture

The graphics layer provides a managed abstraction over the OpenGL state machine. High-level calls flow through the static `Gfx` API, `RenderAPI`, and GPU wrapper objects before reaching OpenGL.Net.

### OpenGL initialization

`Internal_OpenGL` manages a strict three-phase sequence:

1. `InitGLFW` loads GLFW and installs an error callback.
2. `RenderWindow` creates a core-profile context, trying OpenGL 4.6 and falling back version-by-version to 4.0.
3. `SetupOpenGL` initializes OpenGL.Net entry points, queries the actual version and extensions, records renderer information, and enables diagnostics.

`Is45OrAbove` controls use of Direct State Access features. `OpenGL_BODGES` contains driver-specific workarounds, including an Intel texture-binding workaround.

Debug builds can enable synchronous OpenGL debug callbacks. Passing `-debug` enables API logging to `opengl_log.txt`.

### Render state and framebuffers

`RenderState` collects fixed-function pipeline configuration:

- depth test, depth function, and depth mask
- blending and source/destination factors
- face culling and winding
- stencil functions and operations
- scissor regions
- per-channel color masks

`Gfx.PushRenderState` and `Gfx.PopRenderState` form a stack so temporary passes—such as UI rendering—can change state and reliably restore the prior configuration.

`FramebufferObject` wraps OpenGL FBOs. `RenderTexture` combines framebuffer attachments and textures for off-screen rendering, post-processing, multisampling, or intermediate passes. Clear operations can target color, depth, and stencil buffers.

### GPU resource lifetime

`GraphicsObject` is the base for GPU-backed wrappers and standardizes binding and disposal. OpenGL objects cannot safely be deleted on the garbage-collector thread, so FishGfx queues finalizer-triggered destruction in `RenderAPI.GCQueue` and processes deletion on the graphics-context thread.

Managed GPU resources include:

- `BufferObject` for vertex/index data
- `VertexArray` for attribute layouts
- `ShaderStage` and `ShaderProgram`
- `Texture`
- framebuffer objects
- occlusion queries

Modern Direct State Access calls are used when possible, with bind-to-edit fallbacks for older 4.x contexts.

## Shaders, vertices, and drawing

`ShaderProgram` compiles and links one or more `ShaderStage` objects. Shader source may be provided as text or loaded from disk. Uniform locations are cached in a dictionary; relinking clears and rebuilds the cache.

`ShaderUniforms` is a stack-based global rendering context. Binding a shader uploads common values such as:

- view, projection, and model matrices
- near/far clip planes
- viewport and resolution
- camera position
- alpha-test and multisample parameters
- texture size

Built-in shaders live under `FishGfx/data/shaders/`. Standard vertex attribute locations are:

- location 0: position
- location 1: color
- location 2: UV

`Vertex2` and `Vertex3` use sequential, interleaved layouts suitable for direct GPU upload. `Vertex2.CreateQuad` generates a two-triangle rectangle. `Color` is a packed four-byte RGBA structure with integer packing, normalized vector conversions, and predefined colors.

The static `Gfx` class exposes immediate-style helpers:

- initialization for dedicated 2D and 3D meshes/shaders
- state-stack operations
- 2D rectangles and textured primitives
- 3D points and lines
- buffer clearing
- spatial/debug drawing helpers

Its default state enables back-face culling, depth testing, and source-alpha blending.

## Textures and assets

`Texture` wraps 2D, multisampled, and cubemap textures. It prefers immutable storage and DSA, with legacy fallbacks. Factory methods support:

- files and `Bitmap` instances
- six-face cubemaps
- atlas splitting into individual textures
- configurable filters, wraps, and anisotropy

### Geometry formats

`GenericMesh` is the intermediate representation between parsers and GPU-side meshes. It stores `Vertex3` data plus a material name and supports bounding calculations, Y/Z coordinate conversion, and winding reversal.

Supported formats include:

- Wavefront OBJ, including material-delimited meshes
- Valve SMD
- FishGfx Foam

`RenderModel` converts one or more generic meshes into renderable submeshes/material associations. `ModelConv` provides standalone conversion between supported representations.

### Fonts

`GfxFont` defines text measurement and layout independently of the backing format. It models source glyph metrics with `CharOrigin` and positioned glyphs with `CharDest`. Layout supports scaling, newlines, tabs, baseline normalization, and measurement.

`BMFont` implements binary AngelCode BMFont v3 parsing and loads texture pages into FishGfx textures for batched text rendering.

## Camera and spatial math

`Camera` supports perspective, off-center perspective, and orthographic projections. Position and quaternion rotation mark the camera dirty; view/world matrices and orientation vectors are recomputed lazily when requested.

It also supports:

- Euler-angle orientation updates
- forward/right/up vectors
- world-to-screen conversion
- screen-to-world conversion
- integration with the global shader-uniform stack

Spatial primitives include:

- `AABB`: collision, containment, adjacency, intersection, union, and point-cloud bounds.
- `BoundSphere`: spherical bounds.
- `BoundingBox`: oriented/corner-based box representation.
- `GfxUtils`: vector swizzles, component access, rotation conversion, randomization, and serialization helpers.

## High-level drawables

Complex drawable types implement `IDrawable.Draw()` and are built over meshes, textures, shaders, and the model-matrix stack.

- `Sprite`: textured quad with configurable pivot/center.
- `Tilemap`: atlas-indexed grid rebuilt only when dirty and rendered as one batched mesh.
- `Mesh2D` / `Mesh3D`: VAO/VBO/EBO management and drawing.
- `Terrain`: heightmap-derived mesh generation and texturing.
- `RenderModel`: multi-submesh, multi-material model rendering.
- `ParallaxSprite`: scaled multilayer backgrounds with camera-relative motion and seamless horizontal tiling.
- `DevConsole`: in-game command entry, logging, and debug interaction.

## Input and windowing

`RenderWindow` owns the GLFW window/context and exposes:

- buffer swapping and close state
- resize, keyboard, character, and mouse events
- cursor visibility and capture
- clipboard and monitor/window services

`InputManager` translates window events into frame-coherent state:

- held, pressed-this-frame, and released-this-frame keys
- raw and normalized mouse coordinates
- keyboard/mouse query methods

`MicroConfig` provides lightweight serialization/deserialization for primitive settings and enums.

## Test game and physics

The `Test` project is a complete 2D side-scrolling demonstration. `TestGame` loads JSON levels, configures 2D render state, initializes Humper physics, merges collision tiles, spawns entities, updates logic, and draws layered world/UI content.

The rendering order separates background/parallax, tile layers, entities, particles, foreground, debug overlays, and camera-space UI. Level data is compatible with the bundled OgmoEditor workflow.

Entity hierarchy:

- `Entity`: spawn, update, and draw lifecycle.
- `Pawn`: velocity, grounded state, acceleration, gravity, friction, and physics box.
- `Player`: player-specific input and behavior.
- `LevelEntity`: entities instantiated from level metadata.

`SpriteAnimation` and `SpriteAnimator` manage frame sequences and playback. The particle system supplies transient effects such as fire and explosions.

Humper is a spatial-hash AABB engine supporting collision queries and responses such as sliding and bouncing. Tags are bitmasks used to filter interaction categories. The demo includes an AABB-merging optimization to reduce the number of static collision boxes and debug drawing to inspect physics state.

Test utilities include array helpers, vector distance/rounding functions, Bresenham line enumeration, JSON level schemas, and animation helpers.

## Nuklear GUI integration

`FishGfx_Nuklear` bridges NuklearDotNet to FishGfx.

`FishGfxDevice` implements the rendering backend using a streaming `Mesh2D`. It:

- converts Nuklear vertices and draw commands into FishGfx geometry
- maps coordinate systems and flips Y where necessary
- applies clipping through scissor state
- manages GUI textures
- pushes/pops FishGfx render state so UI rendering does not leak state

Input hooks translate `RenderWindow` mouse, key, modifier, and text events into Nuklear events. `EventsEnabled` can disable GUI event consumption. `Test_Nuklear` demonstrates initialization and per-frame usage.

## RealSense and auxiliary tools

`RealSenseCamera` wraps Intel RealSense context, pipeline, configuration, and sensor options. It supports depth, color, infrared, and pose streams; resolution enumeration; exposure/laser configuration; frame delegates; and point-cloud conversion to FishGfx `Vertex3` data.

`RealSenseTest` visualizes streamed 3D data and provides debugging controls.

`ModelConv` converts supported 3D model formats through the generic-mesh pipeline. `VectorPFM` is a separate experimental/auxiliary project for low-level vector or graphics API work.

## Practical requirements and caveats

- Use a Windows development environment capable of building legacy .NET Framework projects.
- Install the framework targeting packs required by the selected projects (4.6.1 through 4.8.1).
- Keep native binaries aligned with the selected x86/x64 build.
- A GPU/driver supporting at least OpenGL 4.0 is required.
- OpenGL 4.5+ enables the preferred Direct State Access paths.
- RealSense features require compatible hardware and the Intel native/managed runtime.
- Initialize and dispose GPU resources on the graphics-context thread; rely on the provided deferred-disposal path when finalizers are involved.
- Call the per-frame input reset at the correct point in the game loop.
- Treat OpenGL line-width behavior as driver-dependent.
- The code contains explicit vendor workarounds, signaling that driver-specific behavior should be tested on target hardware.

## Devin Wiki topic index

The authenticated wiki contains these documented areas:

1. FishGfx Overview
   - Getting Started & Project Structure
   - FishGfxGame Application Lifecycle
2. Core Graphics Architecture
   - OpenGL Initialization & Context Management
   - Render State & Framebuffer System
   - GPU Resource Management
3. Shaders & Rendering Primitives
   - ShaderProgram & ShaderUniforms
   - Vertex Types & Color
   - Gfx Drawing API
4. Textures & Asset Loading
   - Texture System
   - 3D Model Formats (OBJ, SMD, Foam, GenericMesh)
   - Font Rendering (BMFont & GfxFont)
5. Camera & Spatial Math
   - Camera System
   - Spatial Primitives (AABB, BoundSphere, BoundingBox)
6. Drawables & Advanced Graphics
   - Tilemap & Sprite
   - Terrain & RenderModel
   - ParallaxSprite & DevConsole
7. Input & Windowing
   - RenderWindow & GLFW Bindings
   - InputManager & MicroConfig
8. Test Game Demo
   - Game Loop, Level Loading & Rendering Pipeline
   - Entity System & Particle Effects
   - Physics (Humper AABB Engine)
   - Utility Helpers (Utils & Level Data)
9. Nuklear GUI Integration
   - FishGfxDevice Rendering Backend
   - Nuklear Input & Event Mapping
10. Hardware Integration & Auxiliary Tools
    - Intel RealSense Camera Integration
    - Auxiliary Tools (ModelConv & VectorPFM)
11. Glossary

## Source links

- Repository: <https://github.com/sbarisic/FishGfx>
- Authenticated Devin Wiki: <https://app.devin.ai/org/sbarisic/wiki/sbarisic/FishGfx?branch=master>
- Documented wiki revision referenced source commit `f634fc69`.

### Access note

The Devin MCP correctly lists `sbarisic/FishGfx` as an available repository, but its `read_wiki_structure` and `read_wiki_contents` operations currently return a false “repository not found” response. The authenticated Devin web application exposes the complete wiki, so this document was extracted from that authenticated wiki UI.
