using System.Text.Json;
using DeskKeeper.Domain.Contracts;

namespace DeskKeeper.Infrastructure.Audit;

public sealed record AuditRecord(
    DateTimeOffset Ts,
    string Host,
    string? UserSid,
    string Profile,
    string Action,
    bool DryRun,
    int ItemsAffected,
    long BytesArchived,
    IReadOnlyList<AuditFailure> Failures,
    string? ConfigHash,
    string? ManifestHash);

public sealed record AuditFailure(string Path, string Reason);

/// <summary>
/// 审计日志：仅存元数据（JSON Lines），本地轮转（50MB）与 90 天截断。
/// </summary>
public sealed class AuditLogger
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _dir;
    private readonly long _maxBytes;
    private readonly int _retainDays;
    private readonly IClock _clock;

    public AuditLogger(string directory, long maxBytes = 50L * 1024 * 1024, int retainDays = 90, IClock? clock = null)
    {
        _dir = directory;
        _maxBytes = maxBytes;
        _retainDays = retainDays;
        _clock = clock ?? new SystemClock();
        Directory.CreateDirectory(directory);
    }

    public string LogFilePath => Path.Combine(_dir, "audit.jsonl");

    public void Append(AuditRecord record)
    {
        RotateIfNeeded();
        var line = JsonSerializer.Serialize(record, JsonOptions);
        File.AppendAllText(LogFilePath, line + Environment.NewLine);
    }

    public void RotateIfNeeded()
    {
        if (File.Exists(LogFilePath) && new FileInfo(LogFilePath).Length >= _maxBytes)
        {
            var rotated = Path.Combine(
                _dir,
                $"audit-{_clock.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..8]}.jsonl");
            File.Move(LogFilePath, rotated);
        }

        var cutoff = _clock.Now.AddDays(-_retainDays);
        foreach (var file in Directory.EnumerateFiles(_dir, "audit-*.jsonl"))
        {
            if (File.GetLastWriteTime(file) < cutoff)
            {
                File.Delete(file);
            }
        }
    }
}

