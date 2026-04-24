using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

#if WINDOWS
using Microsoft.Win32.TaskScheduler;
using TaskServiceTask = Microsoft.Win32.TaskScheduler.Task;
#endif

namespace SitePix;

internal static class TaskRegistration
{
    private const string DefaultTaskName = "SitePix";
    private const string DefaultMacOSLabel = "com.sitepix.agent";
    private const string DefaultLinuxMarker = "# sitepix";

    /// <summary>
    /// Registers a daily scheduled task on the current platform if configured.
    /// Windows: Task Scheduler | macOS: launchd plist | Linux: cron
    /// </summary>
    public static void EnsureDailyTaskIfConfigured(IConfigurationRoot configuration, ILogger? logger)
    {
        string? startTime = configuration.GetValue<string>("Task Scheduler:StartTime");
        if (string.IsNullOrWhiteSpace(startTime))
            return;

        if (!TimeSpan.TryParse(startTime, out var timeOfDay))
        {
            logger?.LogWarning("Invalid Task Scheduler StartTime: {StartTime}", startTime);
            return;
        }

        string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(exePath))
        {
            logger?.LogWarning("Unable to determine executable path for task registration.");
            return;
        }

        // Allow config to override the task identifier so multiple profiles
        // scheduled on one machine don't collide.
        string taskName = configuration.GetValue<string>("Task Scheduler:Id") ?? DefaultTaskName;

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                RegisterWindows(exePath, timeOfDay, taskName, logger);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                RegisterMacOS(exePath, timeOfDay, taskName, logger);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                RegisterLinux(exePath, timeOfDay, taskName, logger);
            else
                logger?.LogWarning("Unsupported OS for task scheduling.");
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to register scheduled task.");
        }
    }

    // ─── Windows ─────────────────────────────────────────────────────────────

    private static void RegisterWindows(string exePath, TimeSpan timeOfDay, string taskName, ILogger? logger)
    {
#if WINDOWS
        using TaskService ts = new TaskService();
        foreach (TaskServiceTask task in ts.RootFolder.Tasks)
        {
            if (task.Name == taskName)
                return; // already registered
        }

        TaskDefinition td = ts.NewTask();
        td.RegistrationInfo.Author = "sitepix";
        td.RegistrationInfo.Description = "Daily image sync for SitePix";
        td.Actions.Add(new ExecAction(exePath));

        DailyTrigger trigger = new DailyTrigger
        {
            StartBoundary = DateTime.Today.Add(timeOfDay),
            DaysInterval = 1
        };
        td.Triggers.Add(trigger);

        ts.RootFolder.RegisterTaskDefinition(taskName, td);
        logger?.LogInformation("Windows Task '{TaskName}' registered.", taskName);
#endif
    }

    // ─── macOS (launchd) ─────────────────────────────────────────────────────

    private static void RegisterMacOS(string exePath, TimeSpan timeOfDay, string taskName, ILogger? logger)
    {
        // Task name may be user-supplied; keep the plist label conservative
        // (only lower-case it and add the agent suffix if we're using the default name).
        string label = taskName == DefaultTaskName
            ? DefaultMacOSLabel
            : $"com.sitepix.{taskName.ToLowerInvariant()}";

        string plistDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "LaunchAgents");
        string plistPath = Path.Combine(plistDir, $"{label}.plist");

        if (File.Exists(plistPath))
        {
            logger?.LogInformation("macOS LaunchAgent already exists: {Path}", plistPath);
            return;
        }

        Directory.CreateDirectory(plistDir);

        string plistContent = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<!DOCTYPE plist PUBLIC ""-//Apple//DTD PLIST 1.0//EN""
  ""http://www.apple.com/DTDs/PropertyList-1.0.dtd"">
<plist version=""1.0"">
<dict>
    <key>Label</key>
    <string>{label}</string>
    <key>ProgramArguments</key>
    <array>
        <string>{exePath}</string>
    </array>
    <key>StartCalendarInterval</key>
    <dict>
        <key>Hour</key>
        <integer>{timeOfDay.Hours}</integer>
        <key>Minute</key>
        <integer>{timeOfDay.Minutes}</integer>
    </dict>
    <key>StandardOutPath</key>
    <string>/tmp/{label}.log</string>
    <key>StandardErrorPath</key>
    <string>/tmp/{label}.err</string>
</dict>
</plist>";

        File.WriteAllText(plistPath, plistContent);
        logger?.LogInformation("macOS LaunchAgent created: {Path}. Run 'launchctl load {Path}' to activate.", plistPath, plistPath);
    }

    // ─── Linux (cron) ────────────────────────────────────────────────────────

    private static void RegisterLinux(string exePath, TimeSpan timeOfDay, string taskName, ILogger? logger)
    {
        string cronMarker = taskName == DefaultTaskName
            ? DefaultLinuxMarker
            : $"# sitepix-{taskName.ToLowerInvariant()}";
        string cronLine = $"{timeOfDay.Minutes} {timeOfDay.Hours} * * * {exePath} {cronMarker}";

        try
        {
            var result = RunProcess("crontab", "-l");
            if (result.Contains(cronMarker, StringComparison.OrdinalIgnoreCase))
            {
                logger?.LogInformation("Linux cron job already exists.");
                return;
            }

            string newCrontab = result.TrimEnd() + Environment.NewLine + cronLine + Environment.NewLine;
            var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "crontab",
                    Arguments = "-",
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            proc.Start();
            proc.StandardInput.Write(newCrontab);
            proc.StandardInput.Close();
            proc.WaitForExit(5000);

            logger?.LogInformation("Linux cron job registered for {Hour}:{Minute}.",
                timeOfDay.Hours, timeOfDay.Minutes);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to register Linux cron job. You may add it manually: {CronLine}", cronLine);
        }
    }

    private static string RunProcess(string fileName, string arguments)
    {
        var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        proc.Start();
        string output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit(5000);
        return output;
    }
}
