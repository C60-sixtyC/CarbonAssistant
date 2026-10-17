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
        IComparer<string>? nameComparer = null,
        double groupGapPixels = 32,
        int reservedSlotsInFirstZone = 0)
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
        var cellW = Math.Max(1, cellWidth);
        var cellH = Math.Max(1, cellHeight);
        var gap = Math.Clamp(groupGapPixels, 0, workAreaWidth);
        var reserved = Math.Max(0, reservedSlotsInFirstZone);

        // 分区定义：屏幕从左到右为 软件 | 单文件 | 文件夹。
        var ratioApps = ratios.Length > 0 ? ratios[0] : 1.0;
        var ratioFiles = ratios.Length > 2 ? ratios[2] : 0.0;
        var ratioFolders = ratios.Length > 1 ? ratios[1] : 0.0;

        var sum = Math.Max(0.0001, ratioApps + ratioFiles + ratioFolders);
        var appsWidth = workAreaWidth * ratioApps / sum;
        var restStart = Math.Min(workAreaWidth, appsWidth + gap);
        var restWidth = Math.Max(0, workAreaWidth - restStart);
        var filesWidth = restWidth * ratioFiles / Math.Max(0.0001, ratioFiles + ratioFolders);
        var foldersWidth = restWidth - filesWidth;

        var folderCols = Math.Max(1, (int)Math.Floor(foldersWidth / cellW));
        var folderStart = Math.Max(restStart, workAreaWidth - folderCols * cellW);
        var filesCols = Math.Max(1, (int)Math.Floor(filesWidth / cellW));
        var filesStart = Math.Max(0, folderStart - filesCols * cellW);
        var appsCols = Math.Max(1, (int)Math.Floor(appsWidth / cellW));

        var zones = new List<(Zone Name, Zone[] Absorbs, double StartX, int ColCount)>
        {
            (Zone.Apps, new[] { Zone.Apps }, 0.0, appsCols),
            (Zone.Files, new[] { Zone.Files }, filesStart, filesCols),
            (Zone.Folders, new[] { Zone.Folders }, folderStart, folderCols),
        };

        var degraded = false;
        var slotsOk = zones
            .Select((z, i) => (i == 0 ? z.ColCount * fillRows - reserved : z.ColCount * fillRows))
            .All(s => s >= minSlotsPerZone);
        if (!slotsOk)
        {
            // 降级为两列：软件与单文件合并，文件夹保持最右。
            var leftRatio = ratioApps + ratioFiles;
            var rightRatio = ratioFolders;
            var degradeSum = Math.Max(0.0001, leftRatio + rightRatio);
            var leftWidth = workAreaWidth * leftRatio / degradeSum;
            var degradeRestStart = Math.Min(workAreaWidth, leftWidth + gap);
            var degradeFoldersWidth = Math.Max(0, workAreaWidth - degradeRestStart);
            var degradeFolderCols = Math.Max(1, (int)Math.Floor(degradeFoldersWidth / cellW));
            var degradeFolderStart = Math.Max(degradeRestStart, workAreaWidth - degradeFolderCols * cellW);
            var leftCols = Math.Max(1, (int)Math.Floor(degradeFolderStart / cellW));

            zones = new List<(Zone Name, Zone[] Absorbs, double StartX, int ColCount)>
            {
                (Zone.Apps, new[] { Zone.Apps, Zone.Files }, 0.0, leftCols),
                (Zone.Folders, new[] { Zone.Folders }, degradeFolderStart, degradeFolderCols),
            };
            degraded = true;
        }

        var zoneColumnMap = new Dictionary<Zone, int>();
        var slots = new List<LayoutSlot>();
        var overflow = new List<LayoutSlot>();

        for (var zi = 0; zi < zones.Count; zi++)
        {
            var (zone, absorbed, startX, zoneColCount) = zones[zi];
            zoneColumnMap[zone] = zoneColCount;

            var zoneItems = list
                .Where(x => absorbed.Contains(x.Zone))
                .OrderBy(x => x.Item.Name, comparer)
                .ToList();

            var totalSlots = zoneColCount * fillRows;
            var firstSlot = zi == 0 ? reserved : 0;
            for (var i = 0; i < zoneItems.Count; i++)
            {
                var item = zoneItems[i];
                var slotIndex = firstSlot + i;
                if (slotIndex >= totalSlots)
                {
                    overflow.Add(new LayoutSlot(item.Item.FullPath, item.Item.Name, item.Zone, 0, 0));
                    continue;
                }

                var colInZone = slotIndex / fillRows;
                var rowInZone = slotIndex % fillRows;
                var x = startX + colInZone * cellW;
                var y = rowInZone * cellH;
                var xPct = ClampPct(x / workAreaWidth * 100);
                var yPct = ClampPct(y / workAreaHeight * 100);
                slots.Add(new LayoutSlot(item.Item.FullPath, item.Item.Name, item.Zone, xPct, yPct));
            }
        }

        return new LayoutPlan(slots, overflow, cols, fillRows, degraded, zoneColumnMap);
    }

    private static double ClampPct(double value) => Math.Clamp(value, 0, 100);
}

