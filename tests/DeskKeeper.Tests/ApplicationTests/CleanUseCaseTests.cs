using DeskKeeper.Application.Mapping;
using DeskKeeper.Application.UseCases;
using DeskKeeper.Infrastructure.Audit;
using DeskKeeper.Infrastructure.Desktop;
using DeskKeeper.Infrastructure.Shell;
using DeskKeeper.Infrastructure.Storage;
using DeskKeeper.Tests.TestDoubles;

namespace DeskKeeper.Tests.ApplicationTests;

public class CleanUseCaseTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dk-clean-{Guid.NewGuid():N}");

    public CleanUseCaseTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch
        {
        }
    }

    private CleanUseCase BuildUseCase(string scope, string stateDir, string archiveRoot)
    {
        var profile = ProfileFactory.ToDomain(
            "classroom",
            ConfigStore.CreateDefaultConfig().Profiles["classroom"]);
        return new CleanUseCase(
            new DesktopEnumerator(new FakeLinkResolver(), new[] { (scope, "user") }),
            new FileSystemOps(),
            new RecycleBinProbe(new DictionaryRegistryReader()),
            new AuditLogger(Path.Combine(stateDir, "logs"), clock: new FixedClock(DateTimeOffset.Now)),
            new UndoStore(stateDir),
            clock: new FixedClock(DateTimeOffset.Now));
    }

    private static RuleProfile ProfileWithArchive(string archiveRoot)
    {
        var profile = ProfileFactory.ToDomain(
            "classroom",
            ConfigStore.CreateDefaultConfig().Profiles["classroom"]);
        return profile with { Cleanup = profile.Cleanup with { ArchiveRoot = archiveRoot } };
    }

    [Fact]
    public void Manifest_marks_junk_and_stale_but_not_protected_or_fresh()
    {
        var scope = Path.Combine(_root, "课件桌面");
        Directory.CreateDirectory(scope);
        var old = DateTime.UtcNow.AddDays(-40);
        var pptx = Path.Combine(scope, "公开课.pptx");
        File.WriteAllText(pptx, "x");
        File.SetLastWriteTimeUtc(pptx, old);
        var notes = Path.Combine(scope, "笔记.txt");
        File.WriteAllText(notes, "x");
        File.SetLastWriteTimeUtc(notes, old);
        var tmp = Path.Combine(scope, "下载.tmp");
        File.WriteAllText(tmp, new string('x', 512));
        File.SetLastWriteTimeUtc(tmp, old);
        var fresh = Path.Combine(scope, "新.txt");
        File.WriteAllText(fresh, "x");
        File.SetLastWriteTimeUtc(fresh, DateTime.UtcNow.AddDays(-1));
        File.WriteAllText(Path.Combine(scope, "课表.xlsx"), "x");

        var useCase = BuildUseCase(scope, Path.Combine(_root, "s1"), Path.Combine(_root, "a1"));
        var profile = ProfileWithArchive(Path.Combine(_root, "a1"));
        var manifest = useCase.BuildManifest(profile);

        Assert.Equal(1, manifest.ExcludedCount);
        Assert.True(manifest.Items.Single(i => i.FullPath == notes).Checked);
        Assert.True(manifest.Items.Single(i => i.FullPath == tmp).Checked);
        Assert.False(manifest.Items.Single(i => i.FullPath == pptx).Checked);
        Assert.False(manifest.Items.Single(i => i.FullPath == fresh).Checked);
    }

    [Fact]
    public void Dry_run_changes_nothing()
    {
        var scope = Path.Combine(_root, "desktop");
        Directory.CreateDirectory(scope);
        var old = Path.Combine(scope, "old.txt");
        File.WriteAllText(old, "x");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-40));

        var useCase = BuildUseCase(scope, Path.Combine(_root, "s2"), Path.Combine(_root, "a2"));
        var profile = ProfileWithArchive(Path.Combine(_root, "a2"));
        var manifest = useCase.BuildManifest(profile);
        var result = useCase.Execute(manifest, profile, dryRun: true);

        Assert.True(result.DryRun);
        Assert.True(File.Exists(old), "空跑不得移动任何文件");
    }

    [Fact]
    public void Archive_execute_and_restore_round_trip()
    {
        var scope = Path.Combine(_root, "desktop");
        Directory.CreateDirectory(scope);
        var archive = Path.Combine(_root, "archive");
        var old = Path.Combine(scope, "old.txt");
        File.WriteAllText(old, "hello");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-40));

        var useCase = BuildUseCase(scope, Path.Combine(_root, "s3"), archive);
        var profile = ProfileWithArchive(archive);
        var manifest = useCase.BuildManifest(profile);
        var result = useCase.Execute(manifest, profile, dryRun: false);

        Assert.Equal(1, result.Cleaned);
        Assert.NotNull(result.BatchDirectory);
        Assert.False(File.Exists(old));
        Assert.Equal("hello", File.ReadAllText(Path.Combine(result.BatchDirectory!, "old.txt")));

        var restored = useCase.RestoreLastClean();
        Assert.Equal(1, restored.Cleaned);
        Assert.Equal("hello", File.ReadAllText(old));
    }

    [Fact]
    public void Locked_files_are_skipped_but_others_proceed()
    {
        var scope = Path.Combine(_root, "desktop");
        Directory.CreateDirectory(scope);
        var locked = Path.Combine(scope, "locked.txt");
        var normal = Path.Combine(scope, "normal.txt");
        File.WriteAllText(locked, "x");
        File.WriteAllText(normal, "y");
        var oldTime = DateTime.UtcNow.AddDays(-40);
        File.SetLastWriteTimeUtc(locked, oldTime);
        File.SetLastWriteTimeUtc(normal, oldTime);

        var useCase = BuildUseCase(scope, Path.Combine(_root, "s4"), Path.Combine(_root, "a4"));
        var profile = ProfileWithArchive(Path.Combine(_root, "a4"));
        var manifest = useCase.BuildManifest(profile);

        using var handle = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None);
        manifest = useCase.MarkLocked(manifest);
        Assert.True(manifest.Items.Single(i => i.FullPath == locked).Locked);

        var result = useCase.Execute(manifest, profile, dryRun: false);
        Assert.Equal(1, result.Cleaned);
        Assert.True(File.Exists(locked), "占用中的文件绝不能动");
        Assert.False(File.Exists(normal));
    }

    [Fact]
    public void OneDrive_scope_blocks_execution()
    {
        var scope = Path.Combine(_root, "OneDrive");
        Directory.CreateDirectory(scope);
        File.WriteAllText(Path.Combine(scope, "云.docx"), "x");

        var useCase = BuildUseCase(scope, Path.Combine(_root, "s5"), Path.Combine(_root, "a5"));
        var profile = ProfileWithArchive(Path.Combine(_root, "a5"));
        var manifest = useCase.BuildManifest(profile);
        Assert.True(manifest.OneDriveBlocked);

        var result = useCase.Execute(manifest, profile, dryRun: false);
        Assert.True(result.Aborted);
        Assert.True(File.Exists(Path.Combine(scope, "云.docx")));
    }

    [Fact]
    public void Manifest_hash_is_stable_for_same_input()
    {
        var scope = Path.Combine(_root, "desktop");
        Directory.CreateDirectory(scope);
        File.WriteAllText(Path.Combine(scope, "a.txt"), "x");

        var useCase = BuildUseCase(scope, Path.Combine(_root, "s6"), Path.Combine(_root, "a6"));
        var profile = ProfileWithArchive(Path.Combine(_root, "a6"));
        var first = useCase.BuildManifest(profile);
        var second = useCase.BuildManifest(profile);
        Assert.Equal(first.ManifestHash, second.ManifestHash);
    }
}

