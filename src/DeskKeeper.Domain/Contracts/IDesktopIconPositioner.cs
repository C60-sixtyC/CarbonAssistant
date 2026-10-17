namespace DeskKeeper.Domain.Contracts;

/// <summary>
/// 桌面图标落位器。真实实现通过 SysListView32 消息移动图标；
/// 测试用假实现仅记录调用，绝不触碰真实桌面。
/// </summary>
public interface IDesktopIconPositioner
{
    bool TryMoveIcon(string path, string name, double x, double y, out string? error);

    /// <summary>“查看 → 自动排列图标”是否开启；无法判定时返回 null。</summary>
    bool? TryDetectAutoArrange();
}

