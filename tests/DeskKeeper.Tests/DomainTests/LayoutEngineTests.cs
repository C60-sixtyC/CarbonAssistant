using DeskKeeper.Domain.Layout;
using DeskKeeper.Domain.Models;
using DeskKeeper.Domain.Rules;
using DeskKeeper.Tests.TestDoubles;

namespace DeskKeeper.Tests.DomainTests;

public class LayoutEngineTests
{
    private sealed class ReverseComparer : IComparer<string>
    {
        public int Compare(string? x, string? y) =>
            string.CompareOrdinal(y, x);
    }

    [Fact]
    public void Distributes_columns_by_ratio_and_fills_top_to_bottom()
    {
        var items = new[]
        {
            new ZoneItem(ItemFactory.AppLink(@"C:\d\A.lnk"), Zone.Apps),
            new ZoneItem(ItemFactory.AppLink(@"C:\d\B.lnk"), Zone.Apps),
            new ZoneItem(ItemFactory.AppLink(@"C:\d\C.lnk"), Zone.Apps),
            new ZoneItem(ItemFactory.Folder(@"C:\d\讲义"), Zone.Folders),
            new ZoneItem(ItemFactory.File(@"C:\d\笔记.txt"), Zone.Files),
        };

        var plan = LayoutEngine.Compute(items, 1920, 1080, 100, 100, new[] { 2.0, 2.5, 5.5 }, 4, true);

        Assert.Equal(19, plan.Columns);
        Assert.Equal(9, plan.FillRows);
        Assert.False(plan.DegradedToTwoColumns);
        Assert.Equal(4, plan.ZoneColumns[Zone.Apps]);
        Assert.Equal(5, plan.ZoneColumns[Zone.Folders]);
        Assert.Equal(10, plan.ZoneColumns[Zone.Files]);

        var apps = plan.Slots.Where(s => s.Zone == Zone.Apps).ToList();
        Assert.Equal(3, apps.Count);
        Assert.Equal(0, apps[0].XPct);
        Assert.Equal(0, apps[0].YPct);
        // 标题行预留：纵向从第 2 行起（100/1080 ≈ 9.26%）。
        Assert.InRange(apps[1].YPct, 9.0, 9.5);

        var files = plan.Slots.Single(s => s.Zone == Zone.Files);
        Assert.InRange(files.XPct, 20, 21); // 软件 4 列后进入文件区：(4*100)/1920 ≈ 20.8%
    }

    [Fact]
    public void Uses_injected_name_comparer()
    {
        var items = new[]
        {
            new ZoneItem(ItemFactory.AppLink(@"C:\d\A.lnk"), Zone.Apps),
            new ZoneItem(ItemFactory.AppLink(@"C:\d\B.lnk"), Zone.Apps),
            new ZoneItem(ItemFactory.AppLink(@"C:\d\C.lnk"), Zone.Apps),
        };

        var plan = LayoutEngine.Compute(items, 1920, 1080, 100, 100, new[] { 2.0, 2.5, 5.5 }, 4, true, new ReverseComparer());
        var apps = plan.Slots.Where(s => s.Zone == Zone.Apps).Select(s => s.Name).ToList();
        Assert.Equal(new[] { "C.lnk", "B.lnk", "A.lnk" }, apps);
    }

    [Fact]
    public void Degrades_to_two_columns_when_slots_too_few()
    {
        var items = new[]
        {
            new ZoneItem(ItemFactory.AppLink(@"C:\d\A.lnk"), Zone.Apps),
            new ZoneItem(ItemFactory.Folder(@"C:\d\讲义"), Zone.Folders),
            new ZoneItem(ItemFactory.File(@"C:\d\笔记.txt"), Zone.Files),
        };

        var plan = LayoutEngine.Compute(items, 320, 200, 100, 100, new[] { 2.0, 2.5, 5.5 }, 4, false);
        Assert.True(plan.DegradedToTwoColumns);
        Assert.Equal(2, plan.ZoneColumns[Zone.Apps]);
        Assert.Equal(1, plan.ZoneColumns[Zone.Folders]);
        Assert.Equal(2, plan.Slots.Count(s => s.Zone is Zone.Apps or Zone.Files));
        Assert.Equal(1, plan.Slots.Count(s => s.Zone == Zone.Folders));
    }

    [Fact]
    public void Overflow_items_are_reported_not_dropped_silently()
    {
        var items = Enumerable.Range(0, 3)
            .Select(i => new ZoneItem(ItemFactory.File($@"C:\d\f{i}.txt"), Zone.Files))
            .ToArray();

        var plan = LayoutEngine.Compute(items, 400, 100, 100, 100, new[] { 2.0, 2.5, 5.5 }, 1, false);
        Assert.Single(plan.Overflow);
        Assert.Equal(2, plan.Slots.Count);
    }

    [Fact]
    public void Empty_input_yields_empty_plan()
    {
        var plan = LayoutEngine.Compute(Array.Empty<ZoneItem>(), 1920, 1080, 100, 100, new[] { 2.0, 2.5, 5.5 }, 4, true);
        Assert.Empty(plan.Slots);
        Assert.Empty(plan.Overflow);
    }
}

