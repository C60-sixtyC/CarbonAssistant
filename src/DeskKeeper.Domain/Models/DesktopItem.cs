namespace DeskKeeper.Domain.Models;

public enum DesktopItemKind
{
    AppLink,
    Folder,
    File,
    SystemIcon,
    Unknown,
}

/// <summary>
/// 规范化桌面项。系统虚拟图标与隐藏项由枚举器负责过滤，
/// 此处描述的是「可参与布局/清理决策」的最小元数据集。
/// </summary>
public sealed record DesktopItem(
    string Name,
    string FullPath,
    DesktopItemKind Kind,
    long SizeBytes,
    DateTimeOffset CreatedTime,
    DateTimeOffset ModifiedTime,
    DateTimeOffset? AccessedTime,
    bool AccessedTimeReliable,
    bool IsPinned,
    bool IsReadOnly,
    string? LinkTargetExe,
    bool IsOneDriveManaged,
    string SourceDesktop);

