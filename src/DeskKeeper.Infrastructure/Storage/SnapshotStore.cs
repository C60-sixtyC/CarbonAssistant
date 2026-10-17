using System.Text.Json;

namespace DeskKeeper.Infrastructure.Storage;

public sealed class SnapshotItem
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public string Zone { get; set; } = "files";
    public double XPct { get; set; }
    public double YPct { get; set; }
    public bool Pinned { get; set; }
}

public sealed class Snapshot
{
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset SavedAt { get; set; }
    public double WorkAreaWidth { get; set; }
    public double WorkAreaHeight { get; set; }
    public List<SnapshotItem> Items { get; set; } = new();
}

/// <summary>
/// 布局快照：保存前写 .bak；应用布局前写回滚副本（pending 标记），
/// 崩溃后下次启动检测到 pending 即回滚。
/// </summary>
public sealed class SnapshotStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _baseDir;

    public SnapshotStore(string baseDir)
    {
        _baseDir = baseDir;
        Directory.CreateDirectory(baseDir);
    }

    public string SnapshotPath => Path.Combine(_baseDir, "snapshot.json");
    public string RollbackPath => Path.Combine(_baseDir, "rollback.json");

    public void Save(Snapshot snapshot)
    {
        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        if (File.Exists(SnapshotPath))
        {
            File.Copy(SnapshotPath, SnapshotPath + ".bak", overwrite: true);
        }

        var temp = SnapshotPath + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, SnapshotPath, overwrite: true);
    }

    public (Snapshot? Snapshot, bool FromBackup) Load()
    {
        var snapshot = TryLoad(SnapshotPath);
        if (snapshot is not null)
        {
            return (snapshot, false);
        }

        return (TryLoad(SnapshotPath + ".bak"), true);
    }

    public void SaveRollback(Snapshot snapshot)
    {
        File.WriteAllText(RollbackPath, JsonSerializer.Serialize(snapshot, JsonOptions));
    }

    public bool HasPendingRollback => File.Exists(RollbackPath);

    public Snapshot? LoadRollback() => TryLoad(RollbackPath);

    public void ClearRollback()
    {
        if (File.Exists(RollbackPath))
        {
            File.Delete(RollbackPath);
        }
    }

    private static Snapshot? TryLoad(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            return JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}

