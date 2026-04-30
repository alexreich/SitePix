# Contributing to SitePix

Thanks for your interest. This is a small-scope tool — the core engine
is one .NET project plus a few scripts — so most changes are a single
PR away.

## Quick build

```bash
# Prereqs: .NET 10 SDK and (for scraping) Microsoft Edge on Windows or
# Playwright's bundled Chromium on macOS/Linux.
dotnet build SitePix/SitePix.csproj

# Self-contained binary for your local platform:
dotnet publish SitePix/SitePix.csproj -c Release -r osx-arm64 \
  --self-contained -p:PublishSingleFile=true -o dist/local

# Install Playwright Chromium (one-time, into ~/Library/Caches/ms-playwright on mac):
cd SitePix && playwright install chromium
```

Sub `osx-x64`, `win-x64`, `linux-x64`, `linux-arm64` for the runtime
identifier as needed.

## Running locally

```bash
./dist/local/SitePix                              # uses bundled appsettings.json
./dist/local/SitePix samples/petapixel.com.json   # any sample profile
```

## Adding a new source profile

Drop a new `samples/<domain>.json`. The schema lives in
[`README.md`](README.md#profile-schema) and a fully-commented template
is in [`SitePix/appsettings.json`](SitePix/appsettings.json).

Keys to think about for a new site:
- `Scraper:UrlPattern` — regex applied to candidate article URLs.
  `{Year}` is substituted with the current 4-digit year at runtime so
  the profile stays evergreen.
- `Scraper:ContentSelectors` — ordered CSS selectors. The first one
  that matches an article page becomes the scope for `<img>`
  extraction. Most WordPress themes work with the default chain.
- `Scraper:ImageUrlExcludes` — case-insensitive substring filters for
  thumbnails, sponsor logos, etc.
- `PhotoText:BrandColors` — hex array; the text-overlay engine picks
  the best contrast per image and adds a black/white outline.

If you add a profile, please include a brief verification note in the
PR: how many images downloaded on a sample run, any quirks of the
site's HTML.

## Coding style

- C#: defaults from the `Microsoft.NET.Sdk`. No StyleCop / EditorConfig
  enforcement (yet).
- Bash scripts: `set -euo pipefail`. Keep them POSIX-ish where the
  shebang allows — `macos/install.sh` deliberately bashes; the
  generated postinst is `/bin/sh`.
- Workflow YAML: comments explain every non-obvious step, especially
  cross-OS quirks.

## Pull requests

- One change per PR is best; multi-change PRs are fine when the parts
  are tightly coupled.
- For non-trivial work, open an issue first to align on direction.
- Reference issues with `Fixes #N` so they auto-close on merge.
- Keep commit messages descriptive; the body should explain the *why*,
  not just the *what*. Co-author trailers welcome.

## Testing

There is currently no automated test suite. Manual validation is the
norm — run the scraper against the affected source and confirm:
- Images land in `~/Pictures/<SubDirectory>` (or the configured Base).
- `VisitedUrls.log` rolls forward (URLs aren't re-scraped on the next
  run within the retention window).
- Files older than `Policies:RetentionDays` are pruned.

Adding a real test harness (e.g. xUnit + canned HTML fixtures for the
scraper, separated from the live network calls) would be welcome —
file an issue.

## Releases

Releases are cut by pushing a `vX.Y.Z` tag whose version matches
`<Version>` in [`SitePix/SitePix.csproj`](SitePix/SitePix.csproj). CI
fans out to win/linux/mac runners and uploads packages directly to the
GitHub Release. See [`packaging/README.md`](packaging/README.md) for
the one-time steps to wire up Chocolatey / winget / Homebrew tap
publishing.

## License

By contributing, you agree your work is licensed under the project's
[CC-BY-4.0](LICENSE).
