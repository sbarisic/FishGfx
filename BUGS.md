# Bug History

This document tracks defects found during the .NET 10 migration and source review. Feature work and known limitations belong in the [README roadmap](README.md#roadmap).

## Open issues

No open defects are currently recorded here. This does not imply that the project is defect-free; newly confirmed bugs should receive the next `BUG-NNN` identifier and include a reproduction, impact, and verification plan.

## Resolved issues

| ID | Severity | Resolution | Regression coverage |
|:---|:---:|:---|:---|
| BUG-001 | Medium | Initialize 2D shaders before selecting the text shader. | Automatic text-first graphics preflight. |
| BUG-002 | Medium | Reject connections whose ports are not owned by the target graph. | `ConnectionsRejectForeignPortsWithoutReplacingExistingInput`. |
| BUG-003 | Low | Handle controls before glyph lookup and reset kerning continuity at tabs. | `LayoutHandlesControlsBeforeGlyphLookupAndBreaksTabKerning`. |
| BUG-004 | Low | Restore temporary font scale through `finally`. | `DrawTextRestoresScaleWhenAtlasPreparationFails`. |
| BUG-005 | Low | Guard glyph-specific debug drawing when layout is empty. | Automatic control-only debug-text preflight. |
| BUG-006 | Low | Return zero before scanning an empty TTF glyph bitmap. | `PreloadsAsciiAndLazilyAddsBmpGlyphs`. |
| BUG-007 | Low | Initialize every node-port ID with a unique GUID. | `NodesUseExactTypesReplaceInputsAndFanOut`. |
| BUG-008 | Low | Return a structured load failure for null JSON content. | `NullJsonReturnsStructuredFailure`. |
| BUG-009 | Medium | Preserve floating-point texture parameters on the pre-4.5 OpenGL path. | Typed code path, Debug/Release builds, and smoke validation. |
| BUG-010 | Medium | Correct 3D AABB center, bounds, maximum, union, and overlap calculations. | `AabbUsesThreeDimensionalSizeAndUnion` and `FrustumRejectsDistantBounds`. |
| BUG-011 | Medium | Apply RaylibGame's distinct UV corner orientation to every cube face. | `CubeFacesUseRaylibGameCornerOrientations`. |
| BUG-012 | Low | Parse and apply supported Blockbench/Minecraft per-face UV rotations. | `MinecraftLoaderAppliesFaceRotationsAndPreservesReversedUvEndpoints`. |
| BUG-013 | Medium | Correct the mirrored south face in imported Blockbench voxel models. | `MinecraftLoaderUsesBlockbenchUvOrientationForEveryDirection`. |
| BUG-014 | Medium | Preserve exact RGBA values while packing custom-model textures. | `PackedModelRegionsPreserveCompleteAlphaMasksAndPadding`. |
| BUG-015 | Medium | Emit camera-facing back triangles for exposed water boundaries. | `DoubleSidedMaterialsEmitReversedTriangles` and `DoubleSidedWaterVolumeOnlyDoublesExposedBoundaryGeometry`. |
| BUG-016 | Low | Select palette colors by squared RGBA distance with deterministic first-tie behavior. | `PaletteClampUsesRgbaDistanceAndFirstTie`. |
| BUG-017 | Medium | Normalize quaternion input and recover pitch, yaw, and roll consistently. | `QuaternionEulerConversionIncludesRoll`. |
| BUG-018 | High | Correct camera projection/unprojection matrix order, homogeneous division, viewport conversion, and picking rays. | `CameraProjectsAndUnprojectsViewportCoordinates` and `PerspectivePickingRayPointsThroughViewportCenter`. |
| BUG-019 | Medium | Apply complete render-state deltas, including independent stencil write masks. | `DefaultPassStateIncludesCompleteStencilWriteMasks`, builds, and graphics smoke validation. |
| BUG-020 | Medium | Apply custom pass depth/stencil clear values during the requested clear rather than afterward. | Pass descriptor path, builds, and graphics smoke validation. |
| BUG-021 | High | Move immediate-renderer GPU caches and mutable graphics stacks from process-global storage to their owning context. | Multi-context ownership checks, builds, and graphics smoke validation. |
| BUG-022 | Medium | Suppress transparent voxel faces against fully occluding neighbors to prevent coplanar water/terrain geometry. | `TransparentBlocksDoNotEmitCoplanarFacesAgainstOccludingNeighbors` and `TransparentBlocksRetainFacesBehindNonOccludingCutouts`. |
| BUG-023 | High | Reapply vertex stride/offset layouts when meshes switch between packed and separate uploads, and detach empty index data. | OpenGL 4.0/4.6 resource preflight and gallery validation. |
| BUG-024 | High | Preserve caller VAO, framebuffer, renderbuffer, texture, pixel-store, and copy bindings in all bind-to-edit fallbacks. | Exact OpenGL 4.0 gallery/resource preflight. |
| BUG-025 | High | Make render-pass creation/teardown and immediate helper bindings exception-safe; validate and contain recorded state stacks. | `SnapshotsRejectUnbalancedRenderStateCommands`, command failure tests, and graphics smoke validation. |
| BUG-026 | Medium | Copy every mip and cube face during whole-texture copies and use conventional cubemap Y-face mapping. | OpenGL 4.0/4.6 resource preflight and documented face mapping. |
| BUG-027 | High | Validate framebuffer attachment dimensions, exact sample counts, usage/format families, and allocated renderbuffer storage. | Debug framebuffer completeness checks and OpenGL 4.0/4.6 gallery validation. |
| BUG-028 | Medium | Make disposal idempotent across finalizer/explicit races and add deterministic mesh ownership to retained drawables. | Unit suite, context shutdown, and all automated applications. |
| BUG-029 | Medium | Clone loader pixels without GDI+ resampling and make `FlipY` semantics explicit while preserving the default OpenGL orientation. | Exact atlas pixel tests, gallery validation, and API documentation. |

## Resolution notes

### BUG-001: Text shader initialization order

`Gfx.DrawText` previously selected `Default2D` or `SdfText2D` before `Init2D` had guaranteed their creation. Text can now be the first 2D operation after context setup. Automatic gallery mode performs that exact preflight before drawing another primitive.

### BUG-002: Cross-graph connections

`FunctionNodeGraph.Connect` previously accepted compatible ports from nodes owned by another graph. It now returns `null` before changing an occupied input unless both endpoint nodes belong to the target graph. This matches the existing incompatible-type convention.

### BUG-003: Control-character kerning

`GfxFont.LayoutString` previously looked up and kerned a glyph before recognizing tabs and line controls. Carriage returns, newlines, and tabs are now processed first. A tab advances by `TabSize` and clears the previous character so kerning does not cross the tab boundary.

### BUG-004: Font scale restoration

An exception during atlas preparation or rendering could leave `ScaledFontSize` at a temporary draw size. The complete temporary-size operation is now protected by `try/finally`.

### BUG-005: Empty debug layout

A non-empty input containing only controls produces no glyph geometry. Debug drawing now renders its origin marker safely and only accesses glyph positions or glyph bounds when at least one glyph exists.

### BUG-006: Empty TTF glyph border scan

The internal SDF-border diagnostic now returns zero when width, height, or bitmap storage is empty instead of calculating an invalid edge index.

### BUG-007: Empty node-port IDs

`NodePort.Id` now initializes with `Guid.NewGuid()`. Port IDs are non-empty and unique for newly constructed or reconstructed nodes; JSON persistence continues to identify reconstructed ports by node ID and descriptor index.

### BUG-008: Null JSON handling

`NodeGraphJson.Deserialize(null, registry)` now returns an unsuccessful `NodeGraphLoadResult` containing an actionable diagnostic. A null registry remains an argument error because the registry is required to resolve safe callable functions.

### BUG-009: Truncated fallback texture parameters

The legacy bind-to-edit branch of `Texture.TextureParam` converted a boxed floating-point value to `int` before calling `glTexParameter`, truncating values such as anisotropy. The fallback now calls the floating-point overload and preserves the requested value.

### BUG-010: Incorrect 3D AABB operations

`AABB.Bounds` returned the absolute maximum rather than the box size, `Center` added the full size instead of half, and `Maxs` mixed incorrect center/bounds values. Union delegated to a two-dimensional `System.Drawing` rectangle and lost the Z extent, while collision could report intersection when only selected corners overlapped. The implementation now uses component-wise three-dimensional minima/maxima and interval overlap on all axes. Frustum culling and voxel chunk bounds rely on these corrected operations.

### BUG-011: Uniform cube-face UV orientation

The voxel mesher reused the positive-X UV corner order for every cube face. The bottom, positive-Z, and negative-Z faces use different vertex orders, so asymmetric textures were flipped or rotated and grass side bands could appear vertically. Each face definition now stores the source-image UV associated with each geometry corner while retaining the existing half-texel atlas inset.

### BUG-012: Ignored custom-model face rotation

The Minecraft/Blockbench model loader honored element rotations but ignored a face's optional UV `rotation`. It now accepts 0, 90, 180, and 270 degrees, rotates UVs around the face center, and rejects unsupported values. Reversed UV rectangle endpoints remain supported, while coordinates outside the logical 0..16 model texture region are rejected before they can sample another atlas region.

### BUG-013: Mirrored Blockbench south faces

The custom-model loader inherited RaylibGame's south-face UV corner order, which horizontally mirrored that face relative to the Blockbench/Minecraft convention used to author the imported barrel, campfire, torch, and foliage models. The south mapping now follows the source convention while all other face mappings, top-down atlas conversion, element pivots, and reversed UV rectangles remain unchanged.

### BUG-014: Altered model-texture alpha during atlas composition

The compatibility-atlas builder used `Graphics.DrawImageUnscaled` to copy model sheets. GDI+ color conversion changed some source channel and alpha values, turning fully opaque or transparent cutout texels into partial coverage. Atlas composition now copies exact pixels and duplicates exact edge pixels into the padding, preserving the campfire and torch alpha masks.

### BUG-015: Missing underwater-facing water surface

Water emitted only outward-facing triangles while the transparent voxel pass kept back-face culling enabled. The lake surface therefore disappeared when viewed from below. Water is now a double-sided material: exposed boundaries include reversed triangles, shared faces between adjacent water voxels remain omitted, and culling still ensures only the camera-facing copy contributes to blending.

### BUG-016: Incorrect palette clamp metric

Palette clamping mixed channel comparisons and did not define tie behavior. It now minimizes squared straight-RGBA distance, returns the first equal-distance entry, and rejects null or empty palettes.

### BUG-017: Incomplete quaternion-to-Euler conversion

Euler extraction omitted roll and behaved unpredictably for non-unit or zero quaternions. Inputs are normalized, zero quaternions are rejected, and the result matches the yaw-pitch-roll convention used by `System.Numerics`.

### BUG-018: Incorrect camera screen/world conversion

The previous conversion mixed matrix order and screen coordinate conventions and did not perform a complete homogeneous divide. Projection and unprojection now use viewport coordinates consistently, expose failure for non-invertible transforms, and generate normalized near-to-far picking rays.

### BUG-019: Incomplete fixed-function state restoration

The render-state path did not compare or restore every stencil field and had no independent write masks. The state descriptor and delta application now cover front/back masks, functions, references, operations, scissor state, color/depth masks, blending, culling, winding, and point size per context.

### BUG-020: Late custom depth/stencil clear values

A pass using non-default depth or stencil clear values cleared once with defaults and then issued a second clear. Pass creation now sets all requested values and performs one combined buffer clear.

### BUG-021: Process-global immediate renderer resources

Built-in shaders, streaming meshes, the white texture, state stacks, uniform stacks, render-target stacks, and active queries could leak across windows. These caches now live on `GraphicsContext`; resources retain their owner and deferred deletion returns to that context.

### BUG-022: Coplanar water and terrain faces

Transparent voxels emitted a boundary face whenever the neighboring material ID differed. Opaque terrain also emitted its outward face against non-occluding water, placing water and terrain triangles on the same plane and causing visible depth instability while submerged. Transparent faces are now suppressed when the neighbor fully occludes faces. Non-occluding cutout neighbors retain the transparent face so discarded texels still reveal the water boundary.

### BUG-023: Stale mesh vertex layouts

`Mesh3D` reused buffer objects when moving between packed `Vertex3` uploads and separate position/color/UV arrays, but the vertex-array binding retained the previous stride and relative offsets. The mesh now reapplies binding and attribute layout on every upload form, disables absent color data in favor of the default color, and unbinds the element buffer when index data becomes empty. `Mesh2D` applies the same absent-color and empty-index rules.

### BUG-024: Bind-to-edit state leaks

Several pre-4.5 resource operations bound a VAO, framebuffer, renderbuffer, or texture and then reset the binding to zero. That silently modified caller state and sometimes checked a different framebuffer than the one being bound. Fallback operations now capture and restore the exact previous bindings, including distinct read/draw framebuffers and pixel unpack alignment. Debug framebuffer completeness checks run against the requested framebuffer.

### BUG-025: Escaping render-pass state

An exception during pass creation, pass disposal, or an immediate draw could leave state, uniforms, shaders, textures, or a render target bound. Recorded command lists could also contain unmatched state pushes/pops. Cleanup now uses nested `finally` blocks, render state is validated before pushing, the context base state cannot be popped, and command replay validates built-in state balance and unwinds pushes after a failed command.

### BUG-026: Partial whole-texture copies

The whole-texture overload copied only mip zero. It now requires compatible mip counts and copies every mip plus every cube face. Cubemap image loading also maps top to positive Y and bottom to negative Y, matching the documented right/left/top/bottom/front/back cube convention.

### BUG-027: Incompatible framebuffer attachments

Framebuffer compatibility checks only compared a multisampled boolean, ignored some texture/renderbuffer mixtures, and permitted mismatched extents or a depth-only format at the depth-stencil attachment point. Attachments now require identical dimensions and exact sample counts, correct usage and format families, and allocated renderbuffer storage. Depth-stencil texture lookup recognizes both depth attachment points, draw-buffer indices are validated, and blits restore read/draw bindings.

### BUG-028: Incomplete deterministic disposal

Explicit disposal and finalization could race through a plain Boolean flag, and retained drawables offered no way to release their internally owned mesh resources early. Collection now uses an atomic transition. Meshes implement deterministic disposal, and sprite, tile-map, terrain, and render-model containers dispose their owned meshes while leaving caller-supplied shaders and textures untouched.

### BUG-029: Image-loader pixel and orientation ambiguity

Atlas extraction and image conversion could pass pixels through GDI+ drawing operations, which can alter exact channel values. Loading now clones exact 32-bit pixel regions. `TextureLoadOptions.FlipY` defaults to true and performs the named operation, preserving the previous bottom-left OpenGL default while making an explicit false value keep source row order.
