using System.Text.RegularExpressions;

namespace DeskKeeper.Infrastructure.Desktop;

/// <summary>
/// 系统虚拟图标识别：
/// 1) 名称形如 "xx.{GUID}"（CLSID 命名项）；
/// 2) desktop.ini 中存在 CLSID 且项名匹配该 GUID。
/// 命中即标记 SystemIcon，后续一律跳过移动与删除。
/// </summary>
public static partial class SystemIconDetector
{
    [GeneratedRegex(@"\.\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}$", RegexOptions.Compiled)]
    private static partial Regex ClsidNamePattern();

    [GeneratedRegex(@"CLSID\s*=\s*\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex IniClsidPattern();

    public static bool IsSystemIconName(string name) => ClsidNamePattern().IsMatch(name);

    public static bool IsClsidMarkedFolder(string? desktopIniContent)
    {
        if (string.IsNullOrEmpty(desktopIniContent))
        {
            return false;
        }

        return IniClsidPattern().IsMatch(desktopIniContent);
    }
}

