using DeskKeeper.Domain.Models;

namespace DeskKeeper.Domain.Rules;

public enum Zone
{
    None = 0,
    Apps = 1,
    Folders = 2,
    Files = 3,
}

public enum Disposal
{
    Archive,
    Recycle,
    Skip,
}

public sealed record RuleMatch(
    DesktopItemKind? Kind = null,
    string[]? Extensions = null,
    string? NameGlob = null,
    string? NameRegex = null,
    double? SizeMinKB = null,
    double? SizeMaxKB = null,
    double? ModifiedOlderThanDays = null,
    double? AccessedOlderThanDays = null,
    string? PathContains = null);

public sealed record RuleAction(
    Zone? Zone = null,
    bool Exclude = false,
    Disposal? Disposal = null,
    bool? Pin = null,
    bool ConfirmRequired = false);

public sealed record Rule(int Order, string? Id, RuleMatch Match, RuleAction Action);

public sealed record RuleProfile(
    string Id,
    string Name,
    IReadOnlyList<Rule> Rules,
    ProfileLayout Layout,
    CleanupSettings Cleanup);

public sealed record ProfileLayout(
    double[] ZoneRatio,
    int MinSlotsPerZone,
    bool ReserveTitleRow = true,
    bool PinThisPcTopLeft = false,
    bool PinRecycleBinTopLeftSecond = false);

public sealed record CleanupSettings(
    string Mode,
    int KeepDays,
    string ArchiveRoot,
    long ArchiveCapBytes,
    string[] JunkExtensions,
    double JunkMaxKB,
    string[] ProtectedExtensions,
    string TeachingPathContains);

public sealed record RuleDecision(
    Zone Zone,
    bool Exclude,
    Disposal Disposal,
    bool Pin,
    bool ConfirmRequired,
    int RuleOrder,
    string? RuleId)
{
    public static RuleDecision Default = new(
        Zone.Files, false, Disposal.Archive, false, false, int.MaxValue, null);
}

