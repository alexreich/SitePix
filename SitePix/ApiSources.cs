// File: SitePix/ApiSources.cs
// JSON-API image sources. Each provider hits a public catalog endpoint, picks
// the largest available image per item, and surfaces a Title / Subtitle the
// existing PhotoText overlay can render. The HTML-scraping path in Program.cs
// is unaffected — these only run when Source:Provider is set.
//
// Scope: SitePix is a private-use tool. Downloads land in the user's local
// Pictures folder for slideshow / screensaver / desktop-background use; the
// images are never re-published anywhere by SitePix itself. Each source
// below is appropriate for that private-use context. The per-sample
// headers in /samples flag any *redistribution* limitations the user
// should be aware of if they later choose to re-share the downloaded
// material themselves. Sources that explicitly prohibit automated access
// in their Terms of Service are not included.
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace SitePix.Sources;

/// <summary>
/// One downloadable item from an API source. A single API record can map to
/// multiple ImageUrls (e.g., Met "additionalImages", Smithsonian media arrays);
/// the outer pipeline downloads each, sharing Title/Subtitle for the overlay.
/// </summary>
public record ApiItem(
    string Id,
    string SourceUrl,
    IReadOnlyList<string> ImageUrls,
    string Title,
    string? Subtitle);

public interface IApiSource
{
    IAsyncEnumerable<ApiItem> FetchAsync(int desiredCount, CancellationToken ct = default);
}

public static class ApiSourceFactory
{
    public static IApiSource? TryCreate(IConfiguration config, HttpClient client, ILogger logger)
    {
        var provider = config.GetValue<string>("Source:Provider");
        if (string.IsNullOrWhiteSpace(provider)) return null;

        return provider.Trim().ToLowerInvariant() switch
        {
            "metmuseum" or "met" => new MetMuseumSource(config, client, logger),
            "smithsonian" or "si" => new SmithsonianSource(config, client, logger),
            "loc" or "libraryofcongress" => new LibraryOfCongressSource(config, client, logger),
            "nasa" or "nasaimages" => new NasaImagesSource(config, client, logger),
            "flickrcommons" or "flickr" => new FlickrCommonsSource(config, client, logger),
            "nypl" => new NyplSource(config, client, logger),
            _ => throw new InvalidOperationException(
                $"Unknown Source:Provider '{provider}'. Supported: " +
                "metmuseum, smithsonian, loc, nasa, flickrcommons, nypl.")
        };
    }
}

internal static class JsonHelpers
{
    public static string? GetStringOrNull(this JsonElement el, string prop)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        if (!el.TryGetProperty(prop, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null
        };
    }

    public static JsonElement? GetPropertyOrNull(this JsonElement el, string prop)
    {
        if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v))
            return v;
        return null;
    }

    public static IEnumerable<JsonElement> EnumerateArrayOrEmpty(this JsonElement el)
        => el.ValueKind == JsonValueKind.Array ? el.EnumerateArray() : Array.Empty<JsonElement>();

    public static string? ResolveSecret(IConfiguration config, string keyName, string envName)
    {
        var v = config.GetValue<string?>("Source:" + keyName);
        if (!string.IsNullOrWhiteSpace(v)) return v;
        var env = config.GetValue<string?>("Source:" + envName);
        if (!string.IsNullOrWhiteSpace(env))
        {
            var read = Environment.GetEnvironmentVariable(env);
            if (!string.IsNullOrWhiteSpace(read)) return read;
        }
        return null;
    }
}

// ─── Met Museum ──────────────────────────────────────────────────────────────
// Zero-config: no API key required. ~half a million CC0 works. Object detail
// endpoint returns primaryImage + additionalImages plus rich subtitle inputs
// (artist, date, medium).
public class MetMuseumSource : IApiSource
{
    private readonly HttpClient _client;
    private readonly ILogger _logger;
    private readonly string _query;
    private readonly bool _onlyHighlights;
    private readonly int _maxImagesPerItem;

    public MetMuseumSource(IConfiguration config, HttpClient client, ILogger logger)
    {
        _client = client;
        _logger = logger;
        _query = config.GetValue<string?>("Source:Query") ?? "";
        _onlyHighlights = config.GetValue<bool?>("Source:OnlyHighlights") ?? false;
        _maxImagesPerItem = Math.Max(1, config.GetValue<int?>("Source:MaxImagesPerItem") ?? 1);
    }

    public async IAsyncEnumerable<ApiItem> FetchAsync(int desiredCount, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var search = "https://collectionapi.metmuseum.org/public/collection/v1/search?isPublicDomain=true&hasImages=true";
        if (_onlyHighlights) search += "&isHighlight=true";
        // Met requires `q`; "*" matches everything.
        search += "&q=" + Uri.EscapeDataString(string.IsNullOrWhiteSpace(_query) ? "*" : _query);

        int[] objectIds;
        try
        {
            using var stream = await _client.GetStreamAsync(search, ct);
            using var doc = await JsonDocument.ParseAsync(stream, default, ct);
            var ids = doc.RootElement.GetPropertyOrNull("objectIDs");
            if (ids is not { ValueKind: JsonValueKind.Array } idsEl)
            {
                _logger.LogWarning("Met search returned no objectIDs");
                yield break;
            }
            objectIds = idsEl.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.Number)
                .Select(e => e.GetInt32())
                .ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Met search failed");
            yield break;
        }

        if (objectIds.Length == 0) yield break;

        // Fisher-Yates shuffle so consecutive runs don't keep hammering the
        // same first N IDs (and the URL log) for the rest of the catalog.
        var rng = Random.Shared;
        for (int i = objectIds.Length - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (objectIds[i], objectIds[j]) = (objectIds[j], objectIds[i]);
        }

        // Oversample so the outer loop has dedup headroom.
        int candidates = Math.Min(objectIds.Length, Math.Max(desiredCount * 4, desiredCount));
        for (int i = 0; i < candidates; i++)
        {
            int id = objectIds[i];
            ApiItem? item = null;
            try
            {
                using var stream = await _client.GetStreamAsync(
                    $"https://collectionapi.metmuseum.org/public/collection/v1/objects/{id}", ct);
                using var doc = await JsonDocument.ParseAsync(stream, default, ct);
                var root = doc.RootElement;

                var images = new List<string>();
                var primary = root.GetStringOrNull("primaryImage");
                if (!string.IsNullOrWhiteSpace(primary)) images.Add(primary);

                // primaryImage is sometimes empty even with hasImages=true.
                // Fall back to the first additionalImages entry, then walk
                // the rest if MaxImagesPerItem > 1.
                if (root.GetPropertyOrNull("additionalImages") is { } addl
                    && addl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var e in addl.EnumerateArray())
                    {
                        if (images.Count >= _maxImagesPerItem) break;
                        if (e.ValueKind == JsonValueKind.String)
                        {
                            var u = e.GetString();
                            if (!string.IsNullOrWhiteSpace(u)) images.Add(u);
                        }
                    }
                }
                if (images.Count == 0) continue;

                var subParts = new List<string>();
                foreach (var key in new[] { "artistDisplayName", "objectDate", "medium" })
                {
                    var s = root.GetStringOrNull(key);
                    if (!string.IsNullOrWhiteSpace(s)) subParts.Add(s);
                }

                item = new ApiItem(
                    Id: id.ToString(),
                    SourceUrl: $"https://www.metmuseum.org/art/collection/search/{id}",
                    ImageUrls: images,
                    Title: root.GetStringOrNull("title") ?? "Untitled",
                    Subtitle: subParts.Count > 0 ? string.Join(" • ", subParts) : null);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Met object {Id} fetch failed: {Msg}", id, ex.Message);
            }

            if (item != null) yield return item;
        }
    }
}

// ─── Smithsonian Open Access ─────────────────────────────────────────────────
// 4M+ CC0 items spanning art, natural history, and science. Requires a free
// api.data.gov key (set Source:ApiKey or env Source:ApiKeyEnv). DEMO_KEY works
// for exploration but is heavily throttled (~30/hour).
public class SmithsonianSource : IApiSource
{
    private readonly HttpClient _client;
    private readonly ILogger _logger;
    private readonly string _query;
    private readonly string _apiKey;
    private readonly int _maxImagesPerItem;

    public SmithsonianSource(IConfiguration config, HttpClient client, ILogger logger)
    {
        _client = client;
        _logger = logger;
        // Default biases toward art-collection units that reliably have
        // CC0 imagery in the response payload (Cooper Hewitt, Freer/Sackler,
        // National Portrait Gallery, Smithsonian American Art, NMAH, NMAfA).
        // The natural-history datasets match `online_media_type:Images` in
        // facets but very often omit the descriptive media block.
        // Treat empty string the same as null so a sample can ship `"Query": ""`
        // and still get the curated default.
        var configured = config.GetValue<string?>("Source:Query");
        _query = string.IsNullOrWhiteSpace(configured)
            ? "online_media_type:\"Images\" AND metadata_usage:CC0 AND " +
              "(unit_code:CHNDM OR unit_code:FSG OR unit_code:NPG OR unit_code:SAAM OR unit_code:NMAH OR unit_code:NMAfA)"
            : configured;
        _apiKey = JsonHelpers.ResolveSecret(config, "ApiKey", "ApiKeyEnv") ?? "DEMO_KEY";
        _maxImagesPerItem = Math.Max(1, config.GetValue<int?>("Source:MaxImagesPerItem") ?? 1);
    }

    public async IAsyncEnumerable<ApiItem> FetchAsync(int desiredCount, [EnumeratorCancellation] CancellationToken ct = default)
    {
        int rows = Math.Clamp(desiredCount * 4, 10, 100);
        // Random offset so we don't keep hitting the same top of the result set
        // (Smithsonian doesn't expose a randomize flag).
        int start = Random.Shared.Next(0, 5000);
        var url = "https://api.si.edu/openaccess/api/v1.0/search?" +
                  $"q={Uri.EscapeDataString(_query)}&rows={rows}&start={start}&api_key={Uri.EscapeDataString(_apiKey)}";

        JsonElement[] rowsArr;
        try
        {
            using var stream = await _client.GetStreamAsync(url, ct);
            using var doc = await JsonDocument.ParseAsync(stream, default, ct);
            var response = doc.RootElement.GetPropertyOrNull("response");
            if (response is null) yield break;
            var rowsEl = response.Value.GetPropertyOrNull("rows");
            if (rowsEl is null) yield break;
            rowsArr = rowsEl.Value.EnumerateArrayOrEmpty().Select(e => e.Clone()).ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Smithsonian search failed (key set? quota left?)");
            yield break;
        }

        foreach (var row in rowsArr)
        {
            ApiItem? item = null;
            try
            {
                var content = row.GetPropertyOrNull("content");
                if (content is null) continue;
                var descNon = content.Value.GetPropertyOrNull("descriptiveNonRepeating");
                if (descNon is null) continue;
                var onlineMedia = descNon.Value.GetPropertyOrNull("online_media");
                if (onlineMedia is null) continue;
                var media = onlineMedia.Value.GetPropertyOrNull("media");
                if (media is null) continue;

                var images = new List<string>();
                foreach (var m in media.Value.EnumerateArrayOrEmpty())
                {
                    if (images.Count >= _maxImagesPerItem) break;
                    if (m.GetStringOrNull("type") != "Images") continue;

                    // Prefer the labeled "High-resolution JPEG" inside
                    // `resources[]` (typical 2k–4k px master). Fall back to
                    // any other resource URL, then to the unsized
                    // deliveryService URL in `content`.
                    string? best = null;
                    if (m.GetPropertyOrNull("resources") is { } resources
                        && resources.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var r in resources.EnumerateArray())
                        {
                            if (r.GetStringOrNull("label") == "High-resolution JPEG")
                            {
                                best = r.GetStringOrNull("url");
                                if (!string.IsNullOrWhiteSpace(best)) break;
                            }
                        }
                        if (string.IsNullOrWhiteSpace(best))
                        {
                            foreach (var r in resources.EnumerateArray())
                            {
                                var label = r.GetStringOrNull("label") ?? "";
                                if (label.Contains("Thumbnail", StringComparison.OrdinalIgnoreCase)) continue;
                                best = r.GetStringOrNull("url");
                                if (!string.IsNullOrWhiteSpace(best)) break;
                            }
                        }
                    }
                    if (string.IsNullOrWhiteSpace(best))
                        best = m.GetStringOrNull("content");
                    if (!string.IsNullOrWhiteSpace(best)) images.Add(best);
                }
                if (images.Count == 0) continue;

                string? title = row.GetStringOrNull("title");
                if (string.IsNullOrWhiteSpace(title) &&
                    descNon.Value.GetPropertyOrNull("title") is { } titleEl)
                {
                    title = titleEl.GetStringOrNull("content");
                }

                string? subtitle = null;
                if (content.Value.GetPropertyOrNull("freetext") is { } freetext)
                {
                    string? FirstFreetext(string field)
                    {
                        var arr = freetext.GetPropertyOrNull(field);
                        if (arr is null) return null;
                        foreach (var e in arr.Value.EnumerateArrayOrEmpty())
                        {
                            var c = e.GetStringOrNull("content");
                            if (!string.IsNullOrWhiteSpace(c)) return c;
                        }
                        return null;
                    }
                    var parts = new List<string>();
                    var name = FirstFreetext("name");
                    var date = FirstFreetext("date");
                    if (!string.IsNullOrWhiteSpace(name)) parts.Add(name);
                    if (!string.IsNullOrWhiteSpace(date)) parts.Add(date);
                    subtitle = parts.Count > 0
                        ? string.Join(" • ", parts)
                        : FirstFreetext("notes");
                }

                string id = row.GetStringOrNull("id") ?? Guid.NewGuid().ToString("N");
                string sourceUrl = descNon.Value.GetPropertyOrNull("record_link")?.GetString()
                    ?? $"https://collections.si.edu/object/{id}";

                item = new ApiItem(id, sourceUrl, images, title ?? "Untitled", subtitle);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Smithsonian row parse failed: {Msg}", ex.Message);
            }

            if (item != null) yield return item;
        }
    }
}

// ─── Library of Congress ─────────────────────────────────────────────────────
// No key required. /photos/ search returns items where the largest usable
// JPEG lives at item.service_high / service_medium (image_url[] is just a
// 150-px thumbnail). Rights vary per item — most pre-1928 photographs are
// public-domain in the US, but the LoC catalog also includes works whose
// status is "not evaluated" or actively in copyright. For private viewing
// any of these are fine; before re-sharing, check each item's rights page.
public class LibraryOfCongressSource : IApiSource
{
    private readonly HttpClient _client;
    private readonly ILogger _logger;
    private readonly string _path;
    private readonly string _query;
    private readonly int _maxImagesPerItem;

    public LibraryOfCongressSource(IConfiguration config, HttpClient client, ILogger logger)
    {
        _client = client;
        _logger = logger;
        _path = (config.GetValue<string?>("Source:Path") ?? "photos").Trim('/');
        _query = config.GetValue<string?>("Source:Query") ?? "";
        _maxImagesPerItem = Math.Max(1, config.GetValue<int?>("Source:MaxImagesPerItem") ?? 1);
    }

    public async IAsyncEnumerable<ApiItem> FetchAsync(int desiredCount, [EnumeratorCancellation] CancellationToken ct = default)
    {
        int count = Math.Clamp(desiredCount * 4, 10, 100);
        // Random page so consecutive runs sample across the result set.
        int page = Random.Shared.Next(1, 50);
        var url = $"https://www.loc.gov/{_path}/?fo=json&c={count}&sp={page}&fa=online-format:image";
        if (!string.IsNullOrWhiteSpace(_query)) url += "&q=" + Uri.EscapeDataString(_query);

        JsonElement[] results;
        try
        {
            using var stream = await _client.GetStreamAsync(url, ct);
            using var doc = await JsonDocument.ParseAsync(stream, default, ct);
            // LoC wraps results: { content: { results: [...] } }. Older
            // endpoints returned them at the root; accept both shapes.
            var content = doc.RootElement.GetPropertyOrNull("content");
            var resultsEl = (content?.GetPropertyOrNull("results"))
                ?? doc.RootElement.GetPropertyOrNull("results");
            if (resultsEl is null) yield break;
            results = resultsEl.Value.EnumerateArrayOrEmpty().Select(e => e.Clone()).ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LoC search failed");
            yield break;
        }

        foreach (var r in results)
        {
            ApiItem? item = null;
            try
            {
                // image_url[] in /photos/ search results is the gallery
                // thumbnail (~150 px). Presentation-quality JPEGs live under
                // item.service_medium / service_high — try those first.
                var picked = new List<string>();
                if (r.GetPropertyOrNull("item") is { } itemEl)
                {
                    foreach (var key in new[] { "service_high", "service_medium", "service_low" })
                    {
                        if (picked.Count >= _maxImagesPerItem) break;
                        var u = itemEl.GetStringOrNull(key);
                        if (!string.IsNullOrWhiteSpace(u)) picked.Add(u);
                    }
                }
                if (picked.Count == 0 && r.GetPropertyOrNull("image_url") is { } imageUrls)
                {
                    var urls = imageUrls.EnumerateArrayOrEmpty()
                        .Where(e => e.ValueKind == JsonValueKind.String)
                        .Select(e => e.GetString()!)
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .ToList();
                    for (int i = urls.Count - 1; i >= 0 && picked.Count < _maxImagesPerItem; i--)
                        picked.Add(urls[i]);
                }
                if (picked.Count == 0) continue;

                string title = r.GetStringOrNull("title") ?? "Untitled";

                string? date = r.GetStringOrNull("date");
                string? firstSubject = null;
                if (r.GetPropertyOrNull("subject") is { } s && s.ValueKind == JsonValueKind.Array)
                {
                    var sf = s.EnumerateArray().FirstOrDefault();
                    if (sf.ValueKind == JsonValueKind.String) firstSubject = sf.GetString();
                }
                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(date)) parts.Add(date);
                if (!string.IsNullOrWhiteSpace(firstSubject)) parts.Add(firstSubject);
                string? subtitle = parts.Count > 0 ? string.Join(" • ", parts) : null;

                string id = r.GetStringOrNull("id") ?? Guid.NewGuid().ToString("N");
                string sourceUrl = id.StartsWith("http") ? id : $"https://www.loc.gov/item/{id}";

                item = new ApiItem(id, sourceUrl, picked, title, subtitle);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("LoC row parse failed: {Msg}", ex.Message);
            }

            if (item != null) yield return item;
        }
    }
}

// ─── NASA Image Library ──────────────────────────────────────────────────────
// No key required. Each search-response item already lists every rendition
// under links[] (~orig as rel=canonical, ~large/medium/small as alternate,
// ~thumb as preview). NASA still photos are generally free of US copyright,
// with carve-outs for NASA insignia, identifiable people in commercial
// reuse, and ESA-co-credited Hubble/JWST imagery — fine for private
// viewing, worth checking per-image before redistribution.
public class NasaImagesSource : IApiSource
{
    private readonly HttpClient _client;
    private readonly ILogger _logger;
    private readonly string _query;

    public NasaImagesSource(IConfiguration config, HttpClient client, ILogger logger)
    {
        _client = client;
        _logger = logger;
        _query = config.GetValue<string?>("Source:Query") ?? "";
    }

    public async IAsyncEnumerable<ApiItem> FetchAsync(int desiredCount, [EnumeratorCancellation] CancellationToken ct = default)
    {
        int pageSize = Math.Clamp(desiredCount * 4, 10, 100);
        int page = Random.Shared.Next(1, 20);
        var url = $"https://images-api.nasa.gov/search?media_type=image&page={page}&page_size={pageSize}";
        if (!string.IsNullOrWhiteSpace(_query)) url += "&q=" + Uri.EscapeDataString(_query);

        JsonElement[] items;
        try
        {
            using var stream = await _client.GetStreamAsync(url, ct);
            using var doc = await JsonDocument.ParseAsync(stream, default, ct);
            var collection = doc.RootElement.GetPropertyOrNull("collection");
            if (collection is null) yield break;
            var itemsEl = collection.Value.GetPropertyOrNull("items");
            if (itemsEl is null) yield break;
            items = itemsEl.Value.EnumerateArrayOrEmpty().Select(e => e.Clone()).ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NASA search failed");
            yield break;
        }

        foreach (var it in items)
        {
            ApiItem? item = null;
            string? title = null, description = null, nasaId = null;

            if (it.GetPropertyOrNull("data") is { } d && d.ValueKind == JsonValueKind.Array)
            {
                var first = d.EnumerateArray().FirstOrDefault();
                if (first.ValueKind == JsonValueKind.Object)
                {
                    title = first.GetStringOrNull("title");
                    description = first.GetStringOrNull("description");
                    nasaId = first.GetStringOrNull("nasa_id");
                }
            }
            if (string.IsNullOrEmpty(nasaId)) continue;

            // Pick the largest rendition straight from the search response.
            string? best = null;
            if (it.GetPropertyOrNull("links") is { } linksEl
                && linksEl.ValueKind == JsonValueKind.Array)
            {
                var links = linksEl.EnumerateArray()
                    .Where(l => l.ValueKind == JsonValueKind.Object)
                    .Select(l => (
                        href: l.GetStringOrNull("href"),
                        rel: l.GetStringOrNull("rel"),
                        render: l.GetStringOrNull("render")))
                    .Where(l => !string.IsNullOrWhiteSpace(l.href) && l.render == "image")
                    .ToList();

                best = links.FirstOrDefault(l => l.href!.Contains("~orig")).href
                    ?? links.FirstOrDefault(l => l.rel == "canonical").href
                    ?? links.FirstOrDefault(l => l.href!.Contains("~large")).href
                    ?? links.FirstOrDefault(l => l.href!.Contains("~medium")).href
                    ?? links.FirstOrDefault(l => l.rel != "preview" && !l.href!.Contains("~thumb")).href;
            }
            if (string.IsNullOrWhiteSpace(best)) continue;

            string sourceUrl = $"https://images.nasa.gov/details/{nasaId}";
            item = new ApiItem(nasaId!, sourceUrl, new List<string> { best }, title ?? "NASA image", description);

            if (item != null) yield return item;
        }
    }
}

// ─── Flickr Commons ──────────────────────────────────────────────────────────
// Institutional pool of works the contributing libraries and museums have
// flagged as "no known copyright restrictions" — note that this is the
// institution's best-effort statement, weaker than CC0/PD. Requires a free
// Flickr API key (set Source:ApiKey or env Source:ApiKeyEnv). Fine for
// private use; check per-photo before redistributing.
public class FlickrCommonsSource : IApiSource
{
    private readonly HttpClient _client;
    private readonly ILogger _logger;
    private readonly string _apiKey;
    private readonly string _query;

    public FlickrCommonsSource(IConfiguration config, HttpClient client, ILogger logger)
    {
        _client = client;
        _logger = logger;
        _query = config.GetValue<string?>("Source:Query") ?? "";
        _apiKey = JsonHelpers.ResolveSecret(config, "ApiKey", "ApiKeyEnv") ?? "";
    }

    public async IAsyncEnumerable<ApiItem> FetchAsync(int desiredCount, [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            _logger.LogError("Flickr Commons requires Source:ApiKey or Source:ApiKeyEnv");
            yield break;
        }

        int perPage = Math.Clamp(desiredCount * 4, 10, 100);
        int page = Random.Shared.Next(1, 50);
        var url = "https://api.flickr.com/services/rest/?method=flickr.photos.search" +
                  "&is_commons=1" +
                  $"&api_key={Uri.EscapeDataString(_apiKey)}" +
                  "&format=json&nojsoncallback=1" +
                  $"&per_page={perPage}&page={page}" +
                  "&extras=url_o,url_k,url_h,url_l,description,date_taken,owner_name";
        if (!string.IsNullOrWhiteSpace(_query))
            url += "&text=" + Uri.EscapeDataString(_query);

        JsonElement[] photos;
        try
        {
            using var stream = await _client.GetStreamAsync(url, ct);
            using var doc = await JsonDocument.ParseAsync(stream, default, ct);
            var photosEl = doc.RootElement.GetPropertyOrNull("photos");
            if (photosEl is null) yield break;
            var photoArr = photosEl.Value.GetPropertyOrNull("photo");
            if (photoArr is null) yield break;
            photos = photoArr.Value.EnumerateArrayOrEmpty().Select(e => e.Clone()).ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Flickr Commons search failed");
            yield break;
        }

        foreach (var p in photos)
        {
            ApiItem? item = null;
            try
            {
                // Largest available; url_o (original) → url_k (2048) → url_h (1600) → url_l (1024).
                string? best = null;
                foreach (var key in new[] { "url_o", "url_k", "url_h", "url_l" })
                {
                    var v = p.GetStringOrNull(key);
                    if (!string.IsNullOrWhiteSpace(v)) { best = v; break; }
                }
                if (best == null) continue;

                string id = p.GetStringOrNull("id") ?? "";
                string title = p.GetStringOrNull("title") ?? "Untitled";

                string? description = null;
                if (p.GetPropertyOrNull("description") is { } de)
                    description = de.GetStringOrNull("_content");

                string? owner = p.GetStringOrNull("owner_name");
                string? date = p.GetStringOrNull("datetaken");
                var subParts = new List<string>();
                if (!string.IsNullOrWhiteSpace(owner)) subParts.Add(owner);
                if (!string.IsNullOrWhiteSpace(date)) subParts.Add(date);
                string? subtitle = subParts.Count > 0
                    ? string.Join(" • ", subParts)
                    : (string.IsNullOrWhiteSpace(description) ? null : description);

                string ownerId = p.GetStringOrNull("owner") ?? "";
                string sourceUrl = !string.IsNullOrEmpty(ownerId) && !string.IsNullOrEmpty(id)
                    ? $"https://www.flickr.com/photos/{ownerId}/{id}"
                    : $"https://www.flickr.com/photo.gne?id={id}";

                item = new ApiItem(id, sourceUrl, new List<string> { best }, title, subtitle);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Flickr photo parse failed: {Msg}", ex.Message);
            }

            if (item != null) yield return item;
        }
    }
}

// ─── NYPL Digital Collections ────────────────────────────────────────────────
// Requires a free API token from https://api.repo.nypl.org/. Best-effort
// shape: search → captures, fetch the largest IIIF-served JPG per imageID.
// NYPL marks items as public-domain via the publicDomainOnly=true filter;
// that's an institutional determination — solid for private use; double-
// check the rights statement on the item page before redistributing.
public class NyplSource : IApiSource
{
    private readonly HttpClient _client;
    private readonly ILogger _logger;
    private readonly string _query;
    private readonly string _token;

    public NyplSource(IConfiguration config, HttpClient client, ILogger logger)
    {
        _client = client;
        _logger = logger;
        _query = config.GetValue<string?>("Source:Query") ?? "*";
        _token = JsonHelpers.ResolveSecret(config, "ApiKey", "ApiKeyEnv") ?? "";
    }

    public async IAsyncEnumerable<ApiItem> FetchAsync(int desiredCount, [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_token))
        {
            _logger.LogError("NYPL requires Source:ApiKey (token from https://api.repo.nypl.org/)");
            yield break;
        }

        int perPage = Math.Clamp(desiredCount * 4, 10, 100);
        int page = Random.Shared.Next(1, 20);
        var url = "https://api.repo.nypl.org/api/v2/items/search.json" +
                  $"?q={Uri.EscapeDataString(_query)}&per_page={perPage}&page={page}&publicDomainOnly=true";

        JsonElement[] capturesArr;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("Authorization", $"Token token=\"{_token}\"");
            using var resp = await _client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, default, ct);
            JsonElement root = doc.RootElement;
            if (root.GetPropertyOrNull("nyplAPI") is { } api
                && api.GetPropertyOrNull("response") is { } response
                && response.GetPropertyOrNull("capture") is { } capture)
            {
                capturesArr = capture.EnumerateArrayOrEmpty().Select(e => e.Clone()).ToArray();
            }
            else yield break;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NYPL search failed");
            yield break;
        }

        foreach (var cap in capturesArr)
        {
            ApiItem? item = null;
            try
            {
                string? imageId = cap.GetStringOrNull("imageID");
                if (string.IsNullOrEmpty(imageId)) continue;

                // `t=w` is the largest non-IIIF size; `t=g` is mid-size.
                string imageUrl = $"https://images.nypl.org/index.php?id={imageId}&t=w";

                string id = cap.GetStringOrNull("uuid") ?? imageId;
                string title = cap.GetStringOrNull("title") ?? "Untitled";
                string? subtitle = cap.GetStringOrNull("typeOfResource");

                string sourceUrl = cap.GetStringOrNull("itemLink")
                    ?? $"https://digitalcollections.nypl.org/items/{id}";

                item = new ApiItem(id, sourceUrl, new List<string> { imageUrl }, title, subtitle);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("NYPL capture parse failed: {Msg}", ex.Message);
            }

            if (item != null) yield return item;
        }
    }
}
