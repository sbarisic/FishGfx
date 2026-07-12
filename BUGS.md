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
