# Platform Cleanup Notes

## Status
All 18 apps (except Spotify) have been rewritten on the widget platform. The apps-rewrite branch is complete with 640 tests passing.

## Identified Duplications

Six agents independently wrote their own versions of common helpers because certain platform pieces are internal or overly constrained:

### 1. **Glyph Text Caching** (~150-200 lines total duplication)
- `Clocks/ClockKit.cs`: `GlyphAtlas` for anti-aliased scaled digits
- `Ambient/AmbientKit.cs`: `GlyphLine` for per-character caching
- `Toys/ToyKit.cs`: `CachedText` for stateless caching
- `Tube/TubeNodes.cs`: `TextRun` for text layout

**Reason**: `GlyphRun` is internal to Graphics; apps cannot reuse it.

**Recommendation**: Promote a public `TextRun` to `Graphics/UI/` with caching built in.

### 2. **Rounded Rectangles & Circles** (~80-100 lines)
- `Toys/ToyKit.cs`: `Gfx.Disc`, anti-aliased fills
- `Ambient/AmbientKit.cs`: similar disc and rounded fills
- `Visuals/VisualKit.cs`: allocation-free disc rendering
- `Clocks/ClockKit.cs`: rounded rect for flip cards

**Reason**: `SimpleGraphics.FillRoundedRect` and `DrawRoundedRect` allocate a closure per call.

**Recommendation**: Add allocation-free versions to `Graphics.SimpleGraphics`.

### 3. **Rolling Gradient Digits** (~100-150 lines)
- `Clocks/GlyphDigit.cs`: the flip card and rolling digit
- `Ambient/DigitStrip.cs`: a roll animation with tweened colour
- Variants in Toys and Visuals

**Reason**: `RollingNumber` takes a static `TextStyle` and cannot do gradient or tweened colour without per-frame allocation.

**Recommendation**: Extend `RollingNumber` or create a `GradientRollingNumber` widget.

### 4. **Colour Palettes & Tweening** (~80-120 lines)
- `Clocks/ClockKit.cs`: `ClockPalette`, `LiveTheme` palette blending
- `Ambient/HomeTheme.cs`: scene-specific palettes
- `Toys/ToyPalettes.cs`: palette helpers
- Similar patterns in Visuals

**Reason**: Each app had its own palette needs; common pattern is universal.

**Recommendation**: Create a generic `Graphics/UI/PaletteAnimation.cs` for palette tweening.

### 5. **Glow & Post-Effects** 
Already consolidated in `Graphics/Effects/`. No action needed.

### 6. **Fonts & Font Caching**
- `Fonts.Load()` reassigns static instances, causing re-rasterising in parallel tests
- Several apps cache their fonts to avoid this

**Recommendation**: Make `Fonts.Load()` idempotent or add a once-only init.

## Platform Gaps That Drove Duplication

1. `GlyphRun` and `UiDraw` are internal to Graphics
2. `SimpleGraphics` primitives allocate closures
3. `RollingNumber` has no gradient or animation support
4. `Panel` has no absolute positioning
5. `Pager` transitions are opaque (black background)
6. `FrameBuffer` has no writable pixel span API
7. `Fonts.Load()` is not idempotent

## Recommended Cleanup (Priority Order)

1. **High**: Promote `TextRun` to public; add to `Graphics/UI/`
2. **High**: Add allocation-free disc/rounded-rect to `SimpleGraphics`
3. **Medium**: Extend `RollingNumber` or add gradient variant
4. **Medium**: Make `Fonts.Load()` idempotent
5. **Low**: Create generic palette animation helper
6. **Future**: Fix opacity, positioning, and span access on `FrameBuffer`

## Current State

- **Branch**: `apps-rewrite` (all 18 rewrites merged, 640 tests passing)
- **Not on main**: ready for review/visual testing
- **Next**: Phase 3 (scheduler, overlays, commute dashboard)

## Notes for Phase 3

Phase 3 will add more apps (or enhance existing ones) that will also need glyph text, gradients and palettes. Doing this cleanup **before** Phase 3 is ideal so Phase 3 doesn't duplicate a seventh version of these helpers.
