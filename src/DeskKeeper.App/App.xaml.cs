using System.Diagnostics;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using DeskKeeper.App.Services;
using DeskKeeper.Infrastructure.Audit;
using DeskKeeper.Infrastructure.Desktop;
using DeskKeeper.Infrastructure.Shell;
using DeskKeeper.Infrastructure.Storage;
using DeskKeeper.Infrastructure.Windows;

namespace DeskKeeper.App;

public partial class App : System.Windows.Application
{
    internal static string DataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskKeeper");

    internal static AppConfigDto Config { get; private set; } = ConfigStore.CreateDefaultConfig();
    internal static DisplayInfo Display { get; private set; } = Services.DisplayDetector.Detect();
    internal static event Action? DisplayInfoChanged;
    internal static ConfigStore ConfigStore { get; private set; } = new(DataDir);
    internal static SnapshotStore SnapshotStore { get; private set; } = new(DataDir);
    internal static AuditLogger AuditLogger { get; private set; } = new(Path.Combine(DataDir, "logs"));
    internal static UndoStore UndoStore { get; private set; } = new(DataDir);

    internal static void SetConfig(AppConfigDto config) => Config = config;

    private Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnUnhandled;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandled;

        if (TryRunHeadless(e.Args, out var exitCode))
        {
            Shutdown(exitCode);
            return;
        }

        if (!AcquireSingleInstance())
        {
            MessageBox.Show("讲台助手已在运行，请查看任务栏托盘图标。", "讲台助手",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(1);
            return;
        }

        Config = ConfigStore.Load();
        RefreshDisplayInfo();
        var overrideColor = ParseHex(Config.AccentColor);
        var accent = overrideColor ?? Services.WallpaperAccent.Extract();
        if (accent is { } color)
        {
            ApplyAccentToResources(color);
        }
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    internal static void SetAccent(Color? color)
    {
        if (color is { } c && Current is App app)
        {
            app.ApplyAccentToResources(c);
        }
    }

    private static void RefreshDisplayInfo()
    {
        Display = Services.DisplayDetector.Detect();
        DisplayInfoChanged?.Invoke();
    }

    private static void OnDisplaySettingsChanged(object? sender, EventArgs e) => RefreshDisplayInfo();

    private static void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.Desktop && string.IsNullOrWhiteSpace(Config.AccentColor))
        {
            SetAccent(Services.WallpaperAccent.Extract());
        }
    }

    private void ApplyAccentToResources(Color color)
    {
        Resources["PrimaryColor"] = color;
        Resources["PrimaryBrush"] = new SolidColorBrush(color);
    }

    internal static Color? ParseHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return null;
        }

        try
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
        catch
        {
            return null;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private bool AcquireSingleInstance()
    {
        _mutex = new Mutex(true, @"Global\DeskKeeperSingleInstance", out var createdNew);
        return createdNew;
    }

    private static bool TryRunHeadless(string[] args, out int exitCode)
    {
        exitCode = 0;
        var profileId = Arg(args, "--profile") ?? "classroom";
        var dryRun = args.Contains("--dry-run", StringComparer.OrdinalIgnoreCase);
        var logPath = Arg(args, "--log");

        try
        {
            string? summary;
            if (args.Contains("--selftest", StringComparer.OrdinalIgnoreCase))
            {
                summary = SelfTestRunner.Run();
            }
            else if (args.Contains("--apply", StringComparer.OrdinalIgnoreCase))
            {
                summary = HeadlessRunner.Apply(profileId, dryRun);
            }
            else if (args.Contains("--cleanup", StringComparer.OrdinalIgnoreCase))
            {
                summary = HeadlessRunner.Clean(profileId, dryRun);
            }
            else if (args.Contains("--restore", StringComparer.OrdinalIgnoreCase))
            {
                summary = HeadlessRunner.Restore(profileId);
            }
            else if (args.Contains("--restore-layout", StringComparer.OrdinalIgnoreCase))
            {
                summary = HeadlessRunner.RestoreLayout(profileId);
            }
            else if (args.Contains("--read-icons", StringComparer.OrdinalIgnoreCase))
            {
                summary = HeadlessRunner.ReadIcons();
            }
            else if (args.Contains("--display-info", StringComparer.OrdinalIgnoreCase))
            {
                summary = HeadlessRunner.DisplayInfo();
            }
            else
            {
                return false;
            }

            Console.Out.WriteLine(summary);
            if (logPath is not null)
            {
                File.WriteAllText(logPath, summary, Encoding.UTF8);
            }

            exitCode = summary.Contains("\"ok\": true", StringComparison.Ordinal)
                || summary.Contains("\"ok\":true", StringComparison.Ordinal) ? 0 : 2;
            return true;
        }
        catch (Exception ex)
        {
            var summary = $"{{\"ok\":false,\"error\":\"{Escape(ex.Message)}\"}}";
            Console.Out.WriteLine(summary);
            if (logPath is not null)
            {
                File.WriteAllText(logPath, summary, Encoding.UTF8);
            }

            exitCode = 2;
            return true;
        }
    }

    private static string? Arg(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AuditLogger.Append(new AuditRecord(
            DateTimeOffset.Now, Environment.MachineName, null, "-", "crash", false,
            0, 0, new[] { new AuditFailure("ui", e.Exception.Message) }, null, null));
        MessageBox.Show($"发生未处理错误：{e.Exception.Message}", "讲台助手", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }

    private static void OnDomainUnhandled(object sender, UnhandledExceptionEventArgs e)
    {
        AuditLogger.Append(new AuditRecord(
            DateTimeOffset.Now, Environment.MachineName, null, "-", "crash", false,
            0, 0, new[] { new AuditFailure("domain", e.ExceptionObject?.ToString() ?? "") }, null, null));
    }
}

