# SitePix v1.0.0 launch announcement

Templates for posting once the v1.0.0 retag finishes and the package
managers have approved the submissions. Pick one, edit to taste, post.

---

## Show HN (Hacker News)

**Title:** Show HN: SitePix – fetch news-blog photos for your screensaver

**Body:**

I built SitePix as a desktop tool that pulls large photos into a folder
your OS screen saver or wallpaper rotation can point at. It started as
a Windows app I'd been running for myself for years to mirror photos
from kadampa.org/news; once I rewrote it on .NET 10 with SkiaSharp +
Playwright I realized the engine works on any site with dated
permalinks, and the same loop also drives JSON catalog APIs, so I
broadened it to ten bundled sources — six API catalogs (Met Museum
Open Access, Smithsonian Open Access, NASA Image Library, Library of
Congress, Flickr Commons, NYPL Digital Collections) and four HTML
profiles (kadampa.org, petapixel.com, atlasobscura.com,
thephoblographer.com).

What it does on first run:
1. Interactive wizard asks 7 short questions (source, API key if
   needed, save folder, images per run, retention, overlay y/n,
   schedule y/n + time) and writes a config to your local app-data
   folder. Re-run any time with `--setup`.
2. For API sources: hits the catalog endpoint, picks the largest
   rendition per item, surfaces title + creator/date for the overlay.
3. For HTML profiles: headless Chromium loads the index, pulls every
   <img> inside the article body, filters by min-width and a per-site
   URL exclude list.
4. Writes the title + date as an outlined text overlay on each image
   (so they're readable as standalone wallpapers).
5. Registers itself with the host's native scheduler (Task Scheduler /
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

Ten sources bundled out of the box — six API catalogs (Met Museum,
Smithsonian, NASA, Library of Congress, Flickr Commons, NYPL) and four
HTML profiles (kadampa.org, petapixel.com, atlasobscura.com,
thephoblographer.com). First-run wizard walks you through picking
one. Adding a new HTML site is one JSON file — schema's in the README.

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

### Release artifacts
- [ ] Release CI green; release published with 14+ assets +
      SHA-256 sidecars
- [ ] All 10 bundled samples included as standalone download URLs
      (`releases/latest/download/<filename>.json`)
- [ ] Binary ships with `samples/` directory next to it but **no**
      bundled `appsettings.json` — first-run wizard fires for fresh
      installs

### First-run UX
- [ ] Wizard runs cleanly on each OS (Win / mac / Linux) when the
      binary is launched with no config:
  - [ ] All 10 sources show up in the picker, with the API ones
        first and Met as the default
  - [ ] Picking a key-requiring source (Smithsonian / Flickr / NYPL)
        prompts for the key and surfaces the signup URL
  - [ ] Pressing Enter on every prompt produces a Met-default config
        and downloads start
  - [ ] `--setup` flag re-runs the wizard from anywhere
  - [ ] Saved config lands at the OS-correct LocalAppData path
- [ ] End-of-run hint ("Run with `--setup` to reconfigure") appears
      on interactive runs but not on scheduled / piped runs

### Repo hygiene
- [ ] Repo is public (Settings → General → Visibility)
- [ ] Default branch is `main`
- [ ] LICENSE / README / CHANGELOG / CONTRIBUTING / SECURITY /
      CODE_OF_CONDUCT all present
- [ ] README's "Configuration file reference" matches every field the
      samples actually use
- [ ] All 10 sample files are valid plain JSON (no `//` comments)
- [ ] Issue templates render correctly (`New Issue` button on the
      Issues tab shows the bug + feature forms)
- [ ] Topics set on the repo (Settings → About): `dotnet`,
      `screensaver`, `wallpaper`, `macos`, `windows`, `linux`,
      `playwright`, `skiasharp`, `csharp`, `cli`,
      `open-access-api`, `met-museum`, `smithsonian`, `nasa`,
      `library-of-congress`, `flickr-commons`, `nypl`
- [ ] Repo description set: "Private-use CLI that pulls large photos
      from open-access museum APIs and editorial blogs for your
      desktop / screen-saver folder."
- [ ] Repo website URL set (link to the `releases/latest` page)
- [ ] Pinned the latest release on the repo home page

### Distribution
- [ ] Chocolatey package re-tested and approved
- [ ] winget submission opened (manual via Komac per
      packaging/README.md)
- [ ] Homebrew tap repo `alexreich/homebrew-tap` created
- [ ] Smoke-tested at least one install path on each OS, including
      first-run wizard:
  - [ ] `winget install AlexReich.SitePix` (or Setup.exe download)
  - [ ] `brew install alexreich/tap/sitepix` (or tarball)
  - [ ] `sudo dpkg -i sitepix_<version>_amd64.deb` (or AppImage)

### API sanity
- [ ] Each API source pulls at least N=10 items in a fresh run:
  - [ ] Met Museum (no key)
  - [ ] Smithsonian (with a real api.data.gov key)
  - [ ] NASA (no key)
  - [ ] Library of Congress (no key)
  - [ ] Flickr Commons (with a real Flickr key)
  - [ ] NYPL (with a real NYPL token)

## Post-launch checklist

- [ ] Watch GitHub Issues for the first 48 h
- [ ] If a popular sample breaks (theme drift / API shape change),
      patch in a follow-up release
- [ ] Roll up the Dependabot PRs into the next patch once stable
- [ ] Track stargazer count and incoming traffic via Insights →
      Traffic
- [ ] Note which sources users pick most via opt-in telemetry (if
      added later) or by reading discussions / issues
