namespace DeskKeeper.Infrastructure.Desktop;

public interface IShellLinkResolver
{
    /// <summary>解析快捷方式目标；无法解析返回 null。</summary>
    string? ResolveTarget(string linkPath);
}

