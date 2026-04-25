# Cross-Platform Migration Notes

> **Note:** the project was later renamed from `KadampaScreenSaver` to
> `SitePix` and generalized to work with any WordPress-style news blog
> via JSON profiles. This document retains the old name because it
> describes the historical migration. See [`README.md`](README.md) for
> the current architecture and [`samples/`](samples/) for profiles.

This document describes the changes made to convert KadampaScreenSaver from a Windows-only application to a cross-platform application (Windows, macOS, Linux).

## Summary of Changes

### 1. Image Processing: System.Drawing -> SkiaSharp

**Problem**: `System.Drawing.Common` only works on Windows at runtime. It relies on GDI+ which has no implementation on Linux or macOS.

**Solution**: Replaced with [SkiaSharp](https://github.com/nickremond/SkiaSharp) (v3.119.2), a cross-platform 2D graphics library backed by Google's Skia engine.

**Key API mappings**:
| System.Drawing | SkiaSharp |
|---|---|
| `Bitmap` | `SKBitmap` |
| `Graphics` | `SKCanvas` |
| `Color` | `SKColor` |
| `Font` | `SKFont` + `SKTypeface` |
| `SolidBrush` | `SKPaint` |
| `Image.FromStream()` | `SKBitmap.Decode()` |
| `Graphics.DrawString()` | `SKCanvas.DrawText()` |
| `Graphics.MeasureString()` | `SKFont.MeasureText()` |
| `ColorTranslator.FromHtml()` | `SKColor.Parse()` |

**Text wrapping**: SkiaSharp does not have built-in text wrapping (unlike `Graphics.DrawString` with a `RectangleF`). A custom `WrapText()` method was implemented that splits text by words and measures each line to fit within the specified width.

### 2. Browser Automation: Dynamic Channel Selection

**Problem**: Playwright was hardcoded to use `Channel = "msedge"` (Microsoft Edge), which is not available on Linux and may not be installed on macOS.

**Solution**: Browser channel is now selected at runtime:
- **Windows**: Uses Microsoft Edge (`"msedge"`)
- **macOS / Linux**: Uses Playwright's bundled Chromium (`null` channel)

The user agent string is also now platform-appropriate.

### 3. Task Scheduling: Platform-Native Scheduling

**Problem**: Windows Task Scheduler API (`TaskScheduler` NuGet package) only works on Windows.

**Solution**: `TaskRegistration.cs` now provides platform-specific scheduling:
- **Windows**: Windows Task Scheduler (via `Microsoft.Win32.TaskScheduler`, conditionally compiled with `#if WINDOWS`)
- **macOS**: Creates a LaunchAgent plist in `~/Library/LaunchAgents/`
- **Linux**: Adds a cron job to the user's crontab

The `TaskScheduler` NuGet package is only referenced when building on Windows (via MSBuild condition in `.csproj`).

### 4. Project File Changes (`.csproj`)

- Removed `System.Drawing.Common` package reference
- Added `SkiaSharp` (v3.119.2) package reference
- Made `TaskScheduler` package conditional: only included on Windows builds
- Added `WINDOWS` define constant (conditional on Windows) for `#if WINDOWS` compilation

### 5. UrlLogger Consolidation

The `UrlLogger` class existed in two places (inline in `Program.cs` and in `UrlLogger.cs`). Consolidated to a single implementation in `UrlLogger.cs` with the better version that includes:
- URL normalization (lowercase, trailing slash trimming)
- Tab-separated log format with ISO 8601 timestamps
- In-memory `HashSet` cache for fast lookups

### 6. Minor Platform Fixes

- `File.SetCreationTime()` is wrapped in try-catch since creation time is not settable on all Linux filesystems
- Added `.png` to the list of retained file extensions
- Default font falls back to `"sans-serif"` if not configured (SkiaSharp will resolve to the system default)

## Building

```bash
# Build for the current platform
dotnet build

# Publish self-contained for specific platforms
dotnet publish -r win-x64 --self-contained
dotnet publish -r osx-x64 --self-contained
dotnet publish -r osx-arm64 --self-contained
dotnet publish -r linux-x64 --self-contained
```

## Font Availability

Fonts vary by platform. Recommended fonts:
- **Windows**: `Palatino Linotype`, `Segoe UI`, `Gabriola`
- **macOS**: `Palatino`, `Helvetica Neue`, `Georgia`
- **Linux**: `DejaVu Serif`, `Liberation Serif`, `Noto Serif`

If a configured font is not found, SkiaSharp falls back to the system default sans-serif font.
