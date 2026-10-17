using System;
using System.Linq;
using System.Windows.Forms;
using DeskKeeper.Infrastructure.Desktop;
using DeskKeeper.Infrastructure.Windows;

namespace DeskKeeper.App.Services;

public sealed record DisplayInfo(
    int MonitorCount,
    string PrimaryResolution,
    string WorkArea,
    string Grid,
    double DpiScale)
{
    public string Summary =>
        $"显示器 {MonitorCount} 台 · 主屏 {PrimaryResolution} · 工作区 {WorkArea} · 图标网格 {Grid} · 坐标缩放 {DpiScale:P0}";
}

/// <summary>
/// 自动检测显示参数：显示器数量/分辨率、工作区、真实图标网格间距与 DPI 缩放比例。
/// 布局用例每次运行时都会调用这些值，因此无需人工配置。
/// </summary>
public static class DisplayDetector
{
    public static DisplayInfo Detect()
    {
        var parameters = new SystemLayoutParametersProvider().Get();
        var positioner = new DesktopIconPositioner();
        positioner.TryGetDpiScale(out var dpiScale);

        var grid = $"{parameters.CellWidth:F0}×{parameters.CellHeight:F0}（系统默认）";
        if (positioner.TryGetSpacing(out var spacingW, out var spacingH)
            && spacingW > 0 && spacingH > 0)
        {
            grid = $"{spacingW:F0}×{spacingH:F0}（桌面视图）";
        }

        var screens = Screen.AllScreens;
        var primary = screens.FirstOrDefault(s => s.Primary) ?? screens.FirstOrDefault();
        var primaryResolution = primary is null
            ? "未知"
            : $"{primary.Bounds.Width}×{primary.Bounds.Height}";

        return new DisplayInfo(
            screens.Length,
            primaryResolution,
            $"{parameters.WorkAreaWidth:F0}×{parameters.WorkAreaHeight:F0}",
            grid,
            dpiScale);
    }
}

