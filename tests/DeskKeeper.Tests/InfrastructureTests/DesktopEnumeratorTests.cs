using DeskKeeper.Domain.Models;
using DeskKeeper.Infrastructure.Desktop;
using DeskKeeper.Tests.TestDoubles;

namespace DeskKeeper.Tests.InfrastructureTests;

public class DesktopEnumeratorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dk-enum-{Guid.NewGuid():N}");

    public DesktopEnumeratorTests()
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

    [Fact]
    public void Classifies_kinds_and_resolves_link_targets()
    {
        var user = Path.Combine(_root, "user");
        Directory.CreateDirectory(user);
        File.WriteAllText(Path.Combine(user, "课件.pptx"), "x");
        Directory.CreateDirectory(Path.Combine(user, "讲义"));
        var lnk = Path.Combine(user, "微信.lnk");
        File.WriteAllText(lnk, "fake");
        var resolver = new FakeLinkResolver().Add(lnk, @"C:\Program Files\Tencent\WeChat\WeChat.exe");

        var enumerator = new DesktopEnumerator(resolver, new[] { (user, "user") });
        var items = enumerator.Enumerate();

        Assert.Equal(3, items.Count);
        var pptx = items.Single(i => i.Name == "课件.pptx");
        Assert.Equal(DesktopItemKind.File, pptx.Kind);
        Assert.Equal(DesktopItemKind.Folder, items.Single(i => i.Name == "讲义").Kind);
        var link = items.Single(i => i.Name == "微信.lnk");
        Assert.Equal(DesktopItemKind.AppLink, link.Kind);
        Assert.Equal(@"C:\Program Files\Tencent\WeChat\WeChat.exe", link.LinkTargetExe);
    }

    [Fact]
    public void Skips_desktop_ini_and_system_icons()
    {
        var user = Path.Combine(_root, "user");
        Directory.CreateDirectory(user);
        File.WriteAllText(Path.Combine(user, "desktop.ini"), "[.ShellClassInfo]\r\nCLSID={20D04FE0-3AEA-1069-A2D8-08002B30309D}");
        File.WriteAllText(Path.Combine(user, "此电脑.{20D04FE0-3AEA-1069-A2D8-08002B30309D}"), "");
        File.WriteAllText(Path.Combine(user, "正常.txt"), "");

        var enumerator = new DesktopEnumerator(new FakeLinkResolver(), new[] { (user, "user") });
        var items = enumerator.Enumerate();
        Assert.Single(items);
        Assert.Equal("正常.txt", items[0].Name);
    }

    [Fact]
    public void Flags_one_drive_and_public_readonly()
    {
        var onedrive = Path.Combine(_root, "OneDrive");
        var publicRoot = Path.Combine(_root, "public");
        Directory.CreateDirectory(onedrive);
        Directory.CreateDirectory(publicRoot);
        File.WriteAllText(Path.Combine(onedrive, "云文档.docx"), "");
        File.WriteAllText(Path.Combine(publicRoot, "公共.txt"), "");

        var enumerator = new DesktopEnumerator(new FakeLinkResolver(), new[]
        {
            (onedrive, "user"),
            (publicRoot, "public"),
        });
        var items = enumerator.Enumerate();
        Assert.True(items.Single(i => i.Name == "云文档.docx").IsOneDriveManaged);
        Assert.True(items.Single(i => i.Name == "公共.txt").IsReadOnly);
    }
}

