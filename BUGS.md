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
