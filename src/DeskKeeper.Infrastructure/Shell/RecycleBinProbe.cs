using System.Runtime.InteropServices;
using DeskKeeper.Infrastructure.Windows;

namespace DeskKeeper.Infrastructure.Shell;

public sealed record RecycleBinStatus(
    bool NukeOnDeleteEnabled,
    long UsedBytes,
    long ItemCount,
    long? CapacityBytes,
    long AvailableBytes);

/// <summary>
/// 回收站四项预检之一：
/// NukeOnDelete 探测、容量上限与可用空间推算（SHQueryRecycleBin + 注册表 MaxCapacity）。
/// </summary>
public sealed class RecycleBinProbe
{
    private const string BitBucketKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\BitBucket";

    private readonly IRegistryReader _registry;

    public RecycleBinProbe(IRegistryReader? registry = null)
    {
        _registry = registry ?? new CurrentUserRegistryReader();
    }

    public RecycleBinStatus Probe(string? rootPath = null)
    {
        var nuke = _registry.GetDWord(BitBucketKey, "NukeOnDelete") == 1;

        var info = new NativeMethods.SHQUERYRBINFO { cbSize = Marshal.SizeOf<NativeMethods.SHQUERYRBINFO>() };
        var hr = NativeMethods.SHQueryRecycleBin(rootPath, ref info);
        if (hr != 0)
        {
            return new RecycleBinStatus(nuke, 0, 0, null, 0);
        }

        long? capacity = null;
        if (rootPath is not null)
        {
            var guid = GetVolumeGuid(rootPath);
            if (guid is not null)
            {
                var maxMb = _registry.GetDWord($@"{BitBucketKey}\Volume\{guid}", "MaxCapacity");
                if (maxMb is { } mb && mb > 0)
                {
                    capacity = (long)mb * 1024 * 1024;
                }
            }
        }

        var available = capacity is { } cap ? Math.Max(0, cap - info.i64Size) : long.MaxValue;
        return new RecycleBinStatus(nuke, info.i64Size, info.i64NumItems, capacity, available);
    }

    public static bool IsRemovableOrNetwork(string path)
    {
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root))
        {
            return false;
        }

        if (root.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return true;
        }

        const uint DriveRemovable = 2;
        const uint DriveRemote = 4;
        var type = NativeMethods.GetDriveType(root);
        return type is DriveRemovable or DriveRemote;
    }

    private static string? GetVolumeGuid(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path);
            return string.IsNullOrEmpty(root) || root.StartsWith(@"\\", StringComparison.Ordinal)
                ? null
                : new DriveInfo(root).Name.TrimEnd('\\');
        }
        catch
        {
            return null;
        }
    }
}

