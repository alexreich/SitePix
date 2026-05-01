// File: SitePix/ApiSources.cs
// JSON-API image sources. Each provider hits a public catalog endpoint, picks
// the largest available image per item, and surfaces a Title / Subtitle the
// existing PhotoText overlay can render. The HTML-scraping path in Program.cs
// is unaffected — these only run when Source:Provider is set.
//
// Only catalogs that publish items under an explicit CC0 (Creative Commons
// Zero) waiver are supported here — currently the Met Museum's Open Access
// program and the Smithsonian's Open Access program (filtered to
// metadata_usage:CC0). Catalogs whose rights story is "public domain in
// many cases" / "no known restrictions" / "rights vary per item" (NASA,
// Library of Congress general search, Flickr Commons, NYPL) were
// intentionally excluded — under CC0 there is no per-item rights review or
// attribution requirement, so the resulting downloads (and any overlays
// applied to them) can be reposted without legal concern.
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
            _ => throw new InvalidOperationException(
                $"Unknown Source:Provider '{provider}'. Supported: metmuseum, smithsonian. " +
                "Other catalogs were excluded because their items don't ship as explicit CC0.")
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
