# SitePix

[![Release](https://img.shields.io/github/v/release/alexreich/SitePix?display_name=tag&sort=semver)](https://github.com/alexreich/SitePix/releases/latest)
[![CI](https://img.shields.io/github/actions/workflow/status/alexreich/SitePix/ci.yml?branch=main&label=CI)](https://github.com/alexreich/SitePix/actions/workflows/ci.yml)
[![Release CI](https://img.shields.io/github/actions/workflow/status/alexreich/SitePix/release.yml?label=Release%20CI)](https://github.com/alexreich/SitePix/actions/workflows/release.yml)
[![License: CC BY 4.0](https://img.shields.io/badge/license-CC%20BY%204.0-lightgrey)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com/download)

Pulls large photos from WordPress-style news blogs and drops them into a
folder your OS screen saver (or desktop slideshow) can point at. Runs on
**Windows, macOS, and Linux**.

Built to gather news imagery from any blog with an identifiable pattern
and configurable via JSON profiles so the same engine can feed off any
WordPress site with dated permalinks and standard `<article>` /
OpenGraph conventions.

---

## Install

### Windows

| Method | Command |
|---|---|
| winget | `winget install AlexReich.SitePix` |
| Chocolatey | `choco install sitepix` |
| Installer exe | [Download from Releases](https://github.com/alexreich/SitePix/releases/latest), run `SitePix-Setup-<version>.exe` |
| Portable zip | Same Releases page — `SitePix-<version>-win-x64-portable.zip` |

### macOS

```bash
# Homebrew (once the tap is published — see packaging/README.md):
brew install alexreich/tap/sitepix
brew services start sitepix          # daily sync at 05:30

# Or one-liner — no Homebrew required:
curl -fsSL https://github.com/alexreich/SitePix/releases/latest/download/install.sh | sh
```

### Linux

```bash
# AppImage (any distro, no install):
curl -fsSL -o SitePix.AppImage https://github.com/alexreich/SitePix/releases/latest/download/SitePix-<version>-x86_64.AppImage
chmod +x SitePix.AppImage && ./SitePix.AppImage

# Debian / Ubuntu / Mint:
sudo dpkg -i sitepix_<version>_amd64.deb            # or _arm64.deb

# Fedora / RHEL / openSUSE:
sudo rpm -i sitepix-<version>-1.x86_64.rpm          # or .aarch64.rpm

# Any distro (shell installer):
curl -fsSL https://github.com/alexreich/SitePix/releases/latest/download/install.sh | sh
```

After install on macOS or Linux, `sitepix-install-schedule` sets up a
launchd agent (mac) or systemd user timer (linux, with cron fallback).
Pass `--time HH:MM` to change the daily run time, `--uninstall` to remove.

---

## Build from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download). Microsoft
Edge is used by Playwright on Windows; macOS and Linux use Playwright's
bundled Chromium (installed once via `playwright install chromium`).

```bash
# macOS one-liner — installs .NET, publishes binary, fetches once, opens folder
./macos/install.sh --run

# Or manually for any platform
dotnet publish SitePix/SitePix.csproj -c Release -r osx-arm64 --self-contained \
  -p:PublishSingleFile=true -o dist/macos
cd SitePix && playwright install chromium
./dist/macos/SitePix                              # bundled default: petapixel.com
./dist/macos/SitePix samples/kadampa.org.json     # kadampa.org profile
./dist/macos/SitePix samples/atlasobscura.com.json
# Six profiles ship under samples/ — see the table below for the full list.
```

Use `osx-x64` on Intel Macs, `win-x64` on Windows, `linux-x64` / `linux-arm64`
on Linux.

---

## Configuration

On startup SitePix reads a JSON config. Pick one:

1. Positional CLI arg — `sitepix path/to/profile.json`
2. `appsettings.json` next to the binary (default — ships tuned for petapixel.com)
3. If `appsettings.json` is missing, SitePix falls back to `sitepix.json` next to the binary (for older/custom installs)

### Coming for kadampa.org?

The Kadampa profile ships with every release — both bundled next to
the binary and as a standalone download URL. The simplest path on
any install method:

```bash
# macOS / Linux — fetch and use in one shot:
curl -fsSL https://github.com/alexreich/SitePix/releases/latest/download/kadampa.org.json -o ~/.sitepix.json
sitepix ~/.sitepix.json
```

```powershell
# Windows:
Invoke-WebRequest https://github.com/alexreich/SitePix/releases/latest/download/kadampa.org.json -OutFile $env:USERPROFILE\sitepix.json
sitepix $env:USERPROFILE\sitepix.json
```

To make it the default so plain `sitepix` uses it on every run, copy
the same JSON over the bundled `appsettings.json` — see
[Switching profiles](#switching-profiles) below for where that file
lives on each install method.

The Kadampa profile pulls images to `~/Pictures/Kadampa`, applies
the Kadampa brand-color overlay palette (dark blue / beige / sky
blue), and uses a 1024 px minimum width.

### Switching profiles

The same five other profiles ship the same way — replace `kadampa.org`
in the URL above with `petapixel.com`, `atlasobscura.com`,
`fstoppers.com`, `thephoblographer.com`, or `smashingmagazine.com`.
See [the profile table](#sample-profiles) for what each one fetches.

If you'd rather edit `appsettings.json` directly (so plain `sitepix`
uses your chosen profile every run), it lives next to the binary:

| Install method | `appsettings.json` location |
|---|---|
| Windows installer / winget / Chocolatey | `C:\Program Files\SitePix\appsettings.json` |
| Windows portable zip | next to `SitePix.exe` in the extracted folder |
| macOS Homebrew | `$(brew --prefix)/Cellar/sitepix/<version>/libexec/appsettings.json` |
| macOS / Linux portable tarball | next to `SitePix` in the extracted folder |
| Linux `.deb` / `.rpm` | `/opt/sitepix/appsettings.json` |
| Linux AppImage | inside the AppImage — easier to use the URL above and pass it on the CLI |
| Built from source | `dist/<rid>/appsettings.json` |

Each install also bundles the full `samples/` directory next to
`appsettings.json`, so `sitepix samples/<name>.json` works without
needing to `curl` anything.

### Profile schema

```jsonc
// Example: samples/petapixel.com.json — see samples/ for five other ready-to-use profiles.
{
  "StartPage": "https://petapixel.com/",

  "Policies": {
    "LinkDepth": 7,          // max articles per run
    "RetentionDays": 7       // days to keep downloaded images
  },

  "Scraper": {
    // Regex matched against each candidate article URL. {Year} is
    // substituted with the current 4-digit year at runtime.
    "UrlPattern": "/{Year}/",

    // Minimum image width (pixels). Smaller images are discarded.
    "MinWidthPx": 1024,

    // Case-insensitive substrings — any image URL containing one of
    // these is skipped (thumbnails, sponsor logos, etc.).
    "ImageUrlExcludes": ["150x", "whatsapp-image", "book"],

    // Ordered CSS selectors. The first one that matches on the article
    // page defines the content scope that <img> tags are pulled from.
    "ContentSelectors": [
      "main article .entry-content",
      "article .entry-content",
      "main article",
      "article",
      "body"
    ]
  },

  "Task Scheduler": {
    "StartTime": "05:30",    // empty = don't register a scheduled task
    "Id": "SitePix"          // optional override for task/plist/cron identifier
  },

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
    "ImageFileName": false,
    "BrandColors": ["#224486", "#A99886", "#66B9C4"]
  }
}
```

### Sample profiles

Live in [`samples/`](samples/) — file basename is the source domain.
Most popular first. Every profile is also published as a standalone
download URL on the [latest release](https://github.com/alexreich/SitePix/releases/latest):

    https://github.com/alexreich/SitePix/releases/latest/download/<domain>.json

| Profile | Source | Notes |
|---|---|---|
| [`petapixel.com.json`](samples/petapixel.com.json) | [petapixel.com](https://petapixel.com) | Photography news, ~1.5M monthly readers. **Verified.** |
| [`atlasobscura.com.json`](samples/atlasobscura.com.json) | [atlasobscura.com](https://www.atlasobscura.com) | Travel curiosities & long-form photo essays. **Verified.** |
| [`fstoppers.com.json`](samples/fstoppers.com.json) | [fstoppers.com](https://fstoppers.com) | Photography community: news, originals, education. |
| [`thephoblographer.com.json`](samples/thephoblographer.com.json) | [thephoblographer.com](https://www.thephoblographer.com) | Photo gear reviews & sample galleries. |
| [`smashingmagazine.com.json`](samples/smashingmagazine.com.json) | [smashingmagazine.com](https://www.smashingmagazine.com) | Web design & code, screenshot-heavy. |
| [`kadampa.org.json`](samples/kadampa.org.json) | [kadampa.org/news](https://kadampa.org/news) | Buddhist news (original profile). **Verified.** |

The `macos/install.sh` script ends with an interactive picker for these
profiles (or pass `--source <domain>` for non-interactive — e.g.
`--source petapixel.com`). Whichever profile is chosen is copied over
`dist/macos/appsettings.json` so the binary picks it up next run. The
settings file is fully commented — open it any time to tweak min-width,
retention, font, brand colors, etc.

### Brand colors

`PhotoText:BrandColors` is an array of hex strings. For each text overlay
SitePix picks the color with the best contrast against the background it's
sitting on, then draws an automatic black/white stroke around the glyphs
for readability over mixed-luminance photos.

### Reset visited history (start fresh)

SitePix keeps a dedupe history file named `VisitedUrls.log` in the OS
application-data folder (not in the image output folder). The path uses the
`Directories:SubDirectory` value from your profile (default `SitePix`):

| OS | Location |
|---|---|
| Windows | `%LOCALAPPDATA%\SitePix\<SubDirectory>\VisitedUrls.log` |
| macOS | `~/Library/Application Support/SitePix/<SubDirectory>/VisitedUrls.log` |
| Linux | `~/.local/share/SitePix/<SubDirectory>/VisitedUrls.log` |

Delete that file to make SitePix treat every article URL as new again.

```powershell
# Windows — SubDirectory = "SitePix" (default)
Remove-Item "$env:LOCALAPPDATA\SitePix\SitePix\VisitedUrls.log" -ErrorAction SilentlyContinue

# Windows — custom SubDirectory, e.g. "Kadampa"
Remove-Item "$env:LOCALAPPDATA\SitePix\Kadampa\VisitedUrls.log" -ErrorAction SilentlyContinue
```

```bash
# macOS / Linux
rm -f "$HOME/Library/Application Support/SitePix/SitePix/VisitedUrls.log"   # macOS default
rm -f "$HOME/.local/share/SitePix/SitePix/VisitedUrls.log"                  # Linux default
```

If you are not sure which file applies to you:

```powershell
Get-ChildItem "$env:LOCALAPPDATA\SitePix" -Filter "VisitedUrls.log" -Recurse | Select-Object -ExpandProperty FullName
```

---

## Scheduling

Setting `Task Scheduler:StartTime` triggers platform-native scheduling the
next time SitePix runs:

- **Windows**: registers a daily task in Task Scheduler.
- **macOS**: writes `~/Library/LaunchAgents/com.sitepix.agent.plist`. The
  packaged `sitepix-install-schedule` upgrades that to a loaded LaunchAgent
  with explicit `--time` control and clean uninstall.
- **Linux**: appends a cron line. `sitepix-install-schedule` swaps that for
  a systemd user timer (preferred).

Multiple profiles on one machine can use distinct identifiers via
`Task Scheduler:Id` — each ends up at its own task / plist label / cron
marker.

---

## Screen saver setup

- **Windows**: Settings → Personalization → Lock screen → Screen saver →
  Photos → browse to the configured image directory.
- **macOS**: System Settings → Screen Saver → Photos → Choose Folder… →
  pick `~/Pictures/SitePix`. See [`macos/PRD.md`](macos/PRD.md) for the
  detailed walkthrough (Classic slideshow modules, Gatekeeper, Full Disk
  Access, verification).
- **Linux**: use your desktop environment's slideshow settings.

---

## Uninstall

```bash
# Windows
winget uninstall AlexReich.SitePix
choco uninstall sitepix
# or Add/Remove Programs → SitePix → Uninstall

# macOS Homebrew
brew services stop sitepix
brew uninstall sitepix

# macOS / Linux (install.sh)
sitepix-install-schedule --uninstall
rm -rf ~/.local/lib/sitepix ~/.local/bin/sitepix ~/.local/bin/sitepix-install-schedule

# Linux .deb / .rpm
sudo apt remove sitepix         # or dnf / zypper / rpm -e
sitepix-install-schedule --uninstall
```

Downloaded photos are not deleted automatically — remove `~/Pictures/SitePix`
(or whatever you configured) by hand.

---

## Releasing

Bump `<Version>` in [`SitePix/SitePix.csproj`](SitePix/SitePix.csproj),
commit, push a `vX.Y.Z` tag — CI builds and publishes every artifact
(installer, portable zip, AppImage, .deb, .rpm, mac tarballs, winget
manifests, Chocolatey nupkg, Homebrew formula). Full runbook + one-time
package-manager submission steps: [`packaging/README.md`](packaging/README.md).

---

## Verifying downloads

Each release asset has a matching `.sha256` sidecar at the same URL. To
verify before installing:

```bash
# macOS / Linux
shasum -a 256 -c SitePix-1.0.0-osx-arm64.tar.gz.sha256
sha256sum -c    sitepix_1.0.0_amd64.deb.sha256
```

```powershell
# Windows
$expected = (Get-Content SitePix-Setup-1.0.0.exe.sha256 | Select-String -Pattern '^\S+').Matches.Value
$actual   = (Get-FileHash SitePix-Setup-1.0.0.exe -Algorithm SHA256).Hash.ToLowerInvariant()
if ($expected -ne $actual) { throw "Hash mismatch" } else { "OK" }
```

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for build, test, and PR
guidelines, and [CHANGELOG.md](CHANGELOG.md) for what's in each
release. Security issues: please follow [SECURITY.md](SECURITY.md) —
do not file public issues.

## Attribution

SitePix is released under [Creative Commons Attribution 4.0](LICENSE).
If you redistribute, please credit the project and link back.

Scraped images remain the copyright of their original publishers. SitePix
simply downloads what's publicly available on the configured site; please
respect each site's terms of service and robots directives.

---

[:heart: Sponsor](https://github.com/sponsors/alexreich)
