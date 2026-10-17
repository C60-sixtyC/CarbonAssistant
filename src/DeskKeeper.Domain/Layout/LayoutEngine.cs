using DeskKeeper.Domain.Models;
using DeskKeeper.Domain.Rules;

namespace DeskKeeper.Domain.Layout;

public sealed record ZoneItem(DesktopItem Item, Zone Zone);

public sealed record LayoutSlot(
    string Path,
    string Name,
    Zone Zone,
    double XPct,
    double YPct);

public sealed record LayoutPlan(
    IReadOnlyList<LayoutSlot> Slots,
    IReadOnlyList<LayoutSlot> Overflow,
    int Columns,
    int FillRows,
    bool DegradedToTwoColumns,
    IReadOnlyDictionary<Zone, int> ZoneColumns)
{
    public static LayoutPlan Empty { get; } = new(
        Array.Empty<LayoutSlot>(),
        Array.Empty<LayoutSlot>(),
        0,
        0,
        false,
        new Dictionary<Zone, int>());
}

/// <summary>
/// 三分区布局引擎（纯函数）：
/// 按比例切分列宽，区内按自然序自上而下、自左而右填充；
/// 输出百分比坐标，跨分辨率恢复时重算。
/// </summary>
public static class LayoutEngine
{
    public static LayoutPlan Compute(
        IEnumerable<ZoneItem> items,
        double workAreaWidth,
        double workAreaHeight,
        double cellWidth,
        double cellHeight,
        double[] zoneRatios,
        int minSlotsPerZone,
        bool reserveTitleRow,
        IComparer<string>? nameComparer = null)
    {
        var list = items.ToList();
        if (list.Count == 0 || workAreaWidth <= 0 || workAreaHeight <= 0)
        {
            return LayoutPlan.Empty;
        }

        var comparer = nameComparer ?? StringComparer.Ordinal;
        var ratios = zoneRatios is { Length: > 0 } ? zoneRatios : new[] { 1.0 };
        if (ratios.Any(r => r < 0) || ratios.Sum() <= 0)
        {
            throw new ArgumentException("zoneRatios 必须为非负数且总和大于 0", nameof(zoneRatios));
        }

        var cols = Math.Max(1, (int)Math.Floor(workAreaWidth / Math.Max(1, cellWidth)));
        var rows = Math.Max(1, (int)Math.Floor(workAreaHeight / Math.Max(1, cellHeight)));
        var fillRows = reserveTitleRow ? Math.Max(1, rows - 1) : rows;

        // 分区定义：屏幕从左到右为 软件 | 单文件 | 文件夹。
        var ratioApps = ratios.Length > 0 ? ratios[0] : 1.0;
        var ratioFiles = ratios.Length > 2 ? ratios[2] : 0.0;
        var ratioFolders = ratios.Length > 1 ? ratios[1] : 0.0;

        var zones = new List<(Zone Name, double Ratio, Zone[] Absorbs)>
        {
            (Zone.Apps, ratioApps, new[] { Zone.Apps }),
            (Zone.Files, ratioFiles, new[] { Zone.Files }),
            (Zone.Folders, ratioFolders, new[] { Zone.Folders }),
        }.Where(z => z.Ratio > 0).ToList();

        var degraded = false;
        if (zones.Count == 3)
        {
            var trial = DistributeColumns(cols, zones.Select(z => z.Ratio).ToArray());
            var ok = trial.All(c => c > 0 && c * fillRows >= minSlotsPerZone);
            if (!ok)
            {
                // 降级为两列：软件与单文件合并，文件夹保持最右。
                zones = new List<(Zone, double, Zone[])>
                {
                    (Zone.Apps, ratioApps + ratioFiles, new[] { Zone.Apps, Zone.Files }),
                    (Zone.Folders, ratioFolders, new[] { Zone.Folders }),
                };
                degraded = true;
            }
        }

        var zoneColumns = DistributeColumns(cols, zones.Select(z => z.Ratio).ToArray());
        var zoneColumnMap = new Dictionary<Zone, int>();

        var slots = new List<LayoutSlot>();
        var overflow = new List<LayoutSlot>();
        var startX = 0.0;

        for (var zi = 0; zi < zones.Count; zi++)
        {
            var zone = zones[zi].Name;
            var absorbed = zones[zi].Absorbs;
            var zoneColCount = Math.Max(0, zoneColumns[zi]);
            zoneColumnMap[zone] = zoneColCount;

            var zoneItems = list
                .Where(x => absorbed.Contains(x.Zone))
                .OrderBy(x => x.Item.Name, comparer)
                .ToList();

            var capacity = zoneColCount * fillRows;
            for (var i = 0; i < zoneItems.Count; i++)
            {
                var item = zoneItems[i];
                if (i >= capacity)
                {
                    overflow.Add(new LayoutSlot(item.Item.FullPath, item.Item.Name, item.Zone, 0, 0));
                    continue;
                }

                var colInZone = i / fillRows;
                var rowInZone = i % fillRows;
                var x = startX + colInZone * cellWidth;
                var y = rowInZone * cellHeight;
                var xPct = ClampPct(x / workAreaWidth * 100);
                var yPct = ClampPct(y / workAreaHeight * 100);
                slots.Add(new LayoutSlot(item.Item.FullPath, item.Item.Name, item.Zone, xPct, yPct));
            }

            startX += zoneColCount * cellWidth;
        }

        return new LayoutPlan(slots, overflow, cols, fillRows, degraded, zoneColumnMap);
    }

    private static int[] DistributeColumns(int total, double[] ratios)
    {
        var result = new int[ratios.Length];
        if (total <= 0)
        {
            return result;
        }

        var sum = ratios.Sum();
        var used = 0;
        var fractions = new double[ratios.Length];
        for (var i = 0; i < ratios.Length; i++)
        {
            var raw = total * ratios[i] / sum;
            result[i] = Math.Max(0, (int)Math.Floor(raw));
            fractions[i] = raw - result[i];
            used += result[i];
        }

        foreach (var i in Enumerable.Range(0, ratios.Length)
                     .OrderByDescending(i => fractions[i]))
        {
            if (used >= total)
            {
                break;
            }

            result[i]++;
            used++;
        }

        return result;
    }

    private static double ClampPct(double value) => Math.Clamp(value, 0, 100);
}

