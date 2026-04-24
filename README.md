# SitePix

Pulls large photos from WordPress-style news blogs and drops them into a
folder your OS screen saver (or desktop slideshow) can point at. Runs on
**Windows, macOS, and Linux**.

Originally built to gather news imagery from kadampa.org — now
configurable via JSON profiles so the same engine can feed off any
WordPress site with dated permalinks and standard `<article>` /
OpenGraph conventions.

## Requirements
- [.NET 10 Runtime or SDK](https://dotnet.microsoft.com/download)
- Internet access
- **Windows**: Microsoft Edge (used by Playwright for scraping)
- **macOS / Linux**: Playwright's bundled Chromium (installed once via
  `playwright install chromium`)

## Quick start

```bash
# macOS one-liner (installs .NET, publishes binary, fetches once, opens folder)
./macos/install.sh --run
```

Or manually:
```bash
dotnet publish SitePix/SitePix.csproj -c Release -r osx-arm64 --self-contained -o dist/macos
cd SitePix && playwright install chromium
./dist/macos/SitePix                              # uses bundled appsettings.json (kadampa)
./dist/macos/SitePix samples/petapixel.json       # or any other profile
```

Use `osx-x64` on Intel Macs, `win-x64` on Windows, `linux-x64` on Linux.

## Configuration

On startup SitePix reads a JSON config. Pick one:

1. Positional CLI arg — `SitePix path/to/profile.json`
2. `appsettings.json` next to the binary (default)

### Profile schema

```jsonc
{
  "StartPage": "https://kadampa.org/news",

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

Sample profiles live in [`samples/`](samples/):
- [`samples/kadampa.json`](samples/kadampa.json) — Kadampa Buddhist news
- [`samples/petapixel.json`](samples/petapixel.json) — photography news

### Brand colors

`PhotoText:BrandColors` is an array of hex strings. For each text overlay
SitePix picks the color with the best contrast against the background it's
sitting on, then draws an automatic black/white stroke around the glyphs
for readability over mixed-luminance photos.

## Scheduling

Setting `Task Scheduler:StartTime` triggers platform-native scheduling the
next time SitePix runs:

- **Windows**: registers a daily task in Task Scheduler.
- **macOS**: writes `~/Library/LaunchAgents/com.sitepix.agent.plist`.
  Run `launchctl load <path>` once to activate; after that, the agent
  survives reboots. Multiple profiles use distinct labels via
  `Task Scheduler:Id`.
- **Linux**: appends a cron line to the current user's crontab.

## Screen saver setup
- [`macos/PRD.md`](macos/PRD.md) — macOS end-user walkthrough (Classic
  slideshow modules, Gatekeeper, Full Disk Access, verification).
- **Windows**: Settings → Personalization → Lock screen → Screen saver →
  Photos → browse to the configured image directory.
- **Linux**: use your desktop environment's slideshow settings.

## Attribution

SitePix is released under [Creative Commons Attribution 4.0](LICENSE).
If you redistribute, please credit the project and link back.

Scraped images remain the copyright of their original publishers. SitePix
simply downloads what's publicly available on the configured site; please
respect each site's terms of service and robots directives.

## Project history

The project started life as KadampaScreenSaver. See [`XPLATFORM.md`](XPLATFORM.md)
for the cross-platform migration notes (System.Drawing → SkiaSharp,
dynamic Playwright channel, per-OS scheduling).
