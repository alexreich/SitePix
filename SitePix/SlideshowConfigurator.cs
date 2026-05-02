// File: SitePix/SlideshowConfigurator.cs
// Optional convenience invoked at the end of the setup wizard: point the
// OS's native desktop-wallpaper slideshow at the folder we just configured.
// Windows: IDesktopWallpaper COM | macOS: osascript | Linux: GNOME gsettings
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace SitePix;

internal static class SlideshowConfigurator
{
    // Marker file written into the slideshow folder when the wizard configures
    // GNOME. Its presence is the "GNOME slideshow is wired up here" signal that
    // RefreshLinuxIfConfigured uses to decide whether to regenerate the XML.
    private const string GnomeXmlFileName = ".sitepix-slideshow.xml";

    /// <summary>
    /// Best-effort: configure the OS's native desktop slideshow to use
    /// <paramref name="folderPath"/>. Returns a short user-facing summary line
    /// (success or hand-off instructions); never throws.
    /// </summary>
    public static string Configure(string folderPath)
    {
        try { Directory.CreateDirectory(folderPath); } catch { /* best effort */ }

        try
        {
            if (OperatingSystem.IsWindows())
                return ConfigureWindows(folderPath);
            if (OperatingSystem.IsMacOS())
                return ConfigureMacOS(folderPath);
            if (OperatingSystem.IsLinux())
                return ConfigureLinux(folderPath);
            return $"Unsupported OS — open your wallpaper settings and point them at: {folderPath}";
        }
        catch (Exception ex)
        {
            return $"Could not set slideshow automatically ({ex.Message}). " +
                   $"Open your wallpaper settings and point them at: {folderPath}";
        }
    }

    // ─── Windows ─────────────────────────────────────────────────────────────
    // IDesktopWallpaper COM interface — the API the Personalization UI itself
    // uses. Takes an IShellItemArray containing one IShellItem (the folder)
    // and Windows then walks its contents on its own schedule.

    [SupportedOSPlatform("windows")]
    private static string ConfigureWindows(string folderPath)
    {
        Type? wallpaperType = Type.GetTypeFromCLSID(
            new Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD"));
        if (wallpaperType == null)
            throw new PlatformNotSupportedException("IDesktopWallpaper coclass unavailable.");

        var wallpaper = (IDesktopWallpaper)Activator.CreateInstance(wallpaperType)!;

        Guid shellItemGuid = typeof(IShellItem).GUID;
        Guid shellItemArrayGuid = typeof(IShellItemArray).GUID;

        int hr = SHCreateItemFromParsingName(folderPath, IntPtr.Zero, ref shellItemGuid, out var item);
        if (hr != 0 || item == null)
            throw new Exception($"SHCreateItemFromParsingName failed (0x{hr:X8})");

        hr = SHCreateShellItemArrayFromShellItem(item, ref shellItemArrayGuid, out var arr);
        if (hr != 0 || arr == null)
            throw new Exception($"SHCreateShellItemArrayFromShellItem failed (0x{hr:X8})");

        wallpaper.SetSlideshow(arr);
        // 30-minute rotation, no shuffle. Tweak via Settings → Personalization later.
        wallpaper.SetSlideshowOptions(0, 30 * 60 * 1000);
        wallpaper.Enable(true);

        return $"Windows desktop slideshow source set to: {folderPath}";
    }

    [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID,
                          [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetWallpaper(
                          [MarshalAs(UnmanagedType.LPWStr)] string monitorID);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetMonitorDevicePathAt(uint monitorIndex);
        uint GetMonitorDevicePathCount();
        RECT GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorID);
        void SetBackgroundColor(uint color);
        uint GetBackgroundColor();
        void SetPosition(int position);
        int GetPosition();
        void SetSlideshow(IShellItemArray items);
        IShellItemArray GetSlideshow();
        void SetSlideshowOptions(uint options, uint slideshowTick);
        void GetSlideshowOptions(out uint options, out uint slideshowTick);
        void AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string monitorID, int direction);
        int GetStatus();
        void Enable([MarshalAs(UnmanagedType.Bool)] bool enable);
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem { }

    [ComImport, Guid("B63EA76D-1F85-456F-A19C-48159EFA858B"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemArray { }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath, IntPtr pbc, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);

    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHCreateShellItemArrayFromShellItem(
        IShellItem psi, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemArray ppv);

    // ─── macOS ───────────────────────────────────────────────────────────────
    // Setting "picture" via System Events points the Finder at the folder.
    // On macOS Sonoma+ rotation also requires "Change picture" to be enabled
    // in System Settings → Wallpaper, so we open that pane as a hand-off.

    [SupportedOSPlatform("macos")]
    private static string ConfigureMacOS(string folderPath)
    {
        string esc = folderPath.Replace("\\", "\\\\").Replace("\"", "\\\"");
        string script = $"tell application \"System Events\" to tell every desktop " +
                        $"to set picture to \"{esc}\"";

        var psi = new ProcessStartInfo("osascript")
        {
            ArgumentList = { "-e", script },
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var proc = Process.Start(psi)
            ?? throw new Exception("Could not launch osascript.");
        string err = proc.StandardError.ReadToEnd();
        proc.WaitForExit(5000);
        if (proc.ExitCode != 0)
            return $"osascript failed ({proc.ExitCode}): {err.Trim()}\n" +
                   $"  Open System Settings → Wallpaper and point it at: {folderPath}";

        try
        {
            Process.Start(new ProcessStartInfo("open",
                "x-apple.systempreferences:com.apple.preference.desktopscreeneffect")
            { UseShellExecute = false, CreateNoWindow = true })?.Dispose();
        }
        catch { /* not fatal — user can open Settings manually */ }

        return $"macOS desktop picture source set to: {folderPath}\n" +
               $"  System Settings → Wallpaper opened — turn on 'Change picture' for rotation.";
    }

    /// <summary>
    /// Called at the end of every Program.cs run. Regenerates the GNOME
    /// slideshow XML so newly-downloaded images join the rotation and
    /// retention-swept ones leave it. No-op on non-Linux, and no-op on
    /// Linux unless the wizard has previously been opted in (detected by
    /// the marker XML's presence in the folder).
    /// </summary>
    public static void RefreshLinuxIfConfigured(string folderPath)
    {
        if (!OperatingSystem.IsLinux()) return;
        string xmlPath = Path.Combine(folderPath, GnomeXmlFileName);
        if (!File.Exists(xmlPath)) return;
        try { WriteGnomeSlideshowXml(xmlPath, folderPath); }
        catch { /* refresh is best-effort; don't disrupt the run */ }
    }

    // ─── Linux (GNOME) ───────────────────────────────────────────────────────
    // GNOME doesn't natively rotate through a folder; we generate a
    // slideshow XML that lists every image currently in the folder and
    // point picture-uri at it. Other desktop environments (KDE, XFCE, …) have
    // their own bespoke mechanisms; we hand off with a clear instruction.

    [SupportedOSPlatform("linux")]
    private static string ConfigureLinux(string folderPath)
    {
        string xmlPath = Path.Combine(folderPath, GnomeXmlFileName);
        WriteGnomeSlideshowXml(xmlPath, folderPath);

        string uri = "file://" + xmlPath;
        bool ok = TryGsettings("org.gnome.desktop.background", "picture-uri", $"'{uri}'");
        TryGsettings("org.gnome.desktop.background", "picture-uri-dark", $"'{uri}'");

        if (ok)
            return $"GNOME wallpaper slideshow set to: {folderPath}\n" +
                   $"  (Re-run --setup after the next batch of downloads to refresh the list.)";
        return $"gsettings not available — set your desktop's wallpaper to use folder: {folderPath}";
    }

    private static bool TryGsettings(string schema, string key, string value)
    {
        try
        {
            var psi = new ProcessStartInfo("gsettings")
            {
                ArgumentList = { "set", schema, key, value },
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return false;
            proc.WaitForExit(5000);
            return proc.ExitCode == 0;
        }
        catch { return false; }
    }

    private static void WriteGnomeSlideshowXml(string xmlPath, string folder)
    {
        var images = Directory.Exists(folder)
            ? Directory.GetFiles(folder)
                .Where(f => new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp" }
                    .Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList()
            : new List<string>();

        var sb = new StringBuilder();
        sb.AppendLine("<background>");
        sb.AppendLine("  <starttime>");
        sb.AppendLine("    <year>2025</year><month>01</month><day>01</day>");
        sb.AppendLine("    <hour>00</hour><minute>00</minute><second>00</second>");
        sb.AppendLine("  </starttime>");

        const int holdSeconds = 1800;     // 30 min hold
        const int transitionSeconds = 1;  // 1 s cross-fade

        for (int i = 0; i < images.Count; i++)
        {
            string current = images[i];
            string next = images[(i + 1) % images.Count];
            sb.AppendLine("  <static>");
            sb.AppendLine($"    <duration>{holdSeconds}.0</duration>");
            sb.AppendLine($"    <file>{current}</file>");
            sb.AppendLine("  </static>");
            sb.AppendLine("  <transition type=\"overlay\">");
            sb.AppendLine($"    <duration>{transitionSeconds}.0</duration>");
            sb.AppendLine($"    <from>{current}</from>");
            sb.AppendLine($"    <to>{next}</to>");
            sb.AppendLine("  </transition>");
        }

        sb.AppendLine("</background>");
        File.WriteAllText(xmlPath, sb.ToString());
    }
}
