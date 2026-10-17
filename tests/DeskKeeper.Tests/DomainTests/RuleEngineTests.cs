using DeskKeeper.Domain.Models;
using DeskKeeper.Domain.Rules;
using DeskKeeper.Tests.TestDoubles;

namespace DeskKeeper.Tests.DomainTests;

public class RuleEngineTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);

    private static RuleEngine BuildEngine() => new(new[]
    {
        new Rule(100, "apps", new RuleMatch(Kind: DesktopItemKind.AppLink), new RuleAction(Zone: Zone.Apps)),
        new Rule(200, "folders", new RuleMatch(Kind: DesktopItemKind.Folder), new RuleAction(Zone: Zone.Folders)),
        new Rule(300, "lnk", new RuleMatch(Extensions: new[] { "*.lnk" }), new RuleAction(Exclude: true, Disposal: Disposal.Skip)),
        new Rule(400, "schedule", new RuleMatch(NameGlob: "*课表*"), new RuleAction(Exclude: true, Disposal: Disposal.Skip)),
        new Rule(500, "junk", new RuleMatch(Extensions: new[] { "*.tmp" }, SizeMaxKB: 1024), new RuleAction(Disposal: Disposal.Recycle)),
        new Rule(600, "old", new RuleMatch(ModifiedOlderThanDays: 30), new RuleAction(Zone: Zone.Files, Disposal: Disposal.Archive)),
        new Rule(9000, "default", new RuleMatch(), new RuleAction(Zone: Zone.Files)),
    });

    [Fact]
    public void First_match_wins_for_applinks()
    {
        var item = ItemFactory.AppLink(@"C:\Users\t\Desktop\微信.lnk");
        var decision = BuildEngine().Evaluate(item, Now);
        Assert.Equal(Zone.Apps, decision.Zone);
        Assert.False(decision.Exclude);
        Assert.Equal("apps", decision.RuleId);
    }

    [Fact]
    public void Folder_goes_to_folder_zone()
    {
        var decision = BuildEngine().Evaluate(ItemFactory.Folder(@"C:\Users\t\Desktop\讲义"), Now);
        Assert.Equal(Zone.Folders, decision.Zone);
    }

    [Fact]
    public void Lnk_named_file_is_excluded()
    {
        var item = ItemFactory.File(@"C:\Users\t\Desktop\某文件.lnk");
        var decision = BuildEngine().Evaluate(item, Now);
        Assert.True(decision.Exclude);
        Assert.Equal(Disposal.Skip, decision.Disposal);
    }

    [Fact]
    public void Name_glob_excludes_schedule()
    {
        var item = ItemFactory.File(@"C:\Users\t\Desktop\三班课表.xlsx");
        Assert.True(BuildEngine().Evaluate(item, Now).Exclude);
    }

    [Fact]
    public void Size_gate_limits_junk_rule()
    {
        var big = ItemFactory.File(@"C:\Users\t\Desktop\大文件.tmp", size: 5L * 1024 * 1024);
        var decision = BuildEngine().Evaluate(big, Now);
        Assert.False(decision.Exclude);
        Assert.Equal(Disposal.Archive, decision.Disposal);

        var small = ItemFactory.File(@"C:\Users\t\Desktop\小文件.tmp", size: 512 * 1024);
        Assert.Equal(Disposal.Recycle, BuildEngine().Evaluate(small, Now).Disposal);
    }

    [Fact]
    public void Modified_older_rule_uses_clock()
    {
        var old = ItemFactory.File(@"C:\Users\t\Desktop\旧文件.txt", ageDays: 40);
        var fresh = ItemFactory.File(@"C:\Users\t\Desktop\新文件.txt", ageDays: 5);
        Assert.Equal(600, BuildEngine().Evaluate(old, Now).RuleOrder);
        Assert.Equal(9000, BuildEngine().Evaluate(fresh, Now).RuleOrder);
    }

    [Fact]
    public void Unreliable_access_time_never_matches_accessed_rule()
    {
        var engine = new RuleEngine(new[]
        {
            new Rule(100, "accessed", new RuleMatch(AccessedOlderThanDays: 10), new RuleAction(Zone: Zone.Files)),
            new Rule(200, "default", new RuleMatch(), new RuleAction(Zone: Zone.Apps)),
        });
        var unreliable = new DesktopItem(
            "x.txt", @"C:\x.txt", DesktopItemKind.File, 10,
            Now.AddDays(-30), Now.AddDays(-30), Now.AddDays(-30), false, false, false, null, false, "user");
        Assert.Equal(200, engine.Evaluate(unreliable, Now).RuleOrder);

        var reliable = unreliable with { AccessedTimeReliable = true };
        Assert.Equal(100, engine.Evaluate(reliable, Now).RuleOrder);
    }

    [Fact]
    public void Default_decision_is_files_archive()
    {
        var decision = new RuleEngine(Array.Empty<Rule>()).Evaluate(
            ItemFactory.File(@"C:\Users\t\Desktop\anything.txt"), Now);
        Assert.Equal(Zone.Files, decision.Zone);
        Assert.Equal(Disposal.Archive, decision.Disposal);
    }
}

