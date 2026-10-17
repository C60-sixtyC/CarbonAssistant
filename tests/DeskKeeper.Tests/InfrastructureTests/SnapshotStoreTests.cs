using DeskKeeper.Infrastructure.Storage;

namespace DeskKeeper.Tests.InfrastructureTests;

public class SnapshotStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"dk-snap-{Guid.NewGuid():N}");

    public SnapshotStoreTests() => Directory.CreateDirectory(_dir);

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

    private static Snapshot MakeSnapshot() => new()
    {
        SavedAt = DateTimeOffset.Now,
        WorkAreaWidth = 1920,
        WorkAreaHeight = 1080,
        Items =
        {
            new SnapshotItem
            {
                Path = @"C:\d\a.lnk", Name = "a.lnk", Zone = "apps", XPct = 1, YPct = 2, Pinned = true,
            },
        },
    };

    [Fact]
    public void Save_load_round_trip()
    {
        var store = new SnapshotStore(_dir);
        store.Save(MakeSnapshot());
        var (loaded, fromBackup) = store.Load();
        Assert.False(fromBackup);
        Assert.NotNull(loaded);
        Assert.Single(loaded!.Items);
        Assert.Equal("apps", loaded.Items[0].Zone);
    }

    [Fact]
    public void Corrupted_snapshot_falls_back_to_bak()
    {
        var store = new SnapshotStore(_dir);
        store.Save(MakeSnapshot());
        var snapshot = MakeSnapshot();
        snapshot.Items[0].Zone = "files";
        store.Save(snapshot);

        File.WriteAllText(store.SnapshotPath, "garbage");
        var (loaded, fromBackup) = store.Load();
        Assert.True(fromBackup);
        Assert.Equal("apps", loaded!.Items[0].Zone);
    }

    [Fact]
    public void Rollback_pending_lifecycle()
    {
        var store = new SnapshotStore(_dir);
        Assert.False(store.HasPendingRollback);
        store.SaveRollback(MakeSnapshot());
        Assert.True(store.HasPendingRollback);
        Assert.NotNull(store.LoadRollback());
        store.ClearRollback();
        Assert.False(store.HasPendingRollback);
    }
}

