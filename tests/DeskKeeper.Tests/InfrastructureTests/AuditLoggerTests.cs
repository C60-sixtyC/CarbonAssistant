using System.Text.Json;
using DeskKeeper.Infrastructure.Audit;
using DeskKeeper.Tests.TestDoubles;

namespace DeskKeeper.Tests.InfrastructureTests;

public class AuditLoggerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"dk-audit-{Guid.NewGuid():N}");

    public AuditLoggerTests() => Directory.CreateDirectory(_dir);

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

    private static AuditRecord MakeRecord(string hash = "abc") => new(
        DateTimeOffset.Now,
        Environment.MachineName,
        "S-1-5-21-test",
        "classroom",
        "cleanup",
        false,
        3,
        1024,
        Array.Empty<AuditFailure>(),
        "config-hash",
        hash);

    [Fact]
    public void Append_writes_json_line_with_metadata_only()
    {
        var logger = new AuditLogger(_dir, clock: new FixedClock(DateTimeOffset.Now));
        logger.Append(MakeRecord());

        var line = File.ReadLines(logger.LogFilePath).Single();
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;
        Assert.Equal("cleanup", root.GetProperty("action").GetString());
        Assert.Equal(3, root.GetProperty("itemsAffected").GetInt32());
        Assert.False(root.TryGetProperty("fileContent", out _), "审计日志绝不能包含文件内容");
    }

    [Fact]
    public void Rotates_when_size_limit_exceeded_and_prunes_expired()
    {
        var logger = new AuditLogger(_dir, maxBytes: 200, clock: new FixedClock(DateTimeOffset.Now));
        logger.Append(MakeRecord(new string('x', 400)));
        logger.Append(MakeRecord(new string('y', 400)));
        logger.Append(MakeRecord(new string('z', 400)));
        Assert.True(Directory.EnumerateFiles(_dir, "audit-*.jsonl").Any(), "超限后应轮转出历史文件");

        var expired = Path.Combine(_dir, "audit-20200101-000000.jsonl");
        File.WriteAllText(expired, "{}");
        File.SetLastWriteTime(expired, DateTime.Now.AddDays(-120));
        logger.RotateIfNeeded();
        Assert.False(File.Exists(expired), "超过 90 天的日志应被截断");
    }
}

