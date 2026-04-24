using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace SitePix;

public class UrlLogger
{
    private readonly string _filePath;
    private readonly HashSet<string> _visited;

    public UrlLogger(string filePath)
    {
        _filePath = filePath;
        if (!File.Exists(_filePath))
            File.WriteAllText(_filePath, "");

        _visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in File.ReadAllLines(_filePath))
        {
            var parts = line.Split('\t', 2);
            if (parts.Length != 2) continue;
            var norm = parts[1].Trim();
            _visited.Add(norm);
        }
    }

    /// <summary>
    /// True if we've already visited this URL (in normalized form).
    /// </summary>
    public bool AlreadyVisited(string url)
        => _visited.Contains(NormalizeUrl(url));

    /// <summary>
    /// Adds a timestamped entry of the normalized URL, if not already there.
    /// </summary>
    public void LogUrl(string url)
    {
        var norm = NormalizeUrl(url);
        if (_visited.Add(norm))
        {
            var timestamp = DateTime.UtcNow.ToString("O");
            File.AppendAllText(_filePath, $"{timestamp}\t{norm}{Environment.NewLine}");
        }
    }

    /// <summary>
    /// Drops any logged URLs older than retentionDays, both on disk and in memory.
    /// </summary>
    public void Cleanup(int retentionDays)
    {
        if (!File.Exists(_filePath)) return;
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);

        var keptLines = new List<string>();
        foreach (var line in File.ReadAllLines(_filePath))
        {
            var parts = line.Split('\t', 2);
            if (parts.Length != 2) continue;
            if (DateTime.TryParse(parts[0], null, DateTimeStyles.RoundtripKind, out var time)
                && time >= cutoff)
            {
                keptLines.Add(line);
            }
        }

        File.WriteAllLines(_filePath, keptLines);

        _visited.Clear();
        foreach (var line in keptLines)
        {
            var urlPart = line.Split('\t', 2)[1].Trim();
            _visited.Add(urlPart);
        }
    }

    /// <summary>
    /// Normalizes a URL by lower-casing, trimming trailing slash on the path,
    /// and preserving the query string.
    /// </summary>
    private static string NormalizeUrl(string raw)
    {
        try
        {
            var uri = new Uri(raw);
            var path = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
            var query = uri.Query;
            return (path + query).ToLowerInvariant();
        }
        catch
        {
            return raw.Trim().ToLowerInvariant();
        }
    }
}
