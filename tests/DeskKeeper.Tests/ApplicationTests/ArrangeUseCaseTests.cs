using DeskKeeper.Application.Mapping;
using DeskKeeper.Application.UseCases;
using DeskKeeper.Domain.Models;
using DeskKeeper.Infrastructure.Audit;
using DeskKeeper.Infrastructure.Storage;
using DeskKeeper.Infrastructure.Windows;
using DeskKeeper.Tests.TestDoubles;

namespace DeskKeeper.Tests.ApplicationTests;

public class ArrangeUseCaseTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"dk-arrange-{Guid.NewGuid():N}");

    public ArrangeUseCaseTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch
        {
        }
    }

    private static RuleProfile Profile() => ProfileFactory.ToDomain(
        "classroom",
        ConfigStore.CreateDefaultConfig().Profiles["classroom"]);

    private static List<DesktopItem> ThreeItems() => new()
    {
        ItemFactory.AppLink(@"C:\Users\t\Desktop\会议.lnk"),
        ItemFactory.Folder(@"C:\Users\t\Desktop\讲义"),
        ItemFactory.File(@"C:\Users\t\Desktop\笔记.txt"),
    };

    private ArrangeUseCase BuildUseCase(ListItemSource source, RecordingPositioner positioner)
    {
        var snapshotStore = new SnapshotStore(_dir);
        return new ArrangeUseCase(
            source,
            positioner,
            new FixedLayoutProvider(0, 0, 1920, 1080, 96, 96),
            snapshotStore,
            new AuditLogger(Path.Combine(_dir, "logs"), clock: new FixedClock(DateTimeOffset.Now)),
            new NaturalStringComparer(),
            clock: new FixedClock(DateTimeOffset.Now));
    }

    [Fact]
    public void Preview_plans_all_movable_items()
    {
        var useCase = BuildUseCase(new ListItemSource(ThreeItems()), new RecordingPositioner { AutoArrange = false });
        var preview = useCase.Preview(Profile());

        Assert.Equal(3, preview.PlannedSlots.Count);
        Assert.False(preview.OneDriveDetected);
        Assert.Equal(20, preview.Columns);
    }

    [Fact]
    public void Apply_moves_everything_and_persists_snapshot()
    {
        var positioner = new RecordingPositioner { AutoArrange = false };
        var useCase = BuildUseCase(new ListItemSource(ThreeItems()), positioner);
        var preview = useCase.Preview(Profile());

        var result = useCase.Apply(preview, Profile());
        Assert.False(result.Aborted);
        Assert.Equal(3, result.Moved);
        Assert.Equal(3, positioner.Moved.Count);

        var store = new SnapshotStore(_dir);
        Assert.False(store.HasPendingRollback);
        Assert.Equal(3, store.Load().Snapshot!.Items.Count);
    }

    [Fact]
    public void Auto_arrange_enabled_aborts_apply()
    {
        var useCase = BuildUseCase(new ListItemSource(ThreeItems()), new RecordingPositioner { AutoArrange = true });
        var preview = useCase.Preview(Profile());
        Assert.True(preview.AutoArrangeEnabled);

        var result = useCase.Apply(preview, Profile());
        Assert.True(result.Aborted);
        Assert.Contains("自动排列", result.AbortReason);
    }

    [Fact]
    public void Pinned_items_are_kept_and_second_apply_is_incremental()
    {
        var positioner = new RecordingPositioner { AutoArrange = false };
        var useCase = BuildUseCase(new ListItemSource(ThreeItems()), positioner);
        var first = useCase.Preview(Profile());
        useCase.Apply(first, Profile());

        var store = new SnapshotStore(_dir);
        var snapshot = store.Load().Snapshot!;
        var pinnedPath = snapshot.Items[0].Path;
        snapshot.Items[0].Pinned = true;
        store.Save(snapshot);

        var second = useCase.Preview(Profile());
        Assert.Single(second.KeptSlots);
        Assert.Equal(pinnedPath, second.KeptSlots[0].Path);
        Assert.Equal(2, second.PlannedSlots.Count);

        var result = useCase.Apply(second, Profile());
        Assert.Equal(2, result.Moved);
    }

    [Fact]
    public void Restore_from_rollback_repositions_snapshot()
    {
        var store = new SnapshotStore(_dir);
        store.SaveRollback(new Snapshot
        {
            Items =
            {
                new SnapshotItem { Path = @"C:\d\a.lnk", Name = "a.lnk", Zone = "apps", XPct = 10, YPct = 10 },
            },
        });

        var positioner = new RecordingPositioner { AutoArrange = false };
        var useCase = BuildUseCase(new ListItemSource(ThreeItems()), positioner);
        var result = useCase.RestoreFromRollback(Profile());
        Assert.Equal(1, result.Moved);
        Assert.False(store.HasPendingRollback);
    }
}

