using DeskKeeper.Application.Mapping;
using DeskKeeper.Application.UseCases;
using DeskKeeper.Domain.Contracts;
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

    private static RuleProfile Profile()
    {
        var profile = ProfileFactory.ToDomain(
            "classroom",
            ConfigStore.CreateDefaultConfig().Profiles["classroom"]);
        return profile with
        {
            Layout = profile.Layout with
            {
                PinThisPcTopLeft = false,
                PinRecycleBinTopLeftSecond = false,
            },
        };
    }

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

    [Fact]
    public void Apply_captures_undo_and_restore_moves_back()
    {
        var positioner = new RecordingPositioner { AutoArrange = false };
        positioner.PositionsToRead = new List<IconPosition>
        {
            new("会议.lnk", 120, 80),
            new("讲义", 200, 100),
            new("笔记.txt", 300, 120),
        };
        var useCase = BuildUseCase(new ListItemSource(ThreeItems()), positioner);
        var preview = useCase.Preview(Profile());
        useCase.Apply(preview, Profile());

        var store = new SnapshotStore(_dir);
        var undo = store.LoadUndo();
        Assert.NotNull(undo);
        Assert.Equal(3, undo!.Count);

        var restored = useCase.RestoreLastLayout(Profile());
        Assert.False(restored.Aborted);
        Assert.Equal(3, restored.Moved);
        Assert.Equal(120.0, positioner.Moved[3].X);
        Assert.Equal(80.0, positioner.Moved[3].Y);
        Assert.Equal(300.0, positioner.Moved[5].X);
        Assert.Null(store.LoadUndo());
    }

    [Fact]
    public void System_icons_are_pinned_to_top_left_slots_when_enabled()
    {
        var positioner = new RecordingPositioner { AutoArrange = false };
        var useCase = BuildUseCase(new ListItemSource(ThreeItems()), positioner);
        var profile = Profile();
        profile = profile with
        {
            Layout = profile.Layout with
            {
                PinThisPcTopLeft = true,
                PinRecycleBinTopLeftSecond = true,
            },
        };

        var preview = useCase.Preview(profile);
        Assert.Equal(5, preview.PlannedSlots.Count);
        Assert.Equal("此电脑", preview.PlannedSlots[0].Name);
        Assert.Equal(0, preview.PlannedSlots[0].XPct);
        Assert.Equal(0, preview.PlannedSlots[0].YPct);
        Assert.Equal("回收站", preview.PlannedSlots[1].Name);
        Assert.InRange(preview.PlannedSlots[1].YPct, 8.0, 9.0);
    }
}

