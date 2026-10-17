namespace DeskKeeper.Domain.Sorting;

/// <summary>
/// 与 Windows 资源管理器一致的自然排序抽象。
/// Domain 不直接 P/Invoke，具体实现（StrCmpLogicalW）放在 Infrastructure。
/// </summary>
public interface INaturalStringComparer : IComparer<string>
{
}

