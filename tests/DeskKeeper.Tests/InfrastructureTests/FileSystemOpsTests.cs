using DeskKeeper.Infrastructure.Shell;

namespace DeskKeeper.Tests.InfrastructureTests;

public class FileSystemOpsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"dk-fs-{Guid.NewGuid():N}");

    public FileSystemOpsTests() => Directory.CreateDirectory(_dir);

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

    [Fact]
    public void Move_to_archive_moves_file_and_leaves_no_source()
    {
        var ops = new FileSystemOps();
        var source = Path.Combine(_dir, "a.txt");
        var dest = Path.Combine(_dir, "archive", "a.txt");
        File.WriteAllText(source, "hello");

        ops.MoveToArchive(source, dest);
        Assert.False(File.Exists(source));
        Assert.Equal("hello", File.ReadAllText(dest));
    }

    [Fact]
    public void Move_to_archive_moves_directory()
    {
        var ops = new FileSystemOps();
        var source = Path.Combine(_dir, "folder");
        var dest = Path.Combine(_dir, "archive", "folder");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "inner.txt"), "x");

        ops.MoveToArchive(source, dest);
        Assert.False(Directory.Exists(source));
        Assert.Equal("x", File.ReadAllText(Path.Combine(dest, "inner.txt")));
    }

    [Fact]
    public void Exclusive_open_probe_detects_locked_file()
    {
        var ops = new FileSystemOps();
        var path = Path.Combine(_dir, "locked.txt");
        File.WriteAllText(path, "x");
        using (var handle = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.False(ops.CanOpenExclusive(path));
        }

        Assert.True(ops.CanOpenExclusive(path));
    }

    [Fact]
    public void Sha256_is_stable()
    {
        var ops = new FileSystemOps();
        var path = Path.Combine(_dir, "h.txt");
        File.WriteAllText(path, "abc");
        Assert.Equal(ops.ComputeSha256(path), ops.ComputeSha256(path));
        Assert.Equal(64, ops.ComputeSha256(path).Length);
    }
}

