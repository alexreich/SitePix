# Changelog

All notable changes to SitePix are documented here. Format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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

[Unreleased]: https://github.com/alexreich/SitePix/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/alexreich/SitePix/releases/tag/v1.0.0
