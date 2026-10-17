using DeskKeeper.Domain.Contracts;
using DeskKeeper.Domain.Models;
using DeskKeeper.Infrastructure.Desktop;
using DeskKeeper.Infrastructure.Shell;

namespace DeskKeeper.Tests.TestDoubles;

public sealed class FixedClock : IClock
{
    public FixedClock(DateTimeOffset now) => Now = now;
    public DateTimeOffset Now { get; }
}

public sealed class ListItemSource : IDesktopItemSource
{
    private readonly List<DesktopItem> _items;

    public ListItemSource(List<DesktopItem> items) => _items = items;

    public IReadOnlyList<DesktopItem> Enumerate() => _items;
}

public sealed class FixedLayoutProvider : ILayoutParametersProvider
{
    private readonly LayoutParameters _parameters;

    public FixedLayoutProvider(double left, double top, double w, double h, double cellW, double cellH) =>
        _parameters = new LayoutParameters(left, top, w, h, cellW, cellH);

    public LayoutParameters Get() => _parameters;
}

public sealed class RecordingPositioner : IDesktopIconPositioner
{
    public bool? AutoArrange { get; set; }
    public List<(string Path, double X, double Y)> Moved { get; } = new();

    public bool TryMoveIcon(string path, string name, double x, double y, out string? error)
    {
        Moved.Add((path, x, y));
        error = null;
        return true;
    }

    public bool? TryDetectAutoArrange() => AutoArrange;
}

public sealed class DictionaryRegistryReader : IRegistryReader
{
    private readonly Dictionary<(string Key, string Value), uint?> _values = new();

    public DictionaryRegistryReader Set(string key, string value, uint? v)
    {
        _values[(key, value)] = v;
        return this;
    }

    public uint? GetDWord(string subKeyPath, string valueName) =>
        _values.TryGetValue((subKeyPath, valueName), out var v) ? v : null;
}

public sealed class FakeLinkResolver : IShellLinkResolver
{
    private readonly Dictionary<string, string> _targets = new(StringComparer.OrdinalIgnoreCase);

    public FakeLinkResolver Add(string linkPath, string target)
    {
        _targets[linkPath] = target;
        return this;
    }

    public string? ResolveTarget(string linkPath) =>
        _targets.TryGetValue(linkPath, out var target) ? target : null;
}

public static class ItemFactory
{
    public static DesktopItem File(string path, long size = 1024, int ageDays = 0) => new(
        Path.GetFileName(path),
        path,
        DesktopItemKind.File,
        size,
        DateTimeOffset.Now.AddDays(-ageDays - 1),
        DateTimeOffset.Now.AddDays(-ageDays),
        null,
        false,
        false,
        false,
        null,
        false,
        "user");

    public static DesktopItem Folder(string path, int ageDays = 0) => new(
        Path.GetFileName(path),
        path,
        DesktopItemKind.Folder,
        0,
        DateTimeOffset.Now.AddDays(-ageDays - 1),
        DateTimeOffset.Now.AddDays(-ageDays),
        null,
        false,
        false,
        false,
        null,
        false,
        "user");

    public static DesktopItem AppLink(string path) => new(
        Path.GetFileName(path),
        path,
        DesktopItemKind.AppLink,
        1024,
        DateTimeOffset.Now,
        DateTimeOffset.Now,
        null,
        false,
        false,
        false,
        @"C:\Program Files\App\app.exe",
        false,
        "user");
}

