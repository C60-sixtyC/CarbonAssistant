using System.Text.Json;

namespace DeskKeeper.Infrastructure.Audit;

public sealed record UndoEntry(
    string OriginalPath,
    string? MovedToPath,
    string Disposal,
    long SizeBytes,
    DateTimeOffset At);

/// <summary>
/// 撤销映射：记录 原路径 ↔ 归档路径/删除时间 ↔ 大小，
/// 归档项可反向 Move 还原；回收站项提示用户从回收站恢复。
/// </summary>
public sealed class UndoStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _dir;

    public UndoStore(string directory)
    {
        _dir = directory;
        Directory.CreateDirectory(directory);
    }

    public string UndoPath => Path.Combine(_dir, "last-clean.json");

    public void Save(IReadOnlyList<UndoEntry> entries)
    {
        var temp = UndoPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(entries, JsonOptions));
        File.Move(temp, UndoPath, overwrite: true);
    }

    public IReadOnlyList<UndoEntry> Load()
    {
        try
        {
            return File.Exists(UndoPath)
                ? JsonSerializer.Deserialize<List<UndoEntry>>(File.ReadAllText(UndoPath), JsonOptions) ?? new List<UndoEntry>()
                : new List<UndoEntry>();
        }
        catch (JsonException)
        {
            return new List<UndoEntry>();
        }
    }
}

