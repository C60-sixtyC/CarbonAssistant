using DeskKeeper.Infrastructure.Shell;
using DeskKeeper.Tests.TestDoubles;

namespace DeskKeeper.Tests.InfrastructureTests;

public class RecycleBinProbeTests
{
    [Fact]
    public void Detects_nuke_on_delete_from_registry()
    {
        const string key = @"Software\Microsoft\Windows\CurrentVersion\Explorer\BitBucket";
        var on = new RecycleBinProbe(new DictionaryRegistryReader().Set(key, "NukeOnDelete", 1));
        var off = new RecycleBinProbe(new DictionaryRegistryReader().Set(key, "NukeOnDelete", 0));
        var missing = new RecycleBinProbe(new DictionaryRegistryReader());

        Assert.True(on.Probe().NukeOnDeleteEnabled);
        Assert.False(off.Probe().NukeOnDeleteEnabled);
        Assert.False(missing.Probe().NukeOnDeleteEnabled);
    }

    [Theory]
    [InlineData(@"\\server\share\desktop", true)]
    [InlineData(@"C:\Users\t\Desktop", false)]
    [InlineData(@"E:\mobile\desktop", false)]
    public void Detects_unc_paths(string path, bool expected)
    {
        Assert.Equal(expected, RecycleBinProbe.IsRemovableOrNetwork(path));
    }
}

