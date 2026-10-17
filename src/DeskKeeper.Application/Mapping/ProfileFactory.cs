using DeskKeeper.Domain.Models;
using DeskKeeper.Domain.Rules;
using DeskKeeper.Infrastructure.Storage;

namespace DeskKeeper.Application.Mapping;

public static class ProfileFactory
{
    public static RuleProfile ToDomain(string profileId, ProfileDto dto)
    {
        var rules = dto.Rules.Select(ToRule).ToList();
        return new RuleProfile(
            Id: profileId,
            Name: string.IsNullOrWhiteSpace(dto.Name) ? profileId : dto.Name,
            Rules: rules,
            Layout: new ProfileLayout(
                ZoneRatio: dto.Layout.ZoneRatio.ToArray(),
                MinSlotsPerZone: Math.Max(1, dto.Layout.MinSlotsPerZone),
                ReserveTitleRow: dto.Layout.ReserveTitleRow),
            Cleanup: new CleanupSettings(
                Mode: dto.Cleanup.Mode,
                KeepDays: dto.Cleanup.KeepDays,
                ArchiveRoot: dto.Cleanup.ArchiveRoot,
                ArchiveCapBytes: (long)(dto.Cleanup.ArchiveCapGB * 1024 * 1024 * 1024),
                JunkExtensions: dto.Cleanup.JunkExtensions.ToArray(),
                JunkMaxKB: dto.Cleanup.JunkMaxKB,
                ProtectedExtensions: dto.Cleanup.ProtectedExtensions.ToArray(),
                TeachingPathContains: dto.Cleanup.TeachingPathContains));
    }

    private static Rule ToRule(RuleDto dto)
    {
        var match = dto.Match;
        var action = dto.Action;
        return new Rule(
            Order: dto.Order,
            Id: dto.Id,
            Match: new RuleMatch(
                Kind: ParseKind(match.Kind),
                Extensions: match.Ext?.ToArray(),
                NameGlob: match.NameGlob,
                NameRegex: match.NameRegex,
                SizeMinKB: match.SizeMinKB,
                SizeMaxKB: match.SizeMaxKB,
                ModifiedOlderThanDays: match.ModifiedOlderThanDays,
                AccessedOlderThanDays: match.AccessedOlderThanDays,
                PathContains: match.PathContains),
            Action: new RuleAction(
                Zone: ParseZone(action.Zone),
                Exclude: action.Exclude,
                Disposal: ParseDisposal(action.Disposal),
                Pin: action.Pin,
                ConfirmRequired: action.ConfirmRequired));
    }

    private static DesktopItemKind? ParseKind(string? kind) => kind?.ToLowerInvariant() switch
    {
        "applink" => DesktopItemKind.AppLink,
        "folder" => DesktopItemKind.Folder,
        "file" => DesktopItemKind.File,
        "systemicon" => DesktopItemKind.SystemIcon,
        "unknown" => DesktopItemKind.Unknown,
        _ => null,
    };

    private static Zone? ParseZone(string? zone) => zone?.ToLowerInvariant() switch
    {
        "apps" => Zone.Apps,
        "folders" => Zone.Folders,
        "files" => Zone.Files,
        "none" => Zone.None,
        _ => null,
    };

    private static Disposal? ParseDisposal(string? disposal) => disposal?.ToLowerInvariant() switch
    {
        "archive" => Disposal.Archive,
        "recycle" => Disposal.Recycle,
        "skip" => Disposal.Skip,
        _ => null,
    };
}

