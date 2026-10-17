using DeskKeeper.Infrastructure.Storage;

namespace DeskKeeper.Tests.InfrastructureTests;

public class ConfigStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"dk-config-{Guid.NewGuid():N}");

    public ConfigStoreTests() => Directory.CreateDirectory(_dir);

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
    public void Save_and_load_round_trip()
    {
        var store = new ConfigStore(_dir);
        var config = store.CreateDefault();
        config.ActiveProfile = "classroom";
        config.Profiles["classroom"].Cleanup.KeepDays = 14;
        store.Save(config);
        store.Save(config);

        var loaded = store.Load();
        Assert.Equal(14, loaded.Profiles["classroom"].Cleanup.KeepDays);
        Assert.True(File.Exists(store.ConfigPath + ".bak"), "保存后应存在 .bak");
    }

    [Fact]
    public void Corrupted_file_falls_back_to_bak()
    {
        var store = new ConfigStore(_dir);
        var config = store.CreateDefault();
        config.Profiles["classroom"].Cleanup.KeepDays = 21;
        store.Save(config);
        store.Save(config);

        File.WriteAllText(store.ConfigPath, "{not-json");
        var (loaded, fromBackup) = store.LoadWithSource();
        Assert.True(fromBackup);
        Assert.Equal(21, loaded.Profiles["classroom"].Cleanup.KeepDays);
    }

    [Fact]
    public void Invalid_schema_is_rejected_and_default_returned()
    {
        var store = new ConfigStore(_dir);
        var bad = store.CreateDefault();
        bad.SchemaVersion = 99;
        Assert.Throws<ArgumentException>(() => store.Save(bad));

        File.WriteAllText(store.ConfigPath, """{"schemaVersion":99,"activeProfile":"x","profiles":{}}""");
        var loaded = store.Load();
        Assert.Equal(1, loaded.SchemaVersion);
        Assert.True(loaded.Profiles.ContainsKey("classroom"));
    }

    [Fact]
    public void Default_config_is_complete()
    {
        var config = ConfigStore.CreateDefaultConfig();
        var classroom = config.Profiles["classroom"];
        Assert.Equal(6, classroom.Rules.Count);
        Assert.Equal(new[] { 2d, 2.5, 5.5 }, classroom.Layout.ZoneRatio);
        Assert.Equal("archive", classroom.Cleanup.Mode);
        Assert.Contains("*.tmp", classroom.Cleanup.JunkExtensions);
    }
}

