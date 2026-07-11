# Known Bugs

This file tracks bugs discovered during the source-formatting review. All entries are open. The formatting pass that added this document intentionally does not change runtime behavior.

## BUG-001: Text shaders may be read before initialization

- **Status:** Open
- **Severity:** Medium
- **Affected code:** `Gfx.DrawText`, `Gfx.Init2D`
- **Reproduction:** Initialize the rendering context and call `Gfx.DrawText` before any other 2D primitive has initialized the shared 2D shaders.
- **Impact:** `DrawText` selects `Default2D` or `SdfText2D` before calling `Init2D`, so the selected shader may be `null` and the first text draw can throw `NullReferenceException`.
- **Recommended resolution:** Call `Init2D(PrimitiveType.Triangles)` before selecting or configuring the text shader.

## BUG-002: Connections can contain ports from another graph

- **Status:** Open
- **Severity:** Medium
- **Affected code:** `FunctionNodeGraph.Connect`
- **Reproduction:** Create compatible nodes in two different `FunctionNodeGraph` instances and pass one port from each graph to `Connect`.
- **Impact:** The connection is accepted even though one endpoint is not owned by the target graph. Evaluation and serialization can then observe an invalid graph containing references to absent nodes.
- **Recommended resolution:** Verify that both endpoint nodes belong to the graph before replacing or adding a connection. Reject foreign ports without mutating the graph.

## BUG-003: Tabs can receive fallback-glyph kerning

- **Status:** Open
- **Severity:** Low
- **Affected code:** `GfxFont.LayoutString`
- **Reproduction:** Layout text containing a tab after a printable character with a font that resolves the tab through its fallback glyph.
- **Impact:** Glyph lookup and pair kerning happen before control-character handling. The tab advance can therefore include kerning against the fallback glyph, producing inconsistent alignment.
- **Recommended resolution:** Handle carriage returns, newlines, and tabs before glyph lookup and kerning. Define whether a tab resets the previous-character kerning state and apply that rule consistently.

## BUG-004: Text scale is not restored after a draw failure

- **Status:** Open
- **Severity:** Low
- **Affected code:** `Gfx.DrawText`
- **Reproduction:** Draw text with an explicit font size and trigger an exception during atlas preparation, shader setup, upload, or drawing.
- **Impact:** `ScaledFontSize` remains set to the requested draw size, affecting later measurement and rendering with the same font.
- **Recommended resolution:** Restore the original scale in a `finally` block covering every operation after the temporary size is assigned.

## BUG-005: Debug text drawing assumes at least one glyph

- **Status:** Open
- **Severity:** Low
- **Affected code:** `Gfx.DrawText`
- **Reproduction:** Call `Gfx.DrawText` with `DebugDraw` enabled and a non-empty string containing only layout controls such as newlines or tabs.
- **Impact:** Layout produces an empty glyph array, but debug rendering indexes `Chars[0]`, causing `IndexOutOfRangeException`.
- **Recommended resolution:** Guard glyph-specific debug markers with `Chars.Length > 0`; bounds drawing should handle an empty layout independently.

## BUG-006: Empty glyph dimensions can cause invalid bitmap indexing

- **Status:** Open
- **Severity:** Low
- **Affected code:** `TTFFont.GetGlyphBorderMaximum`
- **Reproduction:** Inspect the SDF border of a glyph whose rasterized bitmap has a zero width or height.
- **Impact:** Border indexing assumes both dimensions are positive and can calculate a negative or out-of-range bitmap offset.
- **Recommended resolution:** Return zero when either dimension is zero or the bitmap is empty before scanning its edges.

## BUG-007: Node port IDs are always empty

- **Status:** Open
- **Severity:** Low
- **Affected code:** `NodePort.Id`
- **Reproduction:** Create any reflected function node and inspect the IDs of its input and output ports.
- **Impact:** Every port exposes `Guid.Empty`, so the public ID cannot uniquely identify ports for retained selections, diagnostics, or external graph tooling.
- **Recommended resolution:** Initialize the ID with `Guid.NewGuid()` or accept a reconstructed ID through an internal constructor if persistence must preserve port identity.

## BUG-008: Null JSON bypasses structured load diagnostics

- **Status:** Open
- **Severity:** Low
- **Affected code:** `NodeGraphJson.Deserialize`
- **Reproduction:** Pass `null` as the JSON argument with a valid registry.
- **Impact:** `JsonSerializer.Deserialize` throws `ArgumentNullException`, while malformed JSON is returned as a structured `NodeGraphLoadResult` failure.
- **Recommended resolution:** Detect null before deserialization and return a consistent validation failure, or explicitly document and enforce an argument-exception contract.
