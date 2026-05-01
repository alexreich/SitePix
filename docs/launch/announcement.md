# SitePix v1.0.0 launch announcement

Templates for posting once the v1.0.0 retag finishes and the package
managers have approved the submissions. Pick one, edit to taste, post.

---

## Show HN (Hacker News)

**Title:** Show HN: SitePix – fetch news-blog photos for your screensaver

**Body:**

I built SitePix as a desktop tool that pulls large photos from
WordPress-style news blogs and drops them in a folder your OS screen
saver or wallpaper rotation can point at. It's the cross-platform
generalization of a Windows app I'd been running for myself for years
to mirror photos from kadampa.org/news; once I rewrote it on .NET 10
with SkiaSharp + Playwright I realized the same engine works on any
WP site with dated permalinks, so I shipped six bundled profiles
(petapixel.com, atlasobscura.com, fstoppers.com, thephoblographer.com,
smashingmagazine.com, kadampa.org) and made everything else config-
driven via JSON.

What it does on first run:
1. Headless Chromium loads the index page and the first N articles.
2. Pulls every <img> inside the article body, filters by min-width
   and a per-site URL exclude list.
3. Writes the article title + date + description as an outlined text
   overlay on each image (so they're readable as standalone wallpapers).
4. Registers itself with the host's native scheduler (Task Scheduler /
   launchd / cron / systemd user timer) for a daily refresh.

Distribution: Windows installer + portable + winget + Chocolatey,
macOS tarballs + Homebrew tap, Linux .deb / .rpm / AppImage / portable
tarballs, all signed with SHA-256 sidecars.

Source / license / downloads: https://github.com/alexreich/SitePix
(CC-BY-4.0)

Would love feedback on:
- Themes that don't match the default `<article>.entry-content`
  selector chain — happy to add profiles for sites I haven't tried.
- The first-run UX for picking a screen saver source (currently
  bundled JSON profiles + an interactive picker on macOS).

---

## Reddit — r/selfhosted

**Title:** SitePix: cross-platform CLI that mirrors photos from any
WordPress news blog into your screensaver folder (Win/Mac/Linux,
JSON profiles)

**Body:**

A small open-source utility I just released as v1.0.0:
github.com/alexreich/SitePix

Written in .NET 10, runs on Windows / macOS / Linux (x64 + arm64).
Drop a JSON profile pointing at any WordPress-style news blog with
dated permalinks, and it'll mirror that site's article photos into
`~/Pictures/<site>` daily, ready for your screen saver / wallpaper
rotation to pick up.

Six profiles bundled out of the box (petapixel.com, atlasobscura.com,
fstoppers.com, thephoblographer.com, smashingmagazine.com, kadampa.org).
Adding a new one is one JSON file — schema's in the README.

Native package managers: winget, Chocolatey, Homebrew, .deb, .rpm,
AppImage. Auto-registers with the host scheduler so it just works.

CC-BY-4.0. Issues / PRs welcome.

---

## Reddit — r/wallpapers, r/macapps, r/windowsapps

**Title:** Built a tool that mirrors a news site's photos into your
screensaver folder daily

**Body:**

Long story short: I wanted my screen saver to be daily news photos
without screen-scraping by hand. Couldn't find anything that did this
across platforms, so I wrote one.

Pick a source from the bundled set (petapixel.com, atlasobscura.com,
National-Geographic-style stuff…) or write a one-page JSON profile
for any WP blog. It runs on Windows / Mac / Linux, registers itself
with the OS scheduler, drops large images into ~/Pictures/<site>,
and adds an outlined caption with the article title + date so each
photo stands on its own.

CC-BY-4.0, prebuilt installers for everything:
github.com/alexreich/SitePix

---

## Mastodon / Bluesky / Twitter (≤300 chars)

> Just shipped SitePix v1.0.0 — open-source CLI that mirrors photos
> from any WordPress news blog (PetaPixel, Atlas Obscura, etc.) into
> your screen-saver folder daily. Win/Mac/Linux, JSON profiles, all
> the package managers.
>
> github.com/alexreich/SitePix · CC-BY-4.0

---

## /r/dotnet (developer angle)

**Title:** Show /r/dotnet: SitePix — cross-platform .NET 10 CLI with
SkiaSharp + Playwright + multi-OS packaging in one workflow

**Body:**

Open-sourced SitePix v1.0.0 today. It's a small CLI but the .NET
toolchain pieces might be interesting:

- Single Program.cs targets net10.0, self-contained published for
  win-x64 / osx-arm64 / osx-x64 / linux-x64 / linux-arm64.
- `Microsoft.Playwright` for Cloudflare-fronted scraping, with
  per-OS browser-channel selection (Edge on Windows, bundled
  Chromium elsewhere) so you don't need anything pre-installed.
- `SkiaSharp` replaces `System.Drawing` for the text overlay so it
  actually renders on Linux/macOS — ported the original GDI+ code
  including text wrapping (Skia doesn't ship a wrap helper).
- Cross-platform `TaskRegistration` shells out to schtasks /
  writes a launchd plist / appends a cron line depending on the host.
- One GitHub Actions workflow fans out to win/linux/mac runners,
  builds Inno installer + portable zip + nupkg + AppImage + .deb +
  .rpm + tarballs + Homebrew formula in parallel, attaches them
  directly to the GitHub Release with SHA-256 sidecars (skipping
  Actions artifact storage entirely so the 840 MB-per-run payload
  doesn't blow the Free-plan quota).

github.com/alexreich/SitePix — CC-BY-4.0. Feedback welcome,
especially on the trim story (currently `PublishTrimmed=false` for
Playwright reflection safety; would love to hear from anyone who's
trimmed a Playwright app successfully).

---

## Pre-launch checklist

- [ ] v1.0.0 release CI green; release published with 14+ assets +
      SHA-256 sidecars
- [ ] Repo is public (Settings → General → Visibility)
- [ ] Default branch is `main`
- [ ] LICENSE / README / CHANGELOG / CONTRIBUTING / SECURITY /
      CODE_OF_CONDUCT all present
- [ ] Issue templates render correctly (`New Issue` button on the
      Issues tab shows the bug + feature forms)
- [ ] Topics set on the repo (Settings → About): `dotnet`, `wordpress`,
      `screensaver`, `macos`, `windows`, `linux`, `playwright`,
      `skiasharp`, `csharp`, `cli`
- [ ] Repo description set: "Pulls large photos from WordPress-style
      news blogs for use as a desktop/screen-saver source."
- [ ] Repo website URL set (link to the `releases/latest` page)
- [ ] Pinned the latest release on the repo home page
- [ ] Chocolatey package re-tested and approved (was failing on
      404 while repo private; should now pass)
- [ ] winget submission opened (manual via Komac per
      packaging/README.md)
- [ ] Homebrew tap repo `alexreich/homebrew-tap` created
- [ ] Smoke-tested at least one install path on each OS:
  - [ ] `winget install AlexReich.SitePix` (or Setup.exe download)
  - [ ] `brew install alexreich/tap/sitepix` (or tarball)
  - [ ] `sudo dpkg -i sitepix_1.0.0_amd64.deb` (or AppImage)

## Post-launch checklist

- [ ] Watch GitHub Issues for the first 48 h
- [ ] If a popular profile breaks (theme drift), patch in v1.0.1
- [ ] Roll up the Dependabot PRs into v1.0.1 once stable
- [ ] Track stargazer count and incoming traffic via Insights →
      Traffic
