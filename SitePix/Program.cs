// File: SitePix/Program.cs
using System;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using Microsoft.Playwright;
using SitePix;
using SitePix.Sources;
using SkiaSharp;

// Where the setup wizard saves its config. Always user-writable (LocalAppData
// works on Windows / macOS / Linux), so we never fight Program Files perms.
string UserConfigPath() => Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "SitePix", "appsettings.json");

// Config resolution — first match wins:
//   `--setup` / `-s`         → run wizard, write to user config dir, use it
//   <positional CLI arg>     → use that file (absolute, CWD-relative, or
//                              binary-relative — preserves the dev workflow
//                              of `sitepix samples/foo.json`)
//   <BaseDirectory>/appsettings.json
//   <BaseDirectory>/sitepix.json    (legacy filename)
//   <LocalAppData>/SitePix/appsettings.json   (where the wizard writes)
//   nothing → run the wizard if interactive, else exit with an error
//
// We resolve relative to the binary's own directory, NOT the current working
// directory, because Task Scheduler / launchd / cron all run with a CWD
// that's nowhere near the install path.
string configPath;
bool forceSetup = args.Length > 0 && (args[0] == "--setup" || args[0] == "-s");

if (forceSetup)
{
    configPath = SetupWizard.Run(UserConfigPath());
}
else if (args.Length > 0)
{
    if (File.Exists(args[0]))
        configPath = args[0];
    else if (File.Exists(Path.Combine(AppContext.BaseDirectory, args[0])))
        configPath = Path.Combine(AppContext.BaseDirectory, args[0]);
    else
    {
        Console.Error.WriteLine($"Config file not found: {args[0]}");
        Environment.Exit(1);
        return;
    }
}
else
{
    string appSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
    string sitePixPath = Path.Combine(AppContext.BaseDirectory, "sitepix.json");
    string userPath = UserConfigPath();

    if (File.Exists(appSettingsPath))
        configPath = appSettingsPath;
    else if (File.Exists(sitePixPath))
        configPath = sitePixPath;
    else if (File.Exists(userPath))
        configPath = userPath;
    else if (!Console.IsInputRedirected)
        configPath = SetupWizard.Run(userPath);
    else
    {
        Console.Error.WriteLine(
            $"No config file found. Searched:\n  {appSettingsPath}\n  {sitePixPath}\n  {userPath}\n" +
            "Run interactively to use the setup wizard, or pass a config path on the command line.");
        Environment.Exit(1);
        return;
    }
}

HttpClient client = new HttpClient();
// Some catalog APIs (LoC, Smithsonian, NYPL) reject the default .NET UA. Use
// the same browser-shaped UA the Playwright path advertises — harmless for
// existing image downloads and good citizenship for API hosts.
client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", GetUserAgent());
ILogger<Program> logger = null!;
IConfigurationRoot configuration = new ConfigurationBuilder()
    .AddJsonFile(configPath, optional: false, reloadOnChange: false)
    .Build();

// Cross-platform task scheduling
TaskRegistration.EnsureDailyTaskIfConfigured(configuration, null);

int linkDepth = configuration.GetValue<int>("Policies:LinkDepth");
int retentionDays = configuration.GetValue<int>("Policies:RetentionDays");
string baseDirectory = configuration.GetValue<string>("Directories:Base") ?? "";
string subDirectory = configuration.GetValue<string>("Directories:SubDirectory") ?? "SitePix";
string fontName = configuration.GetValue<string>("PhotoText:Font") ?? "sans-serif";
double titleFontScale = configuration.GetValue<double?>("PhotoText:TitleFontScale") ?? 1.0;
double subtitleFontScale = configuration.GetValue<double?>("PhotoText:SubtitleFontScale") ?? 1.0;

// ─── Scraper config (all site-specific tuning lives here) ────────────────────
// {Year} is substituted with the current 4-digit year so a profile stays
// evergreen without being edited every January.
int currentYear = DateTime.Now.Year;
string urlPatternRaw = (configuration.GetValue<string>("Scraper:UrlPattern") ?? "/{Year}/")
    .Replace("{Year}", currentYear.ToString());
var urlRegex = new Regex(urlPatternRaw, RegexOptions.Compiled | RegexOptions.IgnoreCase);

string[] imageUrlExcludes = configuration.GetSection("Scraper:ImageUrlExcludes").Get<string[]>()
    ?? Array.Empty<string>();
int minWidthPx = configuration.GetValue<int?>("Scraper:MinWidthPx") ?? 1024;
string[] contentSelectors = configuration.GetSection("Scraper:ContentSelectors").Get<string[]>()
    ?? new[]
    {
        "main article .entry-content",
        "article .entry-content",
        "main article",
        "article",
        "body"
    };

// CSS selectors whose contents are skipped — keeps related-articles widgets,
// newsletter signups, sponsor blocks, share bars, etc. from polluting the
// download list. Empty by default (backwards-compatible); profiles opt in.
string[] contentExcludeSelectors = configuration.GetSection("Scraper:ContentExcludeSelectors").Get<string[]>()
    ?? Array.Empty<string>();

// When true, images hosted on third-party domains (CDNs, ad networks, social
// embeds) are dropped — only same-site images survive. Off by default.
bool sameOriginOnly = configuration.GetValue<bool?>("Scraper:SameOriginOnly") ?? false;

// Delay between consecutive article-page fetches, in milliseconds. Trips
// less aggressive per-IP rate limits (fstoppers serves a "Too Many Requests"
// stub when hit too fast). Default 1500 ms; set 0 to disable.
int requestDelayMs = configuration.GetValue<int?>("Scraper:RequestDelayMs") ?? 1500;

// Brand colors for text overlay — configurable per profile, with a neutral
// fallback set (Kadampa palette) if unspecified.
List<SKColor> brandColors = (configuration.GetSection("PhotoText:BrandColors").Get<string[]>()
    ?? new[] { "#224486", "#A99886", "#66B9C4", "#358DCB", "#BE303C", "#48ADF4" })
    .Select(hex => SKColor.Parse(hex))
    .ToList();

// Overlay panel mode: -1 = no panel/no outline, 0 = outline only, 1..255 = panel opacity.
int panelOpacity = Math.Clamp(
    configuration.GetValue<int?>("PhotoText:PanelOpacity") ?? 210, -1, 255);

if (configuration.GetValue<bool>("Directories:UseMyPictures"))
{
    baseDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), subDirectory);
}
else
{
    baseDirectory = Path.Combine(baseDirectory, subDirectory);
}

using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
    builder.AddSimpleConsole(options =>
    {
        options.IncludeScopes = true;
        options.SingleLine = true;
        options.TimestampFormat = "HH:mm:ss ";
    }));

logger = loggerFactory.CreateLogger<Program>();
logger.LogInformation("Using config file: {ConfigPath}", configPath);

Directory.CreateDirectory(baseDirectory);

// Store the URL history log in the OS app-data folder, not alongside images.
// Windows: %LOCALAPPDATA%\SitePix\<SubDirectory>\VisitedUrls.log
// macOS:   ~/Library/Application Support/SitePix/<SubDirectory>/VisitedUrls.log
// Linux:   ~/.local/share/SitePix/<SubDirectory>/VisitedUrls.log
string urlLogDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "SitePix",
    subDirectory);
Directory.CreateDirectory(urlLogDir);
string urlLogFile = Path.Combine(urlLogDir, "VisitedUrls.log");
UrlLogger urlLogger = new UrlLogger(urlLogFile);

// Cleanup old URL logs
urlLogger.Cleanup(30);

// Per-image work shared by both modes (HTML scrape + JSON API). Closes over
// configuration / logger / paint settings; called once per "page" or "API
// item" with the image set + the title/subtitle to overlay. itemIdHint is
// used by API mode to disambiguate filenames — many catalog APIs serve
// images via opaque endpoints (Smithsonian's `/ids/download?id=...`, LoC
// IIIF's `/full/.../default.jpg`) where Path.GetFileName collapses every
// download to the same name.
void ProcessImageBatch(IEnumerable<string> imageUrls, string title, string? overlaySubtitle, string? itemIdHint = null)
{
    Parallel.ForEach(imageUrls, imageUrl =>
    {
        try
        {
            // Strip query string before deriving the local filename. Drupal
            // CDNs (fstoppers cdn.fstoppers.com) sign URLs with `?itok=<token>`
            // and `?` is illegal in Windows filenames — without this, every
            // download silently fails.
            string urlPathOnly;
            try { urlPathOnly = new Uri(imageUrl).LocalPath; }
            catch { urlPathOnly = imageUrl.Split('?')[0]; }
            string fileName = Path.GetFileName(urlPathOnly);

            // No usable extension on the path? Try the `id=` query param —
            // Smithsonian's IDS URLs put the original filename there.
            if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrEmpty(Path.GetExtension(fileName)))
            {
                try
                {
                    var uri = new Uri(imageUrl);
                    foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
                    {
                        int eq = pair.IndexOf('=');
                        if (eq < 0) continue;
                        if (string.Equals(pair.Substring(0, eq), "id", StringComparison.OrdinalIgnoreCase))
                        {
                            string val = Uri.UnescapeDataString(pair.Substring(eq + 1));
                            string candidate = Path.GetFileName(val);
                            if (!string.IsNullOrWhiteSpace(candidate)) fileName = candidate;
                            break;
                        }
                    }
                }
                catch { }
            }

            // Last resort: assume JPEG. Without a known image extension the
            // retention sweep at the end of the run deletes the file.
            if (string.IsNullOrEmpty(Path.GetExtension(fileName)))
                fileName = (string.IsNullOrWhiteSpace(fileName) ? "image" : fileName) + ".jpg";

            DateTime futureDate = new DateTime(9999, 12, 31);
            DateTime publishedDate = DateTime.UtcNow;
            TimeSpan dateDifference = futureDate - publishedDate;
            long reverseOrder = dateDifference.Days;

            string identifier = reverseOrder.ToString("0000000");

            // Inject the item ID so two items downloaded the same day with the
            // same URL filename (LoC IIIF `default.jpg`, Smithsonian
            // `download.jpg`) don't clobber each other.
            string idPart = "";
            if (!string.IsNullOrWhiteSpace(itemIdHint))
            {
                var invalid = Path.GetInvalidFileNameChars();
                var safe = new string(itemIdHint.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
                if (safe.Length > 60) safe = safe.Substring(0, 60);
                idPart = safe + "_";
            }

            fileName = identifier + "_" + idPart + fileName;

            string savePath = Path.Combine(baseDirectory, fileName);

            // Download the image
            DownloadFile(imageUrl, savePath).Wait();

            if (!File.Exists(savePath)) { return; }

            // Check image dimensions
            byte[] imageBytes = File.ReadAllBytes(savePath);
            bool deleteImage = false;
            using (var memoryStream = new MemoryStream(imageBytes))
            using (var bitmap = SKBitmap.Decode(memoryStream))
            {
                if (bitmap == null || bitmap.Width < minWidthPx)
                {
                    deleteImage = true;
                }
                else
                {
                    logger.LogInformation($"Downloaded image: {fileName}");
                }
            }

            if (deleteImage)
            {
                File.Delete(savePath);
                logger.LogWarning($"Deleted image: {fileName} because it was smaller than {minWidthPx}px");
            }
            else
            {
                if (configuration.GetValue<bool>("Directories:PhotoText"))
                {
                    // Add text to image using SkiaSharp
                    using var bitmap = SKBitmap.Decode(savePath);
                    if (bitmap != null)
                    {
                        using var canvas = new SKCanvas(bitmap);

                        string imageNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
                        string textToAdd = $"{title}";
                        if (configuration.GetValue<bool>("PhotoText:DateInclude"))
                        {
                            textToAdd += configuration.GetValue<string>("PhotoText:DatePrefix");
                            textToAdd += $"{DateTime.UtcNow.ToString(configuration.GetValue<string>("PhotoText:DateFormat"))}";
                        }
                        if (configuration.GetValue<bool>("PhotoText:ImageFileName"))
                        {
                            textToAdd += $"\n{imageNameWithoutExtension}";
                        }

                        DrawTextOnImage(canvas, bitmap, textToAdd, fontName, brandColors, true, panelOpacity, titleFontScale);
                        DrawTextOnImage(canvas, bitmap, overlaySubtitle, fontName, brandColors, false, panelOpacity, subtitleFontScale);

                        canvas.Flush();

                        // Save back to file
                        var format = GetImageFormat(savePath);
                        using var image = SKImage.FromBitmap(bitmap);
                        using var data = image.Encode(format, 95);
                        using var stream = File.OpenWrite(savePath);
                        stream.SetLength(0);
                        data.SaveTo(stream);
                    }

                    try { File.SetCreationTime(savePath, DateTime.UtcNow); } catch { /* not supported on all platforms */ }
                    File.SetLastWriteTime(savePath, DateTime.UtcNow);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError($"Error downloading image: {imageUrl}. Error: {ex.Message}");
        }
    });
}

// Apply image-URL exclude patterns the same way for either mode.
List<string> FilterByExcludes(IEnumerable<string> urls)
{
    var result = new List<string>();
    foreach (var u in urls)
    {
        if (string.IsNullOrEmpty(u)) continue;
        string lower = u.ToLowerInvariant();
        bool excluded = false;
        foreach (var ex in imageUrlExcludes)
        {
            if (!string.IsNullOrEmpty(ex) && lower.Contains(ex.ToLowerInvariant()))
            {
                excluded = true;
                break;
            }
        }
        if (!excluded) result.Add(u);
    }
    return result;
}

// ─── Mode dispatch ───────────────────────────────────────────────────────────
// API mode (JSON catalog) vs HTML mode (Playwright + DOM scraping). API mode
// activates when `Source:Provider` is set; otherwise the original scraping
// path runs unchanged so existing profiles (kadampa, fstoppers, …) keep
// working.
var apiSource = ApiSourceFactory.TryCreate(configuration, client, logger);
if (apiSource != null)
{
    string providerName = configuration.GetValue<string>("Source:Provider") ?? "?";
    logger.LogInformation("Using API source: {Provider}", providerName);

    int processed = 0;
    await foreach (var item in apiSource.FetchAsync(linkDepth))
    {
        if (processed >= linkDepth) break;
        if (urlLogger.AlreadyVisited(item.SourceUrl)) continue;

        // Same pacing as HTML mode — public APIs throttle too.
        if (processed > 0 && requestDelayMs > 0)
        {
            await Task.Delay(requestDelayMs);
        }

        urlLogger.LogUrl(item.SourceUrl);

        var filtered = FilterByExcludes(item.ImageUrls);
        if (filtered.Count == 0)
        {
            logger.LogWarning("No usable images for item: {Url}", item.SourceUrl);
            continue;
        }

        ProcessImageBatch(filtered, CleanText(item.Title), item.Subtitle, item.Id);
        processed++;
    }

    logger.LogInformation("API source processed {Count} item(s).", processed);
}
else
{
    // HTML mode (the original Playwright path). StartPage is required here.
    string webpageUrl = configuration.GetValue<string>("StartPage") ?? "";
    if (string.IsNullOrWhiteSpace(webpageUrl))
    {
        Console.Error.WriteLine("StartPage is not configured. Set \"StartPage\" in the config file (or set Source:Provider to use an API source).");
        Environment.Exit(1);
    }

    logger.LogInformation("Starting download of webpage");

    // Extract page URLs from the start page via a real browser. Anchors are
    // pulled from the live DOM after a scroll-driven hydration pass — the raw
    // HTML on JS-rendered indexes (fstoppers /potd) often only ships one
    // <a> until the gallery script runs.
    var startPageLinks = await LoadStartPageLinksAsync(webpageUrl);
    var pageUrls = startPageLinks
        .Where(h => Uri.TryCreate(h, UriKind.Absolute, out _))
        .Where(h => !urlLogger.AlreadyVisited(h))
        .ToList();

    if (pageUrls.Count == 0)
    {
        logger.LogError("No page URLs found in the HTML");
    }
    else
    {
        logger.LogInformation("URL pattern: {Pattern}", urlPatternRaw);
        var matchingUrls = pageUrls.Where(u => urlRegex.IsMatch(u)).Distinct().ToList();
        logger.LogInformation("Found {Total} hrefs on start page, {Matching} match the URL pattern",
            pageUrls.Count, matchingUrls.Count);

        int pageCount = 0;
        foreach (string pageUrl in matchingUrls)
        {
            if (pageCount == linkDepth) break;

            // Be polite — pacing requests avoids tripping per-IP rate limits
            // (fstoppers serves a "Too Many Requests" stub when hit too fast).
            if (pageCount > 0 && requestDelayMs > 0)
            {
                await Task.Delay(requestDelayMs);
            }

            urlLogger.LogUrl(pageUrl);

            var (innerHtml, imageUrls) = await LoadContentAndImagesAsync(pageUrl);

            if (imageUrls == null || imageUrls.Count == 0)
            {
                logger.LogWarning($"No images found on page: {pageUrl}");
                continue;
            }

            var filteredImageUrls = FilterByExcludes(imageUrls);
            if (sameOriginOnly)
            {
                filteredImageUrls = filteredImageUrls.Where(u => IsSameSite(u, webpageUrl)).ToList();
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(innerHtml);

            var ogDescription = doc.DocumentNode.SelectSingleNode("//meta[@property='og:description']")?.GetAttributeValue("content", string.Empty);
            var title = CleanText(doc.DocumentNode.SelectSingleNode("//meta[@property='og:title']")?.GetAttributeValue("content", string.Empty));

            ProcessImageBatch(filteredImageUrls, title, ogDescription);

            pageCount++;
        }
    }
}

// Get the current date
DateTime currentDate = DateTime.Now;

// Get all files in the directory
var files = Directory.GetFiles(baseDirectory);

// Filter out files that are older than retentionDays or not images or videos
foreach (string file in files)
{
    FileInfo fileInfo = new FileInfo(file);
    if ((currentDate - fileInfo.LastWriteTime).TotalDays > retentionDays ||
        !new[] { ".jpg", ".jpeg", ".gif", ".bmp", ".png", ".mp4", ".log" }.Contains(fileInfo.Extension))
    {
        try
        {
            File.Delete(file);
            logger.LogInformation($"Deleted old file: {file}");
        }
        catch (Exception ex)
        {
            logger.LogError($"Error deleting file: {file}. Error: {ex.Message}");
        }
    }
}

// Cleanup again
urlLogger.Cleanup(30);

// Linux GNOME only: regenerate the slideshow XML so today's downloads join
// the rotation. No-op everywhere else, and no-op on Linux unless the wizard
// has previously wired this folder up.
SlideshowConfigurator.RefreshLinuxIfConfigured(baseDirectory);

// Final hint for interactive runs only — keeps scheduled / piped runs quiet.
if (!Console.IsInputRedirected)
{
    Console.WriteLine();
    Console.WriteLine($"Done. Images saved to: {baseDirectory}");
    Console.WriteLine("Run with --setup to reconfigure (source, save folder, schedule, overlay, etc.).");
}


// ─── Helper methods ──────────────────────────────────────────────────────────

/// <summary>
/// Returns the appropriate Playwright browser channel for the current OS.
/// Windows uses Edge; other platforms use Playwright's bundled Chromium.
/// </summary>
string? GetBrowserChannel()
{
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        return "msedge";
    // On Linux/macOS, null = use Playwright's bundled Chromium
    return null;
}

/// <summary>
/// Returns a current-Chrome user agent. A bare default .NET / Playwright UA
/// gets refused by several catalog APIs (LoC, Smithsonian) and degrades the
/// HTML response on a few CDNs, so we identify as a normal browser.
/// </summary>
string GetUserAgent()
{
    if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        return "Mozilla/5.0 (Macintosh; Intel Mac OS X 14_5) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Safari/537.36";
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        return "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Safari/537.36";
    return "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Safari/537.36";
}

/// <summary>
/// Standard headers a real browser sends with a top-level navigation. Set
/// here because Playwright's default context omits a few of them and some
/// servers reply with a stripped-down page when they're missing.
/// </summary>
Dictionary<string, string> GetExtraHttpHeaders() => new()
{
    ["Accept-Language"] = "en-US,en;q=0.9",
    ["Accept"] = "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8",
    ["Upgrade-Insecure-Requests"] = "1"
};

async Task<(string htmlContent, List<string> imageUrls)> LoadContentAndImagesAsync(string url)
{
    using var playwright = await Playwright.CreateAsync();
    var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
    {
        Channel = GetBrowserChannel(),
        Headless = true
    });

    var context = await browser.NewContextAsync(new BrowserNewContextOptions
    {
        UserAgent = GetUserAgent(),
        ViewportSize = new ViewportSize { Width = 1920, Height = 1080 },
        Locale = "en-US",
        ExtraHTTPHeaders = GetExtraHttpHeaders()
    });

    var page = await context.NewPageAsync();
    // DOMContentLoaded (not NetworkIdle) — ad/tracker-heavy sites like
    // petapixel.com never reach true network idle. We rely on the
    // WaitForSelectorAsync + 2 s sleep below to confirm content is ready.
    await page.GotoAsync(url, new PageGotoOptions
    {
        WaitUntil = WaitUntilState.DOMContentLoaded,
        Timeout = 60000
    });

    await page.WaitForSelectorAsync("body", new PageWaitForSelectorOptions { Timeout = 10000 });
    await page.WaitForTimeoutAsync(2000);

    // Trigger lazy-loaded images by scrolling to the bottom, then back to top.
    // Sites like petapixel.com only swap data-src → src once images enter the
    // viewport, so without this we'd grab placeholder pixels for everything
    // below the fold.
    await page.EvaluateAsync(@"
        async () => {
            const sleep = ms => new Promise(r => setTimeout(r, ms));
            const step = Math.max(400, Math.floor(window.innerHeight * 0.9));
            for (let y = 0; y < document.body.scrollHeight; y += step) {
                window.scrollTo(0, y);
                await sleep(150);
            }
            window.scrollTo(0, 0);
            await sleep(300);
        }
    ");

    string content = await page.ContentAsync();

    // Best-effort wait for content imagery. Petapixel surfaces editorial
    // shots inside <figure>; Drupal photo galleries (fstoppers /media/) use
    // bare <img>; some pages have neither for a few seconds. Try both, but
    // *never* fail the whole crawl if the wait times out — the scroll loop
    // and 2 s sleep above are usually enough.
    try
    {
        await page.WaitForSelectorAsync("figure, article img, main img",
            new PageWaitForSelectorOptions
            {
                Timeout = 8000,
                State = WaitForSelectorState.Attached
            });
    }
    catch (TimeoutException) { /* proceed with whatever loaded */ }

    // Ship the configured selector lists into the page context so each profile
    // can aim scraping at a different theme's content container, and prune
    // related-articles / newsletter / ad widgets that sit inside it.
    string selectorsJson = JsonSerializer.Serialize(contentSelectors);
    string excludeJson = JsonSerializer.Serialize(contentExcludeSelectors);
    var images = await page.EvaluateAsync<string[]>($@"
        () => {{
            const selectors = {selectorsJson};
            const excludeSelectors = {excludeJson};
            let root = null;
            for (const sel of selectors) {{
                root = document.querySelector(sel);
                if (root) break;
            }}
            if (!root) root = document.body;

            // Pick the largest URL for each img: prefer the widest srcset
            // candidate, then currentSrc, then a chain of common lazy-load
            // attributes used by WordPress / Jetpack / lazysizes / etc.
            const pickFromSrcset = (srcset) => {{
                if (!srcset) return '';
                const parts = srcset.split(',').map(s => s.trim()).filter(Boolean);
                let bestUrl = '';
                let bestW = -1;
                for (const part of parts) {{
                    const segs = part.split(/\s+/);
                    const url = segs[0];
                    let w = 0;
                    for (let i = 1; i < segs.length; i++) {{
                        const m = segs[i].match(/^(\d+)w$/);
                        if (m) w = parseInt(m[1], 10);
                    }}
                    if (url && w > bestW) {{ bestW = w; bestUrl = url; }}
                }}
                return bestUrl;
            }};

            const pickSrc = (img) => {{
                const fromSrcset = pickFromSrcset(
                    img.getAttribute('srcset') || img.getAttribute('data-srcset') || '');
                if (fromSrcset) return fromSrcset;
                return img.currentSrc ||
                    img.src ||
                    img.getAttribute('data-src') ||
                    img.getAttribute('data-lazy-src') ||
                    img.getAttribute('data-original') ||
                    img.getAttribute('data-full-src') ||
                    '';
            }};

            const isInNavOrMenu = (img) =>
                !!img.closest(
                    'header, nav, footer, [role=navigation], ' +
                    '.menu, .mega-menu, .mobile-menu, .offcanvas, ' +
                    '#menu, #site-navigation, #mobile-menu'
                );

            const isInExcluded = (img) => {{
                if (!excludeSelectors || excludeSelectors.length === 0) return false;
                for (const sel of excludeSelectors) {{
                    try {{ if (img.closest(sel)) return true; }} catch (e) {{ }}
                }}
                return false;
            }};

            const urls = Array.from(root.querySelectorAll('img'))
                .filter(img => !isInNavOrMenu(img))
                .filter(img => !isInExcluded(img))
                .map(pickSrc)
                .filter(Boolean);

            return Array.from(new Set(urls));
        }}
    ");

    await browser.CloseAsync();
    return (content, images.Where(src => !string.IsNullOrWhiteSpace(src)).Distinct().ToList());
}

// Same-site check: image host equals the start-page host or is a subdomain of
// it. `www.` is stripped on both sides so `www.petapixel.com` still matches
// `cdn.petapixel.com`. Used by Scraper:SameOriginOnly to drop ad-network /
// social-embed images entirely.
static bool IsSameSite(string imageUrl, string startUrl)
{
    try
    {
        string Normalize(string host)
        {
            host = host.ToLowerInvariant();
            return host.StartsWith("www.") ? host.Substring(4) : host;
        }
        var imgHost = Normalize(new Uri(imageUrl).Host);
        var startHost = Normalize(new Uri(startUrl).Host);
        return imgHost == startHost || imgHost.EndsWith("." + startHost);
    }
    catch
    {
        return false;
    }
}

async Task DownloadFile(string url, string outputPath)
{
    logger.LogInformation(url, outputPath);
    byte[] data = await client.GetByteArrayAsync(url);

    if (File.Exists(outputPath))
    {
        File.Delete(outputPath);
    }

    await File.WriteAllBytesAsync(outputPath, data);
}

// Loads the index/start page in a real browser, scrolls to trigger any
// JS-driven lazy population (fstoppers /potd is hydrated client-side — the
// initial HTML contains a single <a> until the gallery script runs), then
// returns every anchor's resolved absolute href via DOM. Avoids the
// regex-on-raw-HTML approach which misses SPA-injected links.
async Task<List<string>> LoadStartPageLinksAsync(string url)
{
    using var playwright = await Playwright.CreateAsync();

    var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
    {
        Channel = GetBrowserChannel(),
        Headless = true
    });

    var context = await browser.NewContextAsync(new BrowserNewContextOptions
    {
        UserAgent = GetUserAgent(),
        ViewportSize = new ViewportSize { Width = 1920, Height = 1080 },
        Locale = "en-US",
        ExtraHTTPHeaders = GetExtraHttpHeaders()
    });

    var page = await context.NewPageAsync();

    await page.GotoAsync(url, new PageGotoOptions
    {
        WaitUntil = WaitUntilState.DOMContentLoaded,
        Timeout = 60000
    });

    await page.WaitForSelectorAsync("body", new PageWaitForSelectorOptions { Timeout = 10000 });
    await page.WaitForTimeoutAsync(2000);

    // Scroll top → bottom in steps so any IntersectionObserver-driven gallery
    // gets a chance to populate. Then back to the top so a second pass can see
    // anchors that were only inserted on viewport entry.
    await page.EvaluateAsync(@"
        async () => {
            const sleep = ms => new Promise(r => setTimeout(r, ms));
            const step = Math.max(400, Math.floor(window.innerHeight * 0.9));
            const total = Math.max(document.body.scrollHeight, document.documentElement.scrollHeight);
            for (let y = 0; y < total; y += step) {
                window.scrollTo(0, y);
                await sleep(200);
            }
            window.scrollTo(0, 0);
            await sleep(400);
        }
    ");

    // Best-effort wait specifically for content-style anchors. If the page
    // remains a hydrating shell with only chrome links, this will time out
    // and we'll fall through to whatever the DOM has.
    try
    {
        await page.WaitForSelectorAsync(
            "a[href*='/media/'], a[href*='/photo/'], a[href*='/news/'], a[href*='/article'], main article a",
            new PageWaitForSelectorOptions { Timeout = 15000, State = WaitForSelectorState.Attached });
    }
    catch (TimeoutException) { /* fall through with whatever loaded */ }

    // Detect common throttle / soft-error pages so the user sees *why* they
    // got zero links instead of a silent empty-list. Cheap probe: read title +
    // a snippet of body text and compare to known patterns.
    var probeJson = await page.EvaluateAsync<string>(@"
        () => JSON.stringify({
            title: document.title || '',
            anchorCount: document.querySelectorAll('a[href]').length,
            bodySnippet: (document.body && document.body.innerText)
                ? document.body.innerText.slice(0, 200) : ''
        })
    ");
    var probeLower = probeJson.ToLowerInvariant();
    if (probeLower.Contains("too many requests") ||
        probeLower.Contains("rate limit") ||
        probeLower.Contains("access denied") ||
        probeLower.Contains("are you a robot") ||
        probeLower.Contains("just a moment") ||
        probeLower.Contains("captcha"))
    {
        logger.LogWarning("Start page looks throttled/blocked: {Probe}", probeJson);
    }

    var links = await page.EvaluateAsync<string[]>(@"
        () => Array.from(document.querySelectorAll('a[href]'))
            .map(a => a.href)
            .filter(h => h && (h.startsWith('http://') || h.startsWith('https://')))
    ");

    await browser.CloseAsync();
    return links.Distinct().ToList();
}


// ─── Image / color helpers (SkiaSharp) ────────────────────────────────────────

SKEncodedImageFormat GetImageFormat(string path)
{
    var ext = Path.GetExtension(path).ToLowerInvariant();
    return ext switch
    {
        ".png" => SKEncodedImageFormat.Png,
        ".gif" => SKEncodedImageFormat.Gif,
        ".bmp" => SKEncodedImageFormat.Bmp,
        ".webp" => SKEncodedImageFormat.Webp,
        _ => SKEncodedImageFormat.Jpeg
    };
}

SKColor FindBestTextColor(SKColor background, List<SKColor> brandColors)
{
    SKColor bestBrand = brandColors[0];
    double bestRatio = 0.0;

    foreach (var brandColor in brandColors)
    {
        double ratio = GetContrastRatio(brandColor, background);
        if (ratio > bestRatio)
        {
            bestRatio = ratio;
            bestBrand = brandColor;
        }
    }

    const double minReadableRatio = 3.0;

    if (bestRatio >= minReadableRatio)
        return bestBrand;

    double blackRatio = GetContrastRatio(SKColors.Black, background);
    double whiteRatio = GetContrastRatio(SKColors.White, background);

    if (blackRatio > whiteRatio)
    {
        if (blackRatio >= minReadableRatio) return SKColors.Black;
        return bestBrand;
    }
    else
    {
        if (whiteRatio >= minReadableRatio) return SKColors.White;
        return bestBrand;
    }
}

double ToRelativeLuminance(SKColor c)
{
    double Rsrgb = c.Red / 255.0;
    double Gsrgb = c.Green / 255.0;
    double Bsrgb = c.Blue / 255.0;

    double R = (Rsrgb <= 0.03928) ? (Rsrgb / 12.92) : Math.Pow((Rsrgb + 0.055) / 1.055, 2.4);
    double G = (Gsrgb <= 0.03928) ? (Gsrgb / 12.92) : Math.Pow((Gsrgb + 0.055) / 1.055, 2.4);
    double B = (Bsrgb <= 0.03928) ? (Bsrgb / 12.92) : Math.Pow((Bsrgb + 0.055) / 1.055, 2.4);

    return 0.2126 * R + 0.7152 * G + 0.0722 * B;
}

double GetContrastRatio(SKColor foreground, SKColor background)
{
    double fLum = ToRelativeLuminance(foreground);
    double bLum = ToRelativeLuminance(background);

    double lighter = Math.Max(fLum, bLum);
    double darker = Math.Min(fLum, bLum);

    return (lighter + 0.05) / (darker + 0.05);
}

SKColor CalculateAverageColor(SKBitmap bmp, int startYPercent, int endYPercent)
{
    int height = bmp.Height;
    int startY = height * startYPercent / 100;
    int endY = height * endYPercent / 100;

    long totalR = 0, totalG = 0, totalB = 0;
    long pixelCount = 0;

    for (int y = startY; y < endY; y++)
    {
        for (int x = 0; x < bmp.Width; x++)
        {
            SKColor c = bmp.GetPixel(x, y);
            totalR += c.Red;
            totalG += c.Green;
            totalB += c.Blue;
            pixelCount++;
        }
    }

    byte avgR = (byte)(totalR / pixelCount);
    byte avgG = (byte)(totalG / pixelCount);
    byte avgB = (byte)(totalB / pixelCount);

    return new SKColor(avgR, avgG, avgB);
}

/// <summary>
/// Word-wraps text to fit within maxWidth using the given font.
/// </summary>
List<string> WrapText(string text, SKFont font, float maxWidth)
{
    var result = new List<string>();
    foreach (var paragraph in text.Split('\n'))
    {
        var words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            result.Add("");
            continue;
        }

        var currentLine = words[0];
        for (int i = 1; i < words.Length; i++)
        {
            var testLine = currentLine + " " + words[i];
            if (font.MeasureText(testLine) > maxWidth)
            {
                result.Add(currentLine);
                currentLine = words[i];
            }
            else
            {
                currentLine = testLine;
            }
        }
        result.Add(currentLine);
    }
    return result;
}

/// <summary>
/// Measures the total height of wrapped text.
/// </summary>
float MeasureWrappedTextHeight(string text, SKFont font, float maxWidth)
{
    var lines = WrapText(text, font, maxWidth);
    return lines.Count * font.Spacing;
}

/// <summary>
/// General function to handle text drawing on images using SkiaSharp.
/// </summary>
void DrawTextOnImage(SKCanvas canvas, SKBitmap bitmap, string? text,
                     string fontName, List<SKColor> brandColors, bool isHeader, int panelOpacity, double fontScale)
{
    if (string.IsNullOrWhiteSpace(text)) return;

    int startPercent = isHeader ? 0 : 85;
    int endPercent = isHeader ? 15 : 100;

    // Layout box — 88 % of width gives breathing room on both sides
    float boxTop = bitmap.Height * startPercent / 100f;
    float boxHeight = bitmap.Height * (endPercent - startPercent) / 100f;
    float boxWidth = bitmap.Width * 0.88f;
    float boxLeft = (bitmap.Width - boxWidth) / 2;

    // When a dark panel is shown, text is chosen for contrast against black so
    // bright/light brand colours and white win naturally.
    // When there is no panel, sample the actual image background instead.
    SKColor colorBase = panelOpacity > 0
        ? SKColors.Black
        : CalculateAverageColor(bitmap, startPercent, endPercent);
    SKColor textColor = FindBestTextColor(colorBase, brandColors);

    // Find the largest font size that still fits the box.
    float scale = Math.Clamp((float)fontScale, 0.5f, 3.0f);
    int initialSize = Math.Max(8, (int)Math.Round((isHeader ? 18 : 13) * scale));
    int bestSize = initialSize;
    int maxSize = Math.Max(initialSize, (int)Math.Round(72 * scale));
    var typeface = SKTypeface.FromFamilyName(fontName) ?? SKTypeface.Default;

    for (int size = initialSize; size <= maxSize; size++)
    {
        using var testFont = new SKFont(typeface, size);
        float measuredHeight = MeasureWrappedTextHeight(text, testFont, boxWidth);
        float maxSingleLineWidth = 0;
        foreach (var line in WrapText(text, testFont, boxWidth))
        {
            float w = testFont.MeasureText(line);
            if (w > maxSingleLineWidth) maxSingleLineWidth = w;
        }
        if (maxSingleLineWidth > boxWidth || measuredHeight > boxHeight) break;
        bestSize = size;
    }

    using var font = new SKFont(typeface, bestSize);
    var wrappedLines = WrapText(text, font, boxWidth);
    float totalTextHeight = wrappedLines.Count * font.Spacing;
    float padding = bestSize * 0.6f;

    float startY = isHeader
        ? boxTop + padding + font.Spacing
        : boxTop + (boxHeight - totalTextHeight) - padding + font.Spacing;

    // Draw semi-transparent dark panel (always dark — universally readable)
    if (panelOpacity > 0)
    {
        float panelTop    = startY - font.Spacing - padding * 0.5f;
        float panelBottom = startY + totalTextHeight + padding * 0.5f;
        float panelLeft   = boxLeft - padding;
        float panelRight  = boxLeft + boxWidth + padding;

        using var panelPaint = new SKPaint
        {
            Color = new SKColor(0, 0, 0, (byte)panelOpacity),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        canvas.DrawRoundRect(
            new SKRoundRect(new SKRect(panelLeft, panelTop, panelRight, panelBottom), padding),
            panelPaint);
    }

    // Outline policy: -1 = none, 0/+ = draw halo outline.
    bool drawOutline = panelOpacity >= 0;

    // Thin white outline (halo) for crispness, then fill
    float strokeWidth = Math.Clamp(bestSize * 0.06f, 1.0f, 3.5f);
    SKColor outlineColor = ToRelativeLuminance(textColor) < 0.5 ? SKColors.White : SKColors.Black;

    using var strokePaint = new SKPaint
    {
        Color = outlineColor,
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = strokeWidth,
        StrokeJoin = SKStrokeJoin.Round
    };
    using var fillPaint = new SKPaint
    {
        Color = textColor,
        IsAntialias = true,
        Style = SKPaintStyle.Fill
    };

    foreach (var line in wrappedLines)
    {
        if (drawOutline)
        {
            canvas.DrawText(line, boxLeft, startY, SKTextAlign.Left, font, strokePaint);
        }
        canvas.DrawText(line, boxLeft, startY, SKTextAlign.Left, font, fillPaint);
        startY += font.Spacing;
    }
}

static string CleanText(string? input)
{
    if (string.IsNullOrEmpty(input))
        return string.Empty;

    string deEntitized = HtmlEntity.DeEntitize(input);

    var doc = new HtmlDocument();
    doc.LoadHtml(deEntitized);
    return doc.DocumentNode.InnerText;
}
