using DeskKeeper.Domain.Contracts;
using DeskKeeper.Domain.Models;

namespace DeskKeeper.Infrastructure.Desktop;

/// <summary>
/// 桌面枚举器：仅枚举用户桌面与公共桌面（作用域锁死，无全盘递归入口）。
/// 屏蔽 desktop.ini、隐藏/系统属性项与系统虚拟图标。
/// </summary>
public sealed class DesktopEnumerator : IDesktopItemSource
{
    private static readonly string[] AppLinkExtensions = { ".lnk", ".url", ".appref-ms" };

    private readonly IShellLinkResolver _linkResolver;
    private readonly IReadOnlyList<(string Root, string Label)> _roots;

    public DesktopEnumerator(IShellLinkResolver linkResolver, IReadOnlyList<(string Root, string Label)>? roots = null)
    {
        _linkResolver = linkResolver;
        _roots = roots ?? DefaultRoots();
    }

    public IReadOnlyList<DesktopItem> Enumerate()
    {
        var result = new List<DesktopItem>();
        foreach (var (root, label) in _roots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            var isPublic = label.Equals("public", StringComparison.OrdinalIgnoreCase);
            var oneDrive = root.Contains("OneDrive", StringComparison.OrdinalIgnoreCase);

            foreach (var fsEntry in new DirectoryInfo(root).EnumerateFileSystemInfos())
            {
                try
                {
                    if (fsEntry.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)
                        || fsEntry.Attributes.HasFlag(FileAttributes.Hidden)
                        || fsEntry.Attributes.HasFlag(FileAttributes.System))
                    {
                        continue;
                    }

                    if (SystemIconDetector.IsSystemIconName(fsEntry.Name))
                    {
                        continue;
                    }

                    if (fsEntry.Attributes.HasFlag(FileAttributes.Directory))
                    {
                        string? ownIni = null;
                        try
                        {
                            var ownIniPath = Path.Combine(fsEntry.FullName, "desktop.ini");
                            if (File.Exists(ownIniPath))
                            {
                                ownIni = File.ReadAllText(ownIniPath);
                            }
                        }
                        catch
                        {
                            ownIni = null;
                        }

                        if (SystemIconDetector.IsClsidMarkedFolder(ownIni))
                        {
                            continue;
                        }
                    }

                    var item = Classify(fsEntry, isPublic, oneDrive, label);
                    if (item is not null)
                    {
                        result.Add(item);
                    }
                }
                catch
                {
                    // 单项失败不影响整体枚举。
                }
            }
        }

        return result;
    }

    private DesktopItem? Classify(
        FileSystemInfo entry,
        bool isPublic,
        bool oneDrive,
        string source)
    {
        var isDirectory = entry.Attributes.HasFlag(FileAttributes.Directory);
        var fullPath = entry.FullName;
        var size = 0L;
        if (!isDirectory && entry is FileInfo fileInfo)
        {
            size = fileInfo.Length;
        }

        var ext = Path.GetExtension(fullPath);
        DesktopItemKind kind;
        string? linkTarget = null;

        if (isDirectory)
        {
            kind = DesktopItemKind.Folder;
        }
        else if (AppLinkExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
        {
            kind = DesktopItemKind.AppLink;
            linkTarget = _linkResolver.ResolveTarget(fullPath);
        }
        else if (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            kind = DesktopItemKind.AppLink;
            linkTarget = fullPath;
        }
        else
        {
            kind = DesktopItemKind.File;
        }

        return new DesktopItem(
            Name: entry.Name,
            FullPath: fullPath,
            Kind: kind,
            SizeBytes: size,
            CreatedTime: new DateTimeOffset(entry.CreationTime),
            ModifiedTime: new DateTimeOffset(entry.LastWriteTime),
            AccessedTime: new DateTimeOffset(entry.LastAccessTime),
            AccessedTimeReliable: false,
            IsPinned: false,
            IsReadOnly: isPublic,
            LinkTargetExe: linkTarget is not null && linkTarget.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? linkTarget
                : null,
            IsOneDriveManaged: oneDrive,
            SourceDesktop: source);
    }

    private static IReadOnlyList<(string, string)> DefaultRoots()
    {
        var roots = new List<(string, string)>(2);
        if (KnownFolders.TryGetPath(Windows.NativeMethods.FolderIdDesktop, out var userDesktop))
        {
            roots.Add((userDesktop, "user"));
        }

        if (KnownFolders.TryGetPath(Windows.NativeMethods.FolderIdPublicDesktop, out var publicDesktop))
        {
            roots.Add((publicDesktop, "public"));
        }

        return roots;
    }
}

internal static class KnownFolders
{
    internal static bool TryGetPath(Guid folderId, out string path)
    {
        var hr = Windows.NativeMethods.SHGetKnownFolderPath(folderId, 0, IntPtr.Zero, out var p);
        if (hr == 0 && !string.IsNullOrWhiteSpace(p))
        {
            path = p;
            return true;
        }

        path = string.Empty;
        return false;
    }
}

