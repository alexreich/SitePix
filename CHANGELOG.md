# Changelog

All notable changes to SitePix are documented here. Format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.0.1] - 2026-05-01

The first publicly-announced release. v1.0.0 was a soft launch — the
binaries were uploaded and submitted to package managers but never
announced anywhere. v1.0.1 rolls in the launch-readiness work and
ships as the version users actually see.

> Strict semver would call this minor (1.0.0 → 1.1.0) because of the
> new wizard, API sources, and slideshow configurator. We're using
> 1.0.1 because v1.0.0 had no public audience to honor a "patch-only"
> contract for. Future feature work resumes proper minor bumps.

### Added
- **First-run interactive setup wizard** (`SitePix/SetupWizard.cs`).
  Asks 7 short questions: source, API key (if needed), save folder,
  images per run, retention days, text overlay, daily schedule. Writes
  a config to the OS-appropriate user-app-data folder
  (`%LOCALAPPDATA%\SitePix\appsettings.json` on Windows;
  `~/Library/Application Support/SitePix/` on macOS;
  `~/.local/share/SitePix/` on Linux). Re-run any time with
  `sitepix --setup`. The wizard discovers all `*.json` files under the
  bundled `samples/` directory and offers them with curated descriptions
  (custom samples are picked up automatically with a generic label).
- **API-based sources.** New JSON-API path runs alongside the existing
  HTML scraping path; activates when `Source:Provider` is set in the
  config. Six providers shipped: Met Museum Open Access (`metmuseum`),
  Smithsonian Open Access (`smithsonian`), Library of Congress (`loc`),
  NASA Image Library (`nasa`), Flickr Commons (`flickrcommons`), NYPL
  Digital Collections (`nypl`). Each picks the largest available
  rendition per item and surfaces title + creator/date metadata for
  the overlay.
- **End-of-run hint** on interactive launches: prints the save folder
  path and reminds the user about `--setup`. Quiet on scheduled or
  piped runs.

### Changed
- **No default `appsettings.json` is bundled with the binary.** Fresh
  installs hit the setup wizard on first launch instead of running
  silently against a baked-in config. The legacy
  `<BaseDirectory>/appsettings.json` and `<BaseDirectory>/sitepix.json`
  paths are still respected if present (back-compat for existing
  installs); the wizard's output at `<LocalAppData>/SitePix/appsettings.json`
  is the new canonical location.
- **Sample configs are now plain JSON** (no `//` line comments, no
  `_comment_*` keys). The README's "Configuration file reference"
  section is now the canonical schema doc — every field the engine
  reads is documented there with type, default, and notes.
- **Removed sources that explicitly require automated-access opt-in or
  whose primary content isn't photo-oriented**:
  `samples/fstoppers.com.json` and `samples/smashingmagazine.com.json`.
- Removed bot-detection-evasion code from the Playwright path
  (navigator/plugins/chrome.runtime masking, automation-flag stripping,
  storage-state persistence). The remaining headers are standard
  browser fingerprint shape only — no fingerprint-evasion-shaped logic.
- Chocolatey nuspec: added `iconUrl` (CDN-cached via jsDelivr to a
  committed `packaging/chocolatey/icon.png`) and removed redundant
  `projectSourceUrl` (the GitHub repo IS the project home — having
  identical `projectUrl` and `projectSourceUrl` is what the Choco
  validator nudges against). Resolves the two Guideline notes from
  v1.0.0 moderator feedback.

### Fixed
- Filename collisions in API mode: catalogs that serve images via
  query-string endpoints (Smithsonian's `?id=NAME.jpg`, LoC IIIF's
  `default.jpg`) used to clobber each other inside one run because
  `Path.GetFileName` produced the same local name for every item.
  Filenames now include the API item's unique ID.
- Config files served from no-extension URLs are auto-detected (parse
  `?id=NAME.jpg` from the query string; fall back to `.jpg`) so the
  retention sweep doesn't immediately delete fresh downloads.

### Security
- Added `.verify/`, `*.local.json`, and `*.secret.json` to `.gitignore`
  so wizard-generated configs containing API keys never accidentally
  enter version control. The supported pattern for production use is
  `Source:ApiKeyEnv` pointing at an environment variable instead of
  inline `Source:ApiKey`.

## [1.0.0] - 2026-04-26

First public release. Forked and renamed from KadampaScreenSaver — same
underlying engine, now generalized to scrape any WordPress-style news
blog with dated permalinks via JSON profiles.

### Added
- Cross-platform .NET 10 binary (Windows, macOS, Linux; x64 and arm64).
- 6 bundled source profiles in [`samples/`](samples/), file basename =
  source domain: `petapixel.com`, `atlasobscura.com`, `fstoppers.com`,
  `thephoblographer.com`, `smashingmagazine.com`, `kadampa.org`.
- New `Scraper` config section: `UrlPattern` (regex with `{Year}`
  substitution), `MinWidthPx`, `ImageUrlExcludes` (array),
  `ContentSelectors` (array of CSS selectors).
- Configurable `PhotoText:BrandColors` array — text overlays match each
  source's palette.
- `Task Scheduler:Id` override so multiple profiles can schedule on one
  machine without label collision.
- Positional CLI arg: `sitepix samples/petapixel.com.json`.
- Outlined text overlay (auto-contrast stroke) for readability over
  mixed-luminance photos.
- Multi-OS packaging in CI: Windows installer (Inno) / portable zip /
  winget manifests / Chocolatey nupkg, macOS tarballs (osx-x64 +
  osx-arm64) + Homebrew formula, Linux AppImage / .deb / .rpm /
  portable tarballs (linux-x64 + linux-arm64).
- Per-OS native scheduling via `TaskRegistration`: Windows Task
  Scheduler, macOS launchd, Linux cron / systemd user timer.
- Interactive source picker at the end of `macos/install.sh`
  (`--source <domain>` for non-interactive).

### Changed
- Rename: `KadampaScreenSaver` → `SitePix` (project, namespace,
  solution, binary, LaunchAgent label `com.sitepix.agent`).
- Playwright navigation switched from `NetworkIdle` to
  `DOMContentLoaded` — ad-heavy sites (e.g. petapixel) never reach
  network idle; the article-selector wait + 2 s sleep is sufficient.

### Fixed
- Inno Setup `[Run]` postinstall removed. Although flagged
  `skipifsilent`, it was tripping the Chocolatey verifier.
  Best practice: package install lays down files only — never invokes
  the binary, never hits the network. The user starts SitePix manually
  from the Start Menu shortcut on first use.
- Release CI: platform jobs now upload directly to the GitHub Release
  via `gh release upload` instead of `actions/upload-artifact`. Frees
  ~840 MB / run from the 500 MB Free-plan Actions storage quota
  (release assets are unmetered).

### Decisions documented
Size of the per-release Linux package set (~517 MB across 7 packages)
is intentional, not an artifact. Three size cuts considered and
rejected for v1.0.0:

- **`PublishTrimmed=true`** — risky on Playwright (reflection-heavy);
  saves ~50 MB/binary but needs a `<TrimmerRootAssembly>` audit and
  test harness. Filed as a future enhancement.
- **Drop `linux-arm64`** — would cut ~310 MB but loses Raspberry Pi,
  Apple Silicon Asahi, AWS Graviton, Oracle Cloud free-tier ARM.
- **Drop AppImage** — would cut ~80 MB but loses the universal
  no-sudo Linux install path that works on every distro.

Release assets are unmetered; the win from these cuts is end-user
download size only, and only the trim option moves the needle there.

[Unreleased]: https://github.com/alexreich/SitePix/compare/v1.0.1...HEAD
[1.0.1]: https://github.com/alexreich/SitePix/releases/tag/v1.0.1
[1.0.0]: https://github.com/alexreich/SitePix/releases/tag/v1.0.0
