// File: SitePix/SetupWizard.cs
// First-run interactive setup. Fires when no config file is found at any of
// the standard search paths, or when the user passes `--setup`. Discovers
// the samples shipped next to the binary, presents a curated list, asks a
// few short questions, and writes a usable appsettings.json to the user's
// local app-data folder.
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SitePix;

internal static class SetupWizard
{
    /// <summary>
    /// Curated metadata for the bundled samples. Anything in <c>samples/</c>
    /// that isn't listed here is still offered, just with a generic label.
    /// </summary>
    private record SampleEntry(
        string FileName,
        string Title,
        string Description,
        bool RequiresKey,
        string? KeyName,        // human-readable name of the credential
        string? KeySignupUrl);  // where to register

    private static readonly SampleEntry[] Curated =
    {
        new("metmuseum.org.json", "Met Museum Open Access",
            "CC0, ~half a million artworks (no key)",
            false, null, null),
        new("si.edu.json", "Smithsonian Open Access",
            "CC0, art + natural history",
            true, "api.data.gov key", "https://api.data.gov/signup/"),
        new("nasa.gov.json", "NASA Image Library",
            "generally public-domain space + science imagery (no key)",
            false, null, null),
        new("loc.gov.json", "Library of Congress photos",
            "historical photographs and prints (no key; mixed rights)",
            false, null, null),
        new("flickr.com.json", "Flickr Commons",
            "institutional 'no known restrictions' pool",
            true, "Flickr API key", "https://www.flickr.com/services/apps/create/apply/"),
        new("nypl.org.json", "NYPL Digital Collections",
            "NYPL's public-domain holdings",
            true, "NYPL API token", "https://api.repo.nypl.org/"),
        new("kadampa.org.json", "Kadampa News",
            "Buddhist news site (HTML scrape)",
            false, null, null),
        new("petapixel.com.json", "PetaPixel",
            "photography news (HTML scrape)",
            false, null, null),
        new("atlasobscura.com.json", "Atlas Obscura",
            "travel + curiosity articles (HTML scrape)",
            false, null, null),
        new("thephoblographer.com.json", "The Phoblographer",
            "photography reviews (HTML scrape)",
            false, null, null),
    };

    /// <summary>
    /// Runs the interactive prompts and writes the chosen config to
    /// <paramref name="outPath"/>. Returns the same path so the caller can
    /// hand it straight to ConfigurationBuilder.
    /// </summary>
    public static string Run(string outPath)
    {
        Console.WriteLine();
        Console.WriteLine("─── SitePix setup ───");
        Console.WriteLine();
        Console.WriteLine("SitePix downloads images for your own private use — local slideshow,");
        Console.WriteLine("screensaver, desktop background. Files land in your Pictures folder; nothing");
        Console.WriteLine("is re-published anywhere by SitePix. Some sources have rights that limit");
        Console.WriteLine("*redistribution* even though private viewing is fine — see the chosen");
        Console.WriteLine("sample's header comment if you ever plan to re-share what you've downloaded.");
        Console.WriteLine();
        Console.WriteLine("Press Enter to accept the default in [brackets].");
        Console.WriteLine();

        // ─── 1. Source ───────────────────────────────────────────────────────
        var available = DiscoverAvailableSamples();
        if (available.Count == 0)
        {
            Console.Error.WriteLine("Could not find any sample configs next to the binary. " +
                                    "Reinstall the package or pass a config path on the command line.");
            Environment.Exit(1);
            return outPath;
        }

        Console.WriteLine("Pick an image source:");
        for (int i = 0; i < available.Count; i++)
        {
            var (entry, _) = available[i];
            string suffix = i == 0 ? " (default)" : "";
            Console.WriteLine($"  [{i + 1}] {entry.Title,-28} — {entry.Description}{suffix}");
        }
        Console.Write($"Choice [1]: ");
        string choiceRaw = (Console.ReadLine() ?? "").Trim();
        int choice = 1;
        if (!string.IsNullOrEmpty(choiceRaw) && int.TryParse(choiceRaw, out var parsed))
            choice = parsed;
        if (choice < 1 || choice > available.Count)
        {
            Console.WriteLine($"  Out of range — using [1] {available[0].Item1.Title}.");
            choice = 1;
        }

        var (chosen, samplePath) = available[choice - 1];

        // ─── 2. API key (only for sources that need one) ─────────────────────
        string? apiKey = null;
        if (chosen.RequiresKey)
        {
            Console.WriteLine();
            Console.WriteLine($"{chosen.Title} needs a free {chosen.KeyName}.");
            if (!string.IsNullOrEmpty(chosen.KeySignupUrl))
                Console.WriteLine($"  Sign up: {chosen.KeySignupUrl}");
            Console.Write("Paste your key (or press Enter to fall back to Met Museum): ");
            string typed = (Console.ReadLine() ?? "").Trim();
            if (string.IsNullOrEmpty(typed))
            {
                Console.WriteLine("No key — falling back to Met Museum.");
                var fallback = available.FirstOrDefault(x =>
                    x.Item1.FileName == "metmuseum.org.json");
                if (fallback.Item1 == null)
                {
                    Console.Error.WriteLine("Met Museum sample not found either; aborting.");
                    Environment.Exit(1);
                    return outPath;
                }
                chosen = fallback.Item1;
                samplePath = fallback.Item2;
            }
            else
            {
                apiKey = typed;
            }
        }

        // ─── Pre-load the sample so subsequent questions can show its
        //     defaults (LinkDepth, SubDirectory, etc.) in the prompt. ──────
        var node = LoadSampleAsNode(samplePath);
        int defaultDepth = node["Policies"]?["LinkDepth"]?.GetValue<int>() ?? 20;
        int defaultRetention = node["Policies"]?["RetentionDays"]?.GetValue<int>() ?? 7;
        string sampleSubdir = node["Directories"]?["SubDirectory"]?.GetValue<string>() ?? "SitePix";

        // ─── 3. Save location ────────────────────────────────────────────────
        Console.WriteLine();
        string picsDir = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        Console.Write($"Save downloads to your Pictures folder ({Path.Combine(picsDir, sampleSubdir)})? [Y/n]: ");
        string locAns = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();
        bool useMyPictures = locAns != "n" && locAns != "no";
        string customBase = "";
        if (!useMyPictures)
        {
            Console.Write("Custom save folder (full path): ");
            customBase = (Console.ReadLine() ?? "").Trim();
            if (string.IsNullOrEmpty(customBase))
            {
                Console.WriteLine($"  Empty path — using Pictures folder.");
                useMyPictures = true;
            }
        }

        // ─── 4. Images per run ───────────────────────────────────────────────
        Console.WriteLine();
        Console.Write($"How many images per run? [{defaultDepth}]: ");
        string depthRaw = (Console.ReadLine() ?? "").Trim();
        int linkDepth = defaultDepth;
        if (!string.IsNullOrEmpty(depthRaw)
            && int.TryParse(depthRaw, out var parsedDepth) && parsedDepth > 0)
        {
            linkDepth = parsedDepth;
        }

        // ─── 5. Retention ────────────────────────────────────────────────────
        Console.WriteLine();
        Console.Write($"Keep downloaded images for how many days? [{defaultRetention}]: ");
        string retRaw = (Console.ReadLine() ?? "").Trim();
        int retention = defaultRetention;
        if (!string.IsNullOrEmpty(retRaw)
            && int.TryParse(retRaw, out var parsedRet) && parsedRet > 0)
        {
            retention = parsedRet;
        }

        // ─── 6. Overlay ──────────────────────────────────────────────────────
        Console.WriteLine();
        Console.Write("Add a text overlay (image title + today's date) to each download? [Y/n]: ");
        string overlayAns = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();
        bool wantOverlay = overlayAns != "n" && overlayAns != "no";

        // ─── 7. Daily schedule ───────────────────────────────────────────────
        Console.WriteLine();
        Console.Write("Schedule SitePix to run automatically every day? [y/N]: ");
        string schedAns = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();
        bool wantSchedule = schedAns == "y" || schedAns == "yes";
        string scheduleTime = "";
        if (wantSchedule)
        {
            Console.Write("What time? (24-hour HH:MM) [06:00]: ");
            string t = (Console.ReadLine() ?? "").Trim();
            if (string.IsNullOrEmpty(t)) t = "06:00";
            if (!TimeSpan.TryParse(t, out _))
            {
                Console.WriteLine($"  Could not parse '{t}' — using 06:00.");
                t = "06:00";
            }
            scheduleTime = t;
        }

        // ─── Apply overrides on the loaded sample ────────────────────────────
        ApplyOverrides(node, apiKey, wantOverlay, scheduleTime,
            useMyPictures, customBase, linkDepth, retention);

        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        File.WriteAllText(outPath,
            node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        // ─── Tell the user where things went ─────────────────────────────────
        string subDir = node["Directories"]?["SubDirectory"]?.GetValue<string>() ?? "SitePix";
        string baseDir = useMyPictures ? picsDir : customBase;
        string imgDir = Path.Combine(baseDir, subDir);

        Console.WriteLine();
        Console.WriteLine("─── Setup complete ───");
        Console.WriteLine($"  Source:                {chosen.Title}");
        Console.WriteLine($"  Save folder:           {imgDir}");
        Console.WriteLine($"  Images per run:        {linkDepth}");
        Console.WriteLine($"  Retention:             {retention} day(s)");
        Console.WriteLine($"  Text overlay:          {(wantOverlay ? "on (title + today's date)" : "off")}");
        Console.WriteLine($"  Daily schedule:        {(wantSchedule ? scheduleTime : "off")}");
        Console.WriteLine($"  Config saved to:       {outPath}");
        Console.WriteLine();
        Console.WriteLine("Edit the config file directly to tweak any setting later, or re-run with --setup.");
        Console.WriteLine("Every field is documented in the README's Configuration section.");
        Console.WriteLine();
        Console.WriteLine("Starting first download...");
        Console.WriteLine();

        return outPath;
    }

    /// <summary>
    /// Returns curated samples that actually exist next to the binary, in
    /// the curated display order. Any non-curated sample files are appended
    /// after, with a generic label.
    /// </summary>
    private static List<(SampleEntry, string Path)> DiscoverAvailableSamples()
    {
        string samplesDir = Path.Combine(AppContext.BaseDirectory, "samples");
        var result = new List<(SampleEntry, string)>();
        if (!Directory.Exists(samplesDir)) return result;

        var existing = Directory.GetFiles(samplesDir, "*.json")
            .ToDictionary(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase);

        // Curated entries first, in the registry's order.
        foreach (var entry in Curated)
        {
            if (existing.TryGetValue(entry.FileName, out var path))
            {
                result.Add((entry, path));
                existing.Remove(entry.FileName);
            }
        }

        // Anything else, alphabetically, with auto-derived labels.
        foreach (var kv in existing.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            string title = Path.GetFileNameWithoutExtension(kv.Key);
            result.Add((new SampleEntry(kv.Key, title, "(custom sample)", false, null, null), kv.Value));
        }
        return result;
    }

    /// <summary>
    /// Reads a sample file (which may include // line comments) and parses
    /// it as a mutable JsonNode tree.
    /// </summary>
    private static JsonObject LoadSampleAsNode(string path)
    {
        string text = File.ReadAllText(path);
        var node = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });
        return (JsonObject)(node ?? throw new InvalidOperationException(
            $"Could not parse sample as JSON object: {path}"));
    }

    /// <summary>
    /// Applies the user's wizard answers on top of the loaded sample.
    /// </summary>
    private static void ApplyOverrides(
        JsonObject node,
        string? apiKey,
        bool wantOverlay,
        string scheduleTime,
        bool useMyPictures,
        string customBase,
        int linkDepth,
        int retentionDays)
    {
        if (apiKey != null && node["Source"] is JsonObject src)
        {
            src["ApiKey"] = apiKey;
        }

        if (node["Policies"] is JsonObject policies)
        {
            policies["LinkDepth"] = linkDepth;
            policies["RetentionDays"] = retentionDays;
        }

        if (node["Directories"] is JsonObject dirs)
        {
            dirs["UseMyPictures"] = useMyPictures;
            dirs["Base"] = useMyPictures ? "" : customBase;
            dirs["PhotoText"] = wantOverlay;
        }

        if (node["Task Scheduler"] is JsonObject sched)
        {
            sched["StartTime"] = scheduleTime;
        }
    }
}
