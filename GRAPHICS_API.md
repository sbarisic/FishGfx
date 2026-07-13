# Graphics API Guide

This document is the contract for FishGfx's supported modern rendering API. `GraphicsContext`, `GraphicsFrame`, and `RenderPass` are the preferred stateful entry points. `Gfx` is the compatibility/immediate facade used underneath pass methods and remains available for migration.

## Context, thread, and frame rules

- `RenderWindow.Graphics` owns the context, capabilities, backbuffer, state/uniform stacks, immediate-renderer caches, and registered GPU resources.
- A context can only be used from the thread that created it. Call `GraphicsContext.MakeCurrent` after switching windows on that thread.
- GPU resources record their owning context. Cross-context binding, copying, attachment, or drawing is rejected.
- Only one `GraphicsFrame` and one nested `RenderPass` may be active per context. A frame may contain multiple sequential passes.
- `GraphicsFrame.Present` performs the buffer swap. Disposing an unpresented frame closes its active pass but does not present it.
- A pass must be disposed after all state, model, view, and query scopes created by it. Scopes are disposed in reverse order.
- The context-owned backbuffer cannot be disposed. A render target cannot be disposed while a pass is using it.

The basic lifetime is:

```csharp
using GraphicsFrame frame = graphics.BeginFrame();
using (RenderPass pass = frame.BeginPass(graphics.Backbuffer, descriptor))
{
	using (pass.PushModel(world))
		pass.Draw(mesh);
}
frame.Present();
```

Pass construction and disposal restore render state, shared uniforms, and render-target bindings even when clear or teardown operations fail. Pass scopes restore their captured values in LIFO order.

## Render passes and state

`RenderPassDescriptor` supplies the `RenderView`, `RenderState`, attachment load actions, clear values, texture size, alpha-test value, and multisample count. A `RenderView` is immutable and captures view/projection matrices, camera position, viewport size, and near/far values.

`RenderState` covers culling, winding, depth test/write/function, RGBA write masks, blending, point size, scissor state, depth clamp, and independent front/back stencil functions, operations, references, masks, and write masks. Invalid point sizes, enum values, and scissor rectangles are rejected before state is pushed. The context's base state cannot be popped.

Prefer scoped pass methods:

```csharp
RenderState overlay = descriptor.State;
overlay.EnableDepthTest = false;
using (pass.PushState(overlay))
using (pass.PushView(hudView))
{
	pass.FilledRectangle(8, 8, 240, 48, new Color(0, 0, 0, 180));
}
```

`Gfx.PushRenderState`/`PopRenderState`, direct `ShaderUniforms` mutation, and `RenderTexture.Push`/`Pop` are compatibility APIs. They are context-aware but require manual balancing.

## Resource ownership and disposal

Create modern resources through `GraphicsContext` factories. `Texture`, `GraphicsBuffer`, `VertexArray`, `Framebuffer`, `Renderbuffer`, shaders, queries, fonts, render targets, and retained meshes are disposable.

Disposal is idempotent. It invalidates the managed object immediately and queues its OpenGL deletion for the owner thread. `BeginFrame`, `Present`, `CollectGarbage`, and context shutdown drain that queue. Disposing the context disposes all still-registered resources. Explicit disposal remains recommended for predictable memory use.

Containers document ownership as follows:

- `RenderTarget` owns its offscreen `RenderTexture`; the backbuffer is context-owned.
- `RenderTexture` owns its framebuffer and attachment textures.
- `Mesh2D`, `Mesh3D`, and `VoxelMesh` own their vertex arrays and buffers.
- `Sprite`, `Tilemap`, `Terrain`, and `RenderModel` own their internal meshes, but not assigned shaders or textures.
- `VoxelRenderer` owns workers, shaders, and generated meshes, but not its atlas texture.
- Commands, command batches, submissions, and queues never own referenced GPU resources.

## Buffers

`GraphicsBufferDescriptor` contains a positive byte size, one or more `BufferBindFlags`, and a `BufferUsage` hint. Supported bindings are vertex, index, uniform, storage, transfer source, and transfer destination.

```csharp
using GraphicsBuffer vertices = graphics.CreateBuffer<Vertex3>(
	vertexData,
	BufferBindFlags.Vertex | BufferBindFlags.TransferSource,
	BufferUsage.Static
);
```

- `Write<T>` uploads a tightly packed unmanaged span at a checked byte offset.
- `ResizeDiscard` reallocates storage and discards all previous contents.
- `CopyTo` supports checked same-context GPU copies. The source needs `TransferSource`; the destination needs `TransferDestination`.
- A buffer attached as vertex or index data must include the corresponding bind flag.
- CPU mapping, readback, persistent/coherent storage, and asynchronous transfer are not implemented.

## Vertex arrays and meshes

`VertexArray.BindVertexBuffer` returns a binding index. Attribute format and attribute-to-binding selection are explicit. Passing `null` unbinds that binding. `BindElementBuffer(null)` removes indexed drawing. Draws validate counts, index-buffer byte bounds, disposed dependencies, and resource ownership.

The OpenGL 4.0 fallback temporarily binds the vertex array and restores the caller's previous binding. `Mesh3D` safely supports switching between packed `Vertex3` uploads and separate position/color/UV uploads: it reapplies the correct stride and relative offsets on every layout change. Empty index uploads return the mesh to non-indexed drawing. Mesh default color applies whenever the color attribute is disabled.

## Textures

`TextureDescriptor` contains width, height, `TextureFormat`, `TextureUsageFlags`, dimension, mip count, sample count, fixed-sample-location selection, and initial sampling state.

- Dimensions are 2D, cube, or 2D multisample. Cubes must be square.
- Non-multisample textures use one sample. Multisample textures require at least two samples, exactly one mip, and no transfer usage.
- Color and depth/stencil formats cannot be assigned incompatible attachment usage.
- Sampling supports nearest/linear mip filters, wrap modes, and capability-checked anisotropy. Magnification is nearest or linear only.
- `Write<T>` accepts tightly packed unmanaged data with an explicit `TextureDataFormat`. Regions and mip/face subresources are bounds checked.
- `GenerateMipmaps` is explicit and requires allocated mip levels.
- Whole `CopyTo` requires identical dimension, extent, format, and mip count and copies every mip and all six cube faces. Region copies may select individual 2D or cube subresources.
- Copies require `TransferSource` and `TransferDestination`, must stay within one context, and do not support multisample textures. Use `Framebuffer.Blit` to copy or resolve multisample attachments.

The OpenGL 4.0 paths restore texture, framebuffer, pixel-unpack, and other temporary bindings after edits or copies.

## Image and cubemap loading

`TextureLoader` contains the Windows `System.Drawing` bridge. File/image loading creates sampled, transfer-destination textures; atlas loading requires dimensions exactly divisible by the tile size. Temporary images and partially created textures are disposed after failures, and atlas pixels are cloned exactly rather than resampled.

`TextureLoadOptions.FlipY` defaults to `true`, matching bottom-left OpenGL texture coordinates. Set it to `false` to keep the source image's top-down row order. `MipLevels` allocates and generates the requested chain.

`CubemapPaths` accepts `(left, front, right, back, bottom, top)`. These map to cube faces as follows:

| Cube face | Source |
|:---|:---|
| Positive X | right |
| Negative X | left |
| Positive Y | top |
| Negative Y | bottom |
| Positive Z | front |
| Negative Z | back |

All six images must be square and identically sized.

## Framebuffers and render targets

Framebuffer attachments must belong to the current context, have compatible usage and format families, and share identical width, height, and exact sample count. Depth-stencil attachment points require stencil-capable formats. Renderbuffers must have allocated storage before attachment.

`Framebuffer.DrawBuffers` takes unique, nonnegative color attachment indices. An empty list configures a depth-only target. `Clear` supports selected color, depth, and stencil values. `Blit` preserves the caller's read/draw framebuffer bindings, uses nearest filtering for depth/stencil, and uses the actual backbuffer size when the destination is `null`.

`RenderTargetDescriptor` creates ordinary or G-buffer targets. Samples are zero for single-sampled rendering or at least two for MSAA. A non-G-buffer target must request color, depth-stencil, or both.

## Commands and deferred submission

`CommandList` records mutable, inspectable commands without OpenGL work. `Snapshot` creates an immutable `GraphicsCommandBatch`. Recorded arrays are copied; referenced textures, shaders, fonts, meshes, and models remain caller-owned.

Execute lists and batches through an active pass. Built-in render-state push/pop commands must be balanced. Replay validates the balance before drawing and unwinds any pushed state if a later command throws. Mutation and recursive replay are rejected.

`DeferredRenderQueue` captures batches with a model matrix, sort position, layer, 64-bit sort key, tag, and stable sequence. Built-in comparers support opaque front-to-back, opaque state-then-front-to-back, and transparent back-to-front sorting from either a captured `RenderView` or `Camera`. Prefer the `RenderView` overload so sorting cannot observe later mutable camera changes.

## Compatibility and currently deferred features

The supported source/build boundary is `FishGfx.Modern.sln`. Older demos and integrations outside it still reference removed texture constructors/static helpers and require a separate migration.

The modern rendering layer intentionally does not include a scene graph, entities/components, physics, navigation, audio, networking, asset database, animation system, material graph, or gameplay loop. Rendering features not yet present include general buffer/texture readback, buffer mapping, persistent buffers, sampler objects, texture arrays and views, compressed texture upload, pixel-buffer objects, asynchronous transfers, and a general path/stroke tessellator.

See [README.md](README.md) for examples and validation commands, [INFO.md](INFO.md) for project architecture, and [BUGS.md](BUGS.md) for the correctness history.
