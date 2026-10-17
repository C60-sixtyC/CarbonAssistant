using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;
using DeskKeeper.Application.Mapping;
using DeskKeeper.Domain.Contracts;
using DeskKeeper.Domain.Rules;
using DeskKeeper.Infrastructure.Desktop;
using DeskKeeper.Infrastructure.Storage;

namespace DeskKeeper.App.Services;

/// <summary>
/// 命令行静默模式：供 GPO 计划任务在登录/注销时调用，
/// 例如：DeskKeeper.exe --apply --profile classroom --dry-run --log \\server\log\%COMPUTERNAME%.json
/// </summary>
public static class HeadlessRunner
{
    public static string Apply(string profileId, bool dryRun)
    {
        var profile = Load(profileId);
        var (arrange, _) = AppServices.BuildUseCases();
        var preview = arrange.Preview(profile);
        var result = arrange.Apply(preview, profile, dryRun);
        return JsonSerializer.Serialize(new
        {
            ok = !result.Aborted && result.Failures.Count == 0,
            action = "arrange",
            profile = profile.Id,
            dryRun,
            result.Moved,
            result.Kept,
            planned = preview.PlannedSlots.Count,
            warnings = preview.Warnings,
            result.Failures,
            result.AbortReason,
        }, JsonOptions);
    }

    public static string Clean(string profileId, bool dryRun)
    {
        var profile = Load(profileId);
        var (_, clean) = AppServices.BuildUseCases();
        var manifest = clean.BuildManifest(profile);
        manifest = clean.MarkLocked(manifest);
        var preCheck = clean.PreCheck(manifest, GetScopeRoot());
        var result = clean.Execute(manifest, profile, dryRun, GetScopeRoot());
        return JsonSerializer.Serialize(new
        {
            ok = !result.Aborted && result.Failures.Count == 0,
            action = "cleanup",
            profile = profile.Id,
            dryRun,
            candidates = manifest.CheckedCount,
            manifest.CheckedBytes,
            result.Cleaned,
            result.BytesCleaned,
            result.BatchDirectory,
            preCheck.Warnings,
            result.Failures,
            result.AbortReason,
        }, JsonOptions);
    }

    public static string Restore(string profileId)
    {
        _ = Load(profileId);
        var (_, clean) = AppServices.BuildUseCases();
        var result = clean.RestoreLastClean();
        return JsonSerializer.Serialize(new
        {
            ok = result.Failures.Count == 0,
            action = "restore",
            result.Cleaned,
            result.Failures,
        }, JsonOptions);
    }

    public static string RestoreLayout(string profileId)
    {
        var profile = Load(profileId);
        var (arrange, _) = AppServices.BuildUseCases();
        var result = arrange.RestoreLastLayout(profile);
        return JsonSerializer.Serialize(new
        {
            ok = !result.Aborted && result.Failures.Count == 0,
            action = "restore-layout",
            result.Moved,
            result.Failures,
            result.AbortReason,
        }, JsonOptions);
    }

    public static string ReadIcons()
    {
        var positioner = new DesktopIconPositioner();
        var ok = positioner.TryReadPositions(out var positions);
        return JsonSerializer.Serialize(new
        {
            ok,
            count = positions.Count,
            items = positions.Select(p => new { p.Name, p.X, p.Y }),
        }, JsonOptions);
    }

    public static string DisplayInfo()
    {
        var parameters = new DeskKeeper.Infrastructure.Windows.SystemLayoutParametersProvider().Get();
        var positioner = new DesktopIconPositioner();
        positioner.TryGetDpiScale(out var dpiScale);
        var screens = Screen.AllScreens
            .Select(s => new
            {
                s.DeviceName,
                X = s.Bounds.X,
                Y = s.Bounds.Y,
                Width = s.Bounds.Width,
                Height = s.Bounds.Height,
                s.Primary,
            })
            .ToArray();

        return JsonSerializer.Serialize(new
        {
            workArea = new
            {
                parameters.WorkAreaLeft,
                parameters.WorkAreaTop,
                parameters.WorkAreaWidth,
                parameters.WorkAreaHeight,
                parameters.CellWidth,
                parameters.CellHeight,
            },
            dpiScale,
            virtualScreen = new
            {
                X = GetSystemMetrics(76),
                Y = GetSystemMetrics(77),
                Width = GetSystemMetrics(78),
                Height = GetSystemMetrics(79),
            },
            screens,
        }, JsonOptions);
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private static RuleProfile Load(string profileId)
    {
        var config = App.ConfigStore.Load();
        App.SetConfig(config);
        if (!config.Profiles.TryGetValue(profileId, out var dto))
        {
            throw new InvalidOperationException($"配置中不存在 profile：{profileId}");
        }

        return ProfileFactory.ToDomain(profileId, dto);
    }

    private static string? GetScopeRoot() =>
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}

