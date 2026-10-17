using DeskKeeper.Application.Mapping;
using DeskKeeper.Application.UseCases;
using DeskKeeper.Domain.Rules;
using DeskKeeper.Infrastructure.Desktop;
using DeskKeeper.Infrastructure.Shell;
using DeskKeeper.Infrastructure.Storage;
using DeskKeeper.Infrastructure.Windows;

namespace DeskKeeper.App.Services;

public static class AppServices
{
    public static RuleProfile LoadProfile()
    {
        var profileId = App.Config.ActiveProfile;
        var dto = App.Config.Profiles[profileId];
        return ProfileFactory.ToDomain(profileId, dto);
    }

    public static (ArrangeUseCase arrange, CleanUseCase clean) BuildUseCases()
    {
        var source = new DesktopEnumerator(new ShellLinkResolver());
        var fileOps = new FileSystemOps();
        var probe = new RecycleBinProbe();
        var comparer = new NaturalStringComparer();

        var arrange = new ArrangeUseCase(
            source,
            new DesktopIconPositioner(),
            new SystemLayoutParametersProvider(),
            App.SnapshotStore,
            App.AuditLogger,
            comparer);
        var clean = new CleanUseCase(
            source,
            fileOps,
            probe,
            App.AuditLogger,
            App.UndoStore);
        return (arrange, clean);
    }
}

