# PRD: SkiaSharp Upgrade (3.119.2 → 4.x)

**Owner:** Alex
**Status:** Draft
**Date:** 2026-05-24
**Target component:** [SitePix/SitePix.csproj](../SitePix/SitePix.csproj) — `PackageReference Include="SkiaSharp" Version="3.119.2"`

---

## 1. Background

SitePix uses SkiaSharp for the entire image post-processing pipeline: decoding downloaded photos, measuring/wrapping/rendering the title and subtitle overlay text, picking a readable text colour against the photo background, and re-encoding to JPEG/PNG/WebP/etc.

All of this currently lives in [Program.cs:820-1070](../SitePix/Program.cs#L820) plus the inline decode/draw block at [Program.cs:264-315](../SitePix/Program.cs#L264).

Today we pin **SkiaSharp 3.119.2** (released 2026-02-07, the last stable on the 3.x line). The upstream package is moving to **4.x**, currently shipping as **`4.147.0-preview.2.1`** (released 2026-05-11) and built on Skia milestone 147. There is **no 4.x stable release yet** — the team has signalled rolling previews through summer 2026, with RC → GA in that window. 4.x is a major version bump but the announcement explicitly preserves the surface area we depend on (`SKBitmap`, `SKCanvas`, `SKFont`, `SKPaint`, `SKTypeface`, `SKPath`).

## 2. Goals

1. Move SitePix onto a currently-maintained SkiaSharp line so we keep getting Skia engine fixes (codecs, font shaping, AArch64 perf).
2. Pick up rendering-quality improvements that benefit the overlay text and the photo passthrough, **without** changing the user-visible output in surprising ways.
3. Adopt the small number of new APIs that *actually* simplify our code (notably `SKPathBuilder` only if we touch path work, and animated WebP if we ever ship motion).
4. Stay self-contained-publish-friendly across the 6 RIDs in the csproj (`win-x64/arm64`, `linux-x64/arm64`, `osx-x64/arm64`).

## 3. Non-goals

- Refactoring `DrawTextOnImage` for fun. The current overlay logic works; this PRD is an upgrade, not a rewrite.
- Adopting `HarfBuzzSharp` for advanced text shaping. SitePix renders short Latin/diacritic titles; the default Skia text path is sufficient.
- Switching the image pipeline away from SkiaSharp.

## 4. What changes between 3.119.2 and 4.x

### 4.1 Engine-level improvements we get for free
- **Sharper downscaled images** — mipmap sharpening is on by default. Relevant if/when we ever resample (we currently do not, but it future-proofs a "max dimension" feature).
- **Automatic EXIF rotation** in the codec layer. Today our pipeline trusts whatever the WordPress upload chain produced; with 4.x, `SKBitmap.Decode` will honour EXIF orientation. **This is a behaviour change** — see §6.
- **Oversized bitmaps auto-tile** to stay under GPU texture limits. Low impact for us (CPU raster path), but defends against the rare gigapixel post image.
- **Color-accuracy improvements** for Rec.709 / HLG / PQ transfer functions. Not load-bearing for SitePix.

### 4.2 New APIs worth knowing
- **Variable fonts** — full OpenType axis control via `SKTypeface`. *Potential use:* let the user pick an exact weight (e.g. "Inter 650") in the wizard instead of relying on whichever family-name match Skia picks at [Program.cs:995](../SitePix/Program.cs#L995). **Optional, not required.**
- **Color font palettes** — switch CPAL palettes on emoji/icon fonts. Not applicable to our overlay text.
- **`SKPathBuilder`** — modernised path construction; `SKPath` is now immutable under the hood. We don't build paths anywhere in the codebase, so this is a no-op.
- **Animated WebP encoding** via `SKWebpEncoder` (added in 4.147 Preview 2). Not applicable — SitePix writes static frames.

### 4.3 What stays the same
The 4.0 announcement is explicit that the legacy surface remains. Every call site we use today — `SKBitmap.Decode`, `SKCanvas` ctor over a bitmap, `SKImage.FromBitmap`, `image.Encode(format, 95)`, `SKFont`, `SKPaint` with `Style`/`StrokeJoin`/`IsAntialias`, `SKRoundRect`, `canvas.DrawText(string, x, y, SKTextAlign, SKFont, SKPaint)`, `font.MeasureText`, `font.Spacing`, `SKTypeface.FromFamilyName` — is preserved in 4.x.

## 5. Why upgrade now

- **Maintenance posture.** 3.119.2 is the last of the 3.x line; new Skia engine fixes ship on 4.x.
- **Cross-platform native payloads.** 4.x adds Tizen and Linux Bionic targets and continues to harden cross-builds (the 3.119.2 ARM64 fontconfig fix gives a sense of the kinds of platform bugs that surface against the 6 RIDs we publish).
- **Single-file publish.** SitePix publishes self-contained with `IncludeNativeLibrariesForSelfExtract=true`. The 4.x native-payload reshuffles should be picked up early, not at a release crunch.
- **AOT/IlcDisableReflection fixes** landed late in 3.x; staying current keeps the AOT door open if we ever want it.

## 6. Risks & behavioural changes to validate

1. **EXIF auto-rotation** — the single biggest behavioural risk. If WordPress images carry rotated EXIF, 4.x will render them rotated; 3.x ignored it. Mitigation: sample 20–50 production images through both versions, diff dimensions and visual orientation.
2. **Mipmap sharpening default** — if we ever add resampling, output sharpness will visibly change vs. 3.x. No mitigation needed today since we don't resample.
3. **`SKBitmap.GetPixel` performance** — [`CalculateAverageColor`](../SitePix/Program.cs#L894) loops `GetPixel(x, y)` per pixel. This was already slow on 3.x; 4.x doesn't fix it. Out of scope for the upgrade, but a follow-up using `SKBitmap.Pixels` or `GetPixels()` + span would be a real win. **Flag as a separate task, do not couple to the upgrade.**
4. **Single-file publish** — verify on all 6 RIDs that the new native blobs extract correctly. Highest risk: `linux-arm64`, `osx-arm64`.
5. **Preview status** — as of 2026-05-24 the only 4.x package is `4.147.0-preview.2.1`. Shipping a Preview to end users means: (a) NuGet `PackageReference` must opt into prerelease, (b) we inherit any preview-only regressions Mono finds before RC, (c) we may need to re-bump once or twice as previews roll out through summer. See §7.0 for the adoption-timing decision.

## 7. Migration steps

### 7.0 Adoption timing — pick one

| Option | Pros | Cons |
|---|---|---|
| **A. Adopt `4.147.0-preview.2.1` now** | Get EXIF, sharper downscale, variable fonts immediately. Find platform regressions early on all 6 RIDs while the upstream team is actively releasing previews. | Shipping Preview to end users; may need a follow-up bump per preview drop until GA. |
| **B. Wait for 4.x RC** (likely a few weeks per the upstream cadence) | Lower risk of preview-only regressions, no opt-in to prerelease NuGets. | Sit on 3.119.2 a bit longer; no EXIF/sharper-downscale until then. |
| **C. Wait for 4.x GA** (targeted summer 2026) | Lowest risk; matches our `v1.0.x` posture of shipping stable deps. | Longest delay; we evaluate cold against whatever GA ships. |

**Recommendation:** **B** — track upstream and bump to RC the day it drops. SitePix is a small-blast-radius CLI, so we can afford to be one step ahead of GA, but shipping `preview.2` to users today buys little over waiting for RC.

If we're impatient, **A** is defensible — pin the exact preview (`4.147.0-preview.2.1`), don't float, and re-evaluate each preview drop.

### 7.1 Mechanical bump

1. Bump `SkiaSharp` in [SitePix.csproj](../SitePix/SitePix.csproj) — for Option A use `Version="4.147.0-preview.2.1"` (exact pin; do *not* use a floating range for a preview). NuGet will pick it up automatically; no `<RestoreAdditionalProjectSources>` needed since previews are on nuget.org.
2. `dotnet restore && dotnet build` — should be clean given the API preservation.
3. Run the existing flow against a fixed sample of WordPress posts, comparing produced JPEGs/PNGs/WebPs against the 3.119.2 baseline. Pay attention to:
   - Orientation (EXIF)
   - Text rendering crispness around small sizes (the auto-fit loop at [Program.cs:997-1009](../SitePix/Program.cs#L997))
   - Encoded file sizes at quality 95
4. Publish single-file for each RID, smoke-test the produced binary actually decodes and writes one image.
5. Cut a `v1.1.0` release noting "SkiaSharp 4.x; EXIF orientation is now respected automatically."

## 8. Out-of-scope follow-ups (do not block the upgrade)

- Replace per-pixel `GetPixel` in `CalculateAverageColor` with `GetPixels()` + pointer/span scan. ~50–100x speedup on large photos.
- Expose a font-weight slider in the setup wizard backed by variable-font axes.
- Add an optional "max long edge" resample using the now-sharper downscaler.

## 9. Success criteria

- Project builds and publishes self-contained on all 6 RIDs.
- Visual diff against 3.119.2 baseline shows no regressions for the title/subtitle overlay or panel rendering.
- EXIF-rotated input images now render in their intended orientation (treated as an improvement, called out in release notes).
- No new runtime exceptions on the existing sample profiles in `samples/`.

## 10. References

- [SkiaSharp 4.0 Preview 1 announcement (Microsoft .NET blog)](https://devblogs.microsoft.com/dotnet/welcome-to-skia-sharp-40-preview1/)
- [SkiaSharp 4.147.0-preview.2.1 on NuGet](https://www.nuget.org/packages/SkiaSharp/4.147.0-preview.2.1) — latest preview, 2026-05-11
- [SkiaSharp 4.147.0-preview.1.1 on NuGet](https://www.nuget.org/packages/SkiaSharp/4.147.0-preview.1.1)
- [SkiaSharp v4 release tracking (issue #3684)](https://github.com/mono/SkiaSharp/issues/3684)
- [SkiaSharp releases on GitHub](https://github.com/mono/SkiaSharp/releases)
- [SkiaSharp 3.119.2 on NuGet](https://www.nuget.org/packages/SkiaSharp/) — current stable
- [Skia engine release notes](https://skia.googlesource.com/skia/+/refs/heads/main/RELEASE_NOTES.md)
