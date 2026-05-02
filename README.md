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
./dist/macos/SitePix                              # first run → setup wizard
./dist/macos/SitePix --setup                      # re-run wizard later
./dist/macos/SitePix samples/kadampa.org.json     # bypass wizard, use a specific sample
# 10 ready-to-use samples ship under samples/ — see "Bundled samples" below.
```

Use `osx-x64` on Intel Macs, `win-x64` on Windows, `linux-x64` / `linux-arm64`
on Linux.

---

## Configuration

### First-run setup wizard

SitePix is private-use software — downloads land in your Pictures folder
for slideshow / screen-saver / wallpaper use; nothing is re-published
anywhere unless you do that yourself.

The first time you run `sitepix` on a fresh install, an interactive
wizard asks seven short questions:

1. **Source** — Met Museum (default), Smithsonian, NASA, Library of
   Congress, Flickr Commons, NYPL, plus four HTML-scrape profiles
   (kadampa, petapixel, atlasobscura, thephoblographer).
2. **API key** — only for the three sources that need one (Smithsonian,
   Flickr Commons, NYPL); the prompt links to the signup page.
3. **Save folder** — your OS Pictures folder by default, or a custom path.
4. **Images per run** — how many to download each time.
5. **Retention days** — older files are pruned automatically.
6. **Text overlay** — burn the image title + today's date into each photo.
7. **Daily schedule** — register a Task Scheduler / launchd / cron job.

Re-run the wizard at any time with:

```bash
sitepix --setup
```

The wizard writes a config file to your local app-data folder:

| OS | Config path |
|---|---|
| Windows | `%LOCALAPPDATA%\SitePix\appsettings.json` |
| macOS | `~/Library/Application Support/SitePix/appsettings.json` |
| Linux | `~/.local/share/SitePix/appsettings.json` |

Subsequent runs (manual, scheduled, headless) read that file directly —
the wizard does not fire again until you re-invoke it with `--setup`.

### Picking a config without the wizard

The config-resolution order on launch is:

1. `sitepix --setup` → re-runs the wizard.
2. `sitepix path/to/profile.json` → uses that file (absolute, CWD-relative,
   or relative to the binary's directory).
3. `<BaseDirectory>/appsettings.json` → for legacy installs that bundled
   one (no longer shipped by default).
4. `<BaseDirectory>/sitepix.json` → legacy filename fallback.
5. `<LocalAppData>/SitePix/appsettings.json` → where the wizard saves.
6. None of the above → wizard fires (if running in a terminal) or
   prints a clear error pointing you to `--setup` or a sample.

The 10 bundled samples live next to the binary under `samples/`, so
`sitepix samples/<name>.json` works on any install path. Examples
under [Bundled samples](#bundled-samples) below.

### Configuration file reference

Every key in the wizard-generated `appsettings.json` (and every sample
under `samples/`) is documented below. The samples themselves are plain
JSON without comments — this is the canonical schema doc.

#### Source

What to fetch and from where.

| Field | Type | Default | Notes |
|---|---|---|---|
| `Source.Provider` | string | (none → HTML mode) | One of: `metmuseum`, `smithsonian`, `loc`, `nasa`, `flickrcommons`, `nypl`. Omit (or leave `Source` unset entirely) to use HTML scraping mode driven by `StartPage` + `Scraper`. |
| `Source.Query` | string | `""` | Provider-specific filter. Met / Smithsonian: free-text search. NASA / Flickr / LoC: keyword. NYPL: solr query. |
| `Source.ApiKey` | string | `""` | Inline credential for sources that need one (Smithsonian, Flickr Commons, NYPL). Stored in your local config file; not committed by SitePix. |
| `Source.ApiKeyEnv` | string | `""` | Name of an environment variable to read the key from instead of `ApiKey`. Useful if you'd rather not have the key on disk. |
| `Source.OnlyHighlights` | bool | `false` | Met only — restrict to the museum's ~2,000 curated highlights. |
| `Source.MaxImagesPerItem` | int | `1` | Met / Smithsonian / LoC — also pull alternate-view images for each object. |
| `Source.Path` | string | `"photos"` | LoC only — search endpoint path. Set to e.g. `collections/farm-security-administration` to pin to a curated PD collection. |

#### StartPage

```jsonc
"StartPage": "https://example.com/news"
```

Used by the HTML-scraping path as the index page to walk for article
links. Informational when `Source.Provider` is set (API mode ignores it).

#### Policies

```jsonc
"Policies": { "LinkDepth": 20, "RetentionDays": 7 }
```

| Field | Type | Default | Notes |
|---|---|---|---|
| `Policies.LinkDepth` | int | `20` | Max items processed per run. API mode = items downloaded. HTML mode = articles visited (each may yield several images). |
| `Policies.RetentionDays` | int | `7` | Files older than this in the save folder are deleted at the end of every run. Set high to disable pruning. |

#### Scraper

Mostly relevant in HTML mode; `MinWidthPx` and `RequestDelayMs` also
apply in API mode.

| Field | Type | Default | Notes |
|---|---|---|---|
| `Scraper.UrlPattern` | string (regex) | `/{Year}/` | HTML mode — applied to each anchor on the start page. `{Year}` substitutes the current 4-digit year. |
| `Scraper.MinWidthPx` | int | `1024` | Anything narrower is dropped after download. |
| `Scraper.ImageUrlExcludes` | string[] | `[]` | Case-insensitive substring filters for image URLs (thumbnail markers, sponsor CDN paths, etc.). |
| `Scraper.ContentSelectors` | string[] | `[ "main article .entry-content", … ]` | HTML mode — first matching CSS selector on the article page becomes the scope for `<img>` extraction. |
| `Scraper.ContentExcludeSelectors` | string[] | `[]` | HTML mode — `<img>` tags inside any of these selectors are skipped (newsletter widgets, related-articles strips, …). |
| `Scraper.SameOriginOnly` | bool | `false` | HTML mode — drop images served from third-party hosts (ad networks, embeds). |
| `Scraper.RequestDelayMs` | int | `1500` | Milliseconds between consecutive item / article fetches. Avoids tripping per-IP rate limits. |

#### Task Scheduler

```jsonc
"Task Scheduler": { "StartTime": "06:00", "Id": "SitePix-Met" }
```

| Field | Type | Default | Notes |
|---|---|---|---|
| `Task Scheduler.StartTime` | string | `""` | `HH:MM` 24-hour. Empty = no schedule registered. Setting this triggers Task Scheduler (Win) / launchd (mac) / cron (Linux) registration on the next run. |
| `Task Scheduler.Id` | string | `"SitePix"` | Identifier so multiple SitePix profiles on one machine don't collide. |

#### Directories

```jsonc
"Directories": {
  "UseMyPictures": true,
  "Base": "",
  "SubDirectory": "MetMuseum",
  "PhotoText": true
}
```

| Field | Type | Default | Notes |
|---|---|---|---|
| `Directories.UseMyPictures` | bool | `true` | When `true`, save under the OS Pictures folder. When `false`, use `Base`. |
| `Directories.Base` | string | `""` | Custom save root, used only when `UseMyPictures` is `false`. Combined with `SubDirectory`. |
| `Directories.SubDirectory` | string | `"SitePix"` | Folder name appended under the chosen base. |
| `Directories.PhotoText` | bool | `true` | Burn a text overlay (title + today's date) into each saved image. Toggling off saves clean originals. |

#### PhotoText

Overlay rendering. Only relevant when `Directories.PhotoText` is `true`.

| Field | Type | Default | Notes |
|---|---|---|---|
| `PhotoText.Font` | string | `"Helvetica Neue"` | Font family. Falls back to `SKTypeface.Default` if the requested family isn't installed. |
| `PhotoText.TitleFontScale` | number | `1.0` | Multiplier applied to the auto-fitted title font size. |
| `PhotoText.SubtitleFontScale` | number | `1.0` | Same for the subtitle (`og:description` in HTML mode; per-source metadata in API mode). |
| `PhotoText.DateInclude` | bool | `true` | Append today's date to the title. |
| `PhotoText.DateFormat` | string | `"MM/dd"` | .NET date-format string. |
| `PhotoText.DatePrefix` | string | `" - "` | Inserted between the title and the date. |
| `PhotoText.ImageFileName` | bool | `false` | Add the source filename on a second line (debugging aid). |
| `PhotoText.PanelOpacity` | int (-1 to 255) | `210` | `-1` = no panel, no outline. `0` = outline only. `1..255` = dark panel opacity behind the text. |
| `PhotoText.BrandColors` | string[] | `["#000000","#FFFFFF",…]` | Hex colors. SitePix picks whichever has the best contrast against the panel/background; an automatic black/white stroke is drawn around the glyphs for readability. |

#### LogLevel

Standard Microsoft.Extensions.Logging filter — `Default`, `Microsoft`,
`System` keys, values `Information` / `Warning` / etc.

### Bundled samples

10 ready-to-use profiles ship next to the binary under
[`samples/`](samples/), and each is published as a standalone download
URL on the [latest release](https://github.com/alexreich/SitePix/releases/latest):

    https://github.com/alexreich/SitePix/releases/latest/download/<filename>

API-based sources (no HTML scraping; fast, polite, structured metadata):

| Sample | Source | Auth | Notes |
|---|---|---|---|
| [`metmuseum.org.json`](samples/metmuseum.org.json) | [Met Museum Open Access](https://www.metmuseum.org/about-the-met/policies-and-documents/open-access) | none | Default. CC0, ~half a million artworks. |
| [`si.edu.json`](samples/si.edu.json) | [Smithsonian Open Access](https://www.si.edu/openaccess) | free key — [api.data.gov](https://api.data.gov/signup/) | CC0, art + natural history (filtered to art-collection units by default). |
| [`nasa.gov.json`](samples/nasa.gov.json) | [NASA Image Library](https://images.nasa.gov/) | none | Public-domain space and science imagery; per-item carve-outs noted in the sample header. |
| [`loc.gov.json`](samples/loc.gov.json) | [Library of Congress photos](https://www.loc.gov/photos/) | none | Historical photographs; rights vary per item. |
| [`flickr.com.json`](samples/flickr.com.json) | [Flickr Commons](https://www.flickr.com/commons) | free key — [Flickr API](https://www.flickr.com/services/apps/create/apply/) | "No known restrictions" institutional pool. |
| [`nypl.org.json`](samples/nypl.org.json) | [NYPL Digital Collections](https://digitalcollections.nypl.org/) | free token — [api.repo.nypl.org](https://api.repo.nypl.org/) | NYPL's public-domain holdings. |

HTML-scraping sources (Playwright-driven; for blogs and editorial sites):

| Sample | Source | Notes |
|---|---|---|
| [`kadampa.org.json`](samples/kadampa.org.json) | [kadampa.org/news](https://kadampa.org/news) | Buddhist news. Original profile. |
| [`petapixel.com.json`](samples/petapixel.com.json) | [petapixel.com](https://petapixel.com) | Photography news, ~1.5M monthly readers. |
| [`atlasobscura.com.json`](samples/atlasobscura.com.json) | [atlasobscura.com](https://www.atlasobscura.com) | Travel curiosities and long-form photo essays. |
| [`thephoblographer.com.json`](samples/thephoblographer.com.json) | [thephoblographer.com](https://www.thephoblographer.com) | Photo-gear reviews and sample galleries. |

The wizard offers all of these (Met first, then the other API sources,
then HTML scrapers). Drop your own `*.json` file into the bundled
`samples/` directory and the wizard will list it too with a generic
`(custom sample)` label.

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
