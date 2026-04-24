# SitePix on macOS — Setup PRD

> Companion to [`XPLATFORM.md`](../XPLATFORM.md) and the root
> [`README.md`](../README.md). This doc focuses on what a macOS end user
> needs to go from a fresh Mac to a working SitePix-fed screen saver.

## 1. Problem
The project's main deliverable on macOS is the cross-platform .NET
executable built from the repo (see `XPLATFORM.md` for the migration
story). That binary handles image scraping, text overlay, and
LaunchAgent generation. What's missing is a concrete, click-by-click
rollout guide for macOS — including the non-obvious bits: Gatekeeper
quarantine, Full Disk Access, and which of macOS's many screen-saver
modules actually accept a custom folder.

## 2. Goals
- A single path from `dotnet publish` output to a working daily
  image refresh and a macOS screen saver that cycles those images.
- Cover the macOS-specific friction the general README skips:
  Gatekeeper, Full Disk Access, the correct screen-saver module.
- Provide a verification checklist so the setup can be validated
  without waiting for the screen saver to trigger on its own.

## 3. Non-goals
- Code changes — the .NET binary is authoritative; anything that would
  require a code change belongs in an issue against `Program.cs` /
  `TaskRegistration.cs`, not here.
- A packaged `.pkg` / `.dmg` installer or a codesigned / notarized
  binary. Listed as a future enhancement in §9.
- A custom `.saver` screen-saver module. We rely on the built-in
  Classic slideshow modules.

## 4. Users
- Existing SitePix users migrating from Windows to macOS.
- New macOS users who want a daily-refreshed slideshow of imagery from
  any supported WordPress news blog (see `samples/` for profiles).

## 5. What actually runs on macOS
| Piece | Implementation |
| --- | --- |
| Executable | `SitePix` native binary produced by `dotnet publish -r osx-arm64 --self-contained` (or `-r osx-x64` on Intel Macs). |
| HTML fetch | Playwright with bundled Chromium (`Channel = null` on non-Windows, set in `Program.cs`). |
| Image drawing | SkiaSharp (cross-platform replacement for `System.Drawing`). |
| Scheduler | `TaskRegistration.RegisterMacOS()` writes `~/Library/LaunchAgents/com.sitepix.agent.plist` on first run, driven by `Task Scheduler:StartTime` in `appsettings.json`. |
| Screen saver | macOS built-in **Classic** slideshow modules (System Settings → Screen Saver). |

---

## 6. End-user setup

> **Fastest path:** run [`macos/install.sh`](install.sh) from the repo
> root. It executes §6.1–§6.5 automatically (install .NET 10 to
> `$HOME/.dotnet`, publish for the local arch, install Playwright
> Chromium, apply macOS-friendly defaults to `appsettings.json`). Pass
> `--run` to also download images once and open the output folder, and
> `--schedule` to also enable the daily LaunchAgent. The manual steps
> below are what the script does, in case you want to do it yourself.

### 6.1 Prerequisites
- macOS 13 (Ventura) or newer. Sonoma 14 / Sequoia 15 tested.
- [.NET 10 SDK](https://dotnet.microsoft.com/download) installed
  (needed to build; runtime-only is fine for the consumption step
  once a self-contained build exists).
- Command Line Tools for Xcode:
  ```bash
  xcode-select --install
  ```

### 6.2 Build the macOS binary
From the repo root:
```bash
dotnet publish SitePix/SitePix.csproj \
  -c Release \
  -r osx-arm64 \
  --self-contained \
  -o dist/macos
```
Substitute `osx-x64` on Intel Macs. Output lands in
`dist/macos/SitePix` alongside `appsettings.json`.

### 6.3 Install Playwright's browser once
The first time the app runs (or ahead of time), install Chromium for
Playwright:
```bash
# Install the Playwright .NET CLI tool once:
dotnet tool install --global Microsoft.Playwright.CLI
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"

# The CLI auto-discovers Playwright from a csproj, so run it from the
# project directory (not from the publish output):
cd SitePix
playwright install chromium
```
This drops Chromium into `~/Library/Caches/ms-playwright/`.

### 6.4 Configure `appsettings.json`
Copy and edit next to the binary in `dist/macos/`:
```json
{
  "StartPage": "https://kadampa.org/news",
  "Policies": { "LinkDepth": 7, "RetentionDays": 7 },
  "Task Scheduler": { "StartTime": "05:30" },
  "Directories": {
    "UseMyPictures": true,
    "PhotoText": true,
    "SubDirectory": "SitePix"
  },
  "PhotoText": {
    "Font": "Palatino",
    "DateInclude": true,
    "DateFormat": "MM/dd",
    "DatePrefix": " - ",
    "ImageFileName": false
  }
}
```
With `UseMyPictures: true` on macOS, images land in
`~/Pictures/SitePix`. Good default — the screen saver's
file picker and the Photos app both have that folder handy.

### 6.5 First run (clears Gatekeeper + triggers LaunchAgent creation)
```bash
./SitePix
```
If macOS blocks execution ("cannot be opened because the developer
cannot be verified"), clear quarantine:
```bash
xattr -dr com.apple.quarantine ./SitePix
```
On first successful run, `TaskRegistration.RegisterMacOS()` creates
`~/Library/LaunchAgents/com.sitepix.agent.plist`. It is **not
loaded automatically** — activate it once:
```bash
launchctl unload ~/Library/LaunchAgents/com.sitepix.agent.plist 2>/dev/null
launchctl load   ~/Library/LaunchAgents/com.sitepix.agent.plist
launchctl list | grep sitepix
```

### 6.6 Point the macOS screen saver at the folder
The modern Apple-designed screen savers (Sonoma / Sequoia aerials,
landscapes, Macintosh mosaic) cannot be pointed at a custom folder.
Use a **Classic** module instead.

macOS Sonoma (14) / Sequoia (15):
1. **System Settings → Screen Saver**.
2. Scroll to the **Classic** section.
3. Pick a module that accepts a folder source:
   *Ken Burns*, *Shifting Tiles*, *Sliding Panels*, *Photo Mobile*,
   *Photo Wall*, or *Vintage Prints*.
4. Click **Options…** (or **Photo Library** → **Source**) next to
   the preview.
5. **Choose Folder…** → select `~/Pictures/SitePix`.
6. Set idle timeout in **System Settings → Lock Screen → Start Screen
   Saver when inactive**.

macOS Ventura (13) and earlier:
1. **System Settings → Desktop & Screen Saver → Screen Saver**.
2. Pick a slideshow module as above.
3. **Source → Choose Folder…** → `~/Pictures/SitePix`.

### 6.7 Grant Full Disk Access if the folder picker is empty
If the "Choose Folder…" dialog shows the folder but the module
refuses to render from it, the screen-saver engine needs access to
your `~/Pictures` directory:
**System Settings → Privacy & Security → Full Disk Access →** add
`ScreenSaverEngine` (in `/System/Library/CoreServices/`).

### 6.8 Optional: wallpaper rotation from the same folder
**System Settings → Wallpaper → Add Folder…** → pick
`~/Pictures/SitePix`, toggle **Shuffle**. Desktop rotates
through the same daily-refreshed imagery.

---

## 7. Verification checklist
- [ ] `./SitePix` exits 0 and populates
      `~/Pictures/SitePix` with at least one `.jpg`.
- [ ] `~/Library/LaunchAgents/com.sitepix.agent.plist` exists.
- [ ] `launchctl list | grep sitepix` shows the agent loaded.
- [ ] A second run skips articles already in `VisitedUrls.log`.
- [ ] Files older than `Policies:RetentionDays` are removed on a
      subsequent run.
- [ ] The chosen Classic screen-saver module previews the folder's
      imagery when you click **Preview** in System Settings.
- [ ] Locking the Mac (`Ctrl+⌘+Q`) and leaving it idle for the
      configured lock-screen timeout starts the screen saver.

## 8. Troubleshooting
| Symptom | Fix |
| --- | --- |
| `cannot be opened because the developer cannot be verified` | `xattr -dr com.apple.quarantine ./SitePix` |
| `Executable doesn't exist at …ms-playwright/chromium-…` | Re-run `playwright install chromium`. Check `~/Library/Caches/ms-playwright/`. |
| `launchctl load` says `Load failed: 5: Input/output error` | Plist is already loaded; `launchctl unload` first, then `load`. |
| LaunchAgent runs but nothing downloads | Check `/tmp/com.sitepix.agent.log` and `/tmp/com.sitepix.agent.err`. Usually a Chromium path issue — reinstall browsers. |
| Screen saver shows black / no photos | Grant `ScreenSaverEngine` Full Disk Access (§6.7), or switch to a Classic slideshow module (§6.6). |
| HTTP 403 from the source site | Cloudflare challenge — the .NET app handles this via Playwright. If you see this in logs, Chromium is likely not installed; see `playwright install chromium`. |

## 9. Future enhancements
- Signed / notarized `.pkg` installer that handles Gatekeeper and
  `launchctl load` automatically.
- `make macos` target that wraps `dotnet publish` + `playwright
  install` + plist load.
- Custom `.saver` bundle so the screen saver runs the fetch at the
  moment the screen saver activates (no separate LaunchAgent
  required).
- Photos.app album ingestion so the modern non-Classic slideshow
  screen savers can source the images.
