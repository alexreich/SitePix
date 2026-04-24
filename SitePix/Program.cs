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
using SkiaSharp;

// First positional CLI arg selects a config profile file — e.g.
// `sitepix samples/petapixel.json`. Otherwise fall back to appsettings.json
// next to the binary (or in the cwd, which dotnet Configuration searches by default).
string configPath = "appsettings.json";
if (args.Length > 0)
{
    if (!File.Exists(args[0]))
    {
        Console.Error.WriteLine($"Config file not found: {args[0]}");
        Environment.Exit(1);
    }
    configPath = args[0];
}

HttpClient client = new HttpClient();
ILogger<Program> logger = null!;
IConfigurationRoot configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile(configPath, optional: true, reloadOnChange: false)
    .Build();

// Cross-platform task scheduling
TaskRegistration.EnsureDailyTaskIfConfigured(configuration, null);

int linkDepth = configuration.GetValue<int>("Policies:LinkDepth");
int retentionDays = configuration.GetValue<int>("Policies:RetentionDays");
string baseDirectory = configuration.GetValue<string>("Directories:Base") ?? "";
string subDirectory = configuration.GetValue<string>("Directories:SubDirectory") ?? "SitePix";
string fontName = configuration.GetValue<string>("PhotoText:Font") ?? "sans-serif";

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

// Brand colors for text overlay — configurable per profile, with a neutral
// fallback set (Kadampa palette) if unspecified.
List<SKColor> brandColors = (configuration.GetSection("PhotoText:BrandColors").Get<string[]>()
    ?? new[] { "#224486", "#A99886", "#66B9C4", "#358DCB", "#BE303C", "#48ADF4" })
    .Select(hex => SKColor.Parse(hex))
    .ToList();

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

string webpageUrl = configuration.GetValue<string>("StartPage") ?? "";
if (string.IsNullOrWhiteSpace(webpageUrl))
{
    Console.Error.WriteLine("StartPage is not configured. Set \"StartPage\" in the config file.");
    Environment.Exit(1);
}
Directory.CreateDirectory(baseDirectory);

// Create UrlLogger instance (store log file in baseDirectory for convenience)
string urlLogFile = Path.Combine(baseDirectory, "VisitedUrls.log");
UrlLogger urlLogger = new UrlLogger(urlLogFile);

// Cleanup old URL logs
urlLogger.Cleanup(30);

// Download the webpage
logger.LogInformation("Starting download of webpage");

// Extract page URLs from HTML
string htmlContent = await DownloadHtmlContentAsync(webpageUrl);
var pageUrls = Regex.Matches(htmlContent, "<a.*?href=[\"'](.*?)[\"']")
    .Cast<Match>()
    .Select(m => m.Groups[1].Value)
    // resolve relative -> absolute
    .Select(href =>
    {
        var u = new Uri(href, UriKind.RelativeOrAbsolute);
        return u.IsAbsoluteUri
            ? u
            : new Uri(new Uri(webpageUrl), href);
    })
    // skip anything we already logged
    .Where(u => !urlLogger.AlreadyVisited(u.AbsoluteUri))
    .Select(u => u.AbsoluteUri)
    .ToList();

// Check if any page URLs were found
if (pageUrls.Count == 0)
{
    logger.LogError("No page URLs found in the HTML");
    return;
}

logger.LogInformation("URL pattern: {Pattern}", urlPatternRaw);

int pageCount = 0;
// Download images from each page
foreach (string pageUrl in pageUrls)
{
    // Skip pages that don't match the configured URL pattern.
    if (!urlRegex.IsMatch(pageUrl))
    {
        continue;
    }
    if (pageCount == linkDepth)
    {
        break;
    }

    // Record that we've visited this URL
    urlLogger.LogUrl(pageUrl);

    var (innerHtml, imageUrls) = await LoadContentAndImagesAsync(pageUrl);

    if (imageUrls == null || imageUrls.Count == 0)
    {
        logger.LogWarning($"No images found on page: {pageUrl}");
        continue;
    }

    // Filter out images whose URL matches any configured exclude pattern
    // (case-insensitive substring match).
    var filteredImageUrls = new List<string>();
    foreach (var imageUrl in imageUrls)
    {
        if (string.IsNullOrEmpty(imageUrl)) continue;
        string imageUrlLower = imageUrl.ToLowerInvariant();
        bool excluded = false;
        foreach (var ex in imageUrlExcludes)
        {
            if (!string.IsNullOrEmpty(ex) && imageUrlLower.Contains(ex.ToLowerInvariant()))
            {
                excluded = true;
                break;
            }
        }
        if (!excluded) filteredImageUrls.Add(imageUrl);
    }
    var html = await LoadContentAndImagesAsync(pageUrl);
    var doc = new HtmlDocument();
    doc.LoadHtml(html.htmlContent);

    var ogDescription = doc.DocumentNode.SelectSingleNode("//meta[@property='og:description']")?.GetAttributeValue("content", string.Empty);
    var title = CleanText(doc.DocumentNode.SelectSingleNode("//meta[@property='og:title']")?.GetAttributeValue("content", string.Empty));
    var publishedTime = doc.DocumentNode.SelectSingleNode("//meta[@property='article:published_time']")?.GetAttributeValue("content", string.Empty);

    Parallel.ForEach(filteredImageUrls, imageUrl =>
    {
        try
        {
            string fileName = Path.GetFileName(imageUrl);

            DateTime futureDate = new DateTime(9999, 12, 31);
            DateTime publishedDate = DateTime.UtcNow;
            TimeSpan dateDifference = futureDate - publishedDate;
            long reverseOrder = dateDifference.Days;

            string identifier = reverseOrder.ToString("0000000");
            fileName = identifier + "_" + fileName;

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

                        DrawTextOnImage(canvas, bitmap, textToAdd, fontName, brandColors, true);
                        DrawTextOnImage(canvas, bitmap, ogDescription, fontName, brandColors, false);

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

    pageCount++;
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
/// Returns a platform-appropriate user agent string.
/// </summary>
string GetUserAgent()
{
    if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        return "Mozilla/5.0 (Macintosh; Intel Mac OS X 13_5_1) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/110.0.5481.77 Safari/537.36";
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        return "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/110.0.5481.77 Safari/537.36";
    return "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/110.0.5481.77 Safari/537.36";
}

async Task<(string htmlContent, List<string> imageUrls)> LoadContentAndImagesAsync(string url)
{
    using var playwright = await Playwright.CreateAsync();
    var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
    {
        Channel = GetBrowserChannel(),
        Headless = true,
        IgnoreDefaultArgs = new[] { "--enable-automation" },
        Args = new[] { "--disable-blink-features=AutomationControlled" }
    });

    var context = await browser.NewContextAsync(new BrowserNewContextOptions
    {
        UserAgent = GetUserAgent(),
        ViewportSize = new ViewportSize { Width = 1920, Height = 1080 }
    });

    await context.AddInitScriptAsync(@"
        () => {
            Object.defineProperty(navigator, 'webdriver', {
                get: () => undefined
            });
        }
    ");

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

    string content = await page.ContentAsync();

    // Wait for the actual post content (avoids grabbing images from the hamburger/mega-menu)
    await page.WaitForSelectorAsync("article", new PageWaitForSelectorOptions
    {
        Timeout = 10000,
        State = WaitForSelectorState.Attached
    });

    // Ship the configured selector list into the page context so each profile
    // can aim scraping at a different theme's content container.
    string selectorsJson = JsonSerializer.Serialize(contentSelectors);
    var images = await page.EvaluateAsync<string[]>($@"
        () => {{
            const selectors = {selectorsJson};
            let root = null;
            for (const sel of selectors) {{
                root = document.querySelector(sel);
                if (root) break;
            }}
            if (!root) root = document.body;

            const pickSrc = (img) =>
                img.currentSrc ||
                img.src ||
                img.getAttribute('data-src') ||
                img.getAttribute('data-lazy-src') ||
                img.getAttribute('data-original') ||
                '';

            const isInNavOrMenu = (img) =>
                !!img.closest(
                    'header, nav, footer, [role=navigation], ' +
                    '.menu, .mega-menu, .mobile-menu, .offcanvas, ' +
                    '#menu, #site-navigation, #mobile-menu'
                );

            const urls = Array.from(root.querySelectorAll('img'))
                .filter(img => !isInNavOrMenu(img))
                .map(pickSrc)
                .filter(Boolean);

            return Array.from(new Set(urls));
        }}
    ");

    await browser.CloseAsync();
    return (content, images.Where(src => !string.IsNullOrWhiteSpace(src)).Distinct().ToList());
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

async Task<string> DownloadHtmlContentAsync(string url)
{
    using var playwright = await Playwright.CreateAsync();

    var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
    {
        Channel = GetBrowserChannel(),
        Headless = true,
        IgnoreDefaultArgs = new[] { "--enable-automation" },
        Args = new[] { "--disable-blink-features=AutomationControlled" }
    });

    var context = await browser.NewContextAsync(new BrowserNewContextOptions
    {
        UserAgent = GetUserAgent(),
        ViewportSize = new ViewportSize { Width = 1920, Height = 1080 }
    });

    await context.AddInitScriptAsync(@"() => {
        Object.defineProperty(navigator, 'webdriver', {
            get: () => undefined
        });
    }");

    var page = await context.NewPageAsync();

    await page.GotoAsync(url, new PageGotoOptions
    {
        WaitUntil = WaitUntilState.DOMContentLoaded,
        Timeout = 60000
    });

    await page.WaitForSelectorAsync("body", new PageWaitForSelectorOptions { Timeout = 10000 });
    await page.WaitForTimeoutAsync(2000);

    string content = await page.ContentAsync();
    await browser.CloseAsync();
    return content;
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
                     string fontName, List<SKColor> brandColors, bool isHeader)
{
    if (string.IsNullOrWhiteSpace(text)) return;

    // 1) Sample the background, pick a good foreground color
    int startPercent = isHeader ? 0 : 90;
    int endPercent = isHeader ? 10 : 100;
    SKColor backgroundAvg = CalculateAverageColor(bitmap, startPercent, endPercent);
    SKColor textColor = FindBestTextColor(backgroundAvg, brandColors);

    // 2) Figure out our layout box
    float boxTop = bitmap.Height * startPercent / 100f;
    float boxHeight = bitmap.Height * (endPercent - startPercent) / 100f;
    float boxWidth = bitmap.Width * 0.80f;
    float boxLeft = (bitmap.Width - boxWidth) / 2;

    // 3) Find the largest font size that fits both width and height
    int initialSize = isHeader ? 16 : 12;
    int bestSize = initialSize;
    const int maxSize = 72;

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

        if (maxSingleLineWidth > boxWidth || measuredHeight > boxHeight)
        {
            break;
        }

        bestSize = size;
    }

    // 4) Draw it for real — stroke (outline) first, then fill, so letters
    //    remain readable when the background under them has mixed light/dark
    //    patches that would otherwise swallow a single-color fill.
    using var font = new SKFont(typeface, bestSize);

    // Outline is the opposite luminance of the fill. Dark fill gets a white
    // halo; light fill gets a black halo.
    SKColor outlineColor = ToRelativeLuminance(textColor) < 0.5
        ? SKColors.White
        : SKColors.Black;

    // Stroke width scales with font size but is clamped so small descriptions
    // don't get muddy and giant titles don't get cartoonish.
    float strokeWidth = Math.Clamp(bestSize * 0.09f, 2.0f, 6.0f);

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

    var wrappedLines = WrapText(text, font, boxWidth);
    float totalTextHeight = wrappedLines.Count * font.Spacing;

    float startY;
    if (isHeader)
    {
        startY = boxTop + 5 + font.Spacing;
    }
    else
    {
        startY = boxTop + (boxHeight - totalTextHeight) - 5 + font.Spacing;
    }

    foreach (var line in wrappedLines)
    {
        canvas.DrawText(line, boxLeft, startY, SKTextAlign.Left, font, strokePaint);
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
