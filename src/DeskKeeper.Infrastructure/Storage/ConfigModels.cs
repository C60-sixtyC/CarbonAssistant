namespace DeskKeeper.Infrastructure.Storage;

public sealed class AppConfigDto
{
    public int SchemaVersion { get; set; } = 1;
    public string ActiveProfile { get; set; } = "classroom";
    public string? AccentColor { get; set; }
    public Dictionary<string, ProfileDto> Profiles { get; set; } = new();
}

public sealed class ProfileDto
{
    public string Name { get; set; } = "";
    public List<RuleDto> Rules { get; set; } = new();
    public LayoutDto Layout { get; set; } = new();
    public CleanupDto Cleanup { get; set; } = new();
}

public sealed class RuleDto
{
    public int Order { get; set; }
    public string? Id { get; set; }
    public RuleMatchDto Match { get; set; } = new();
    public RuleActionDto Action { get; set; } = new();
}

public sealed class RuleMatchDto
{
    public string? Kind { get; set; }
    public List<string>? Ext { get; set; }
    public string? NameGlob { get; set; }
    public string? NameRegex { get; set; }
    public double? SizeMinKB { get; set; }
    public double? SizeMaxKB { get; set; }
    public double? ModifiedOlderThanDays { get; set; }
    public double? AccessedOlderThanDays { get; set; }
    public string? PathContains { get; set; }
}

public sealed class RuleActionDto
{
    public string? Zone { get; set; }
    public bool Exclude { get; set; }
    public string? Disposal { get; set; }
    public bool? Pin { get; set; }
    public bool ConfirmRequired { get; set; }
}

public sealed class LayoutDto
{
    public List<double> ZoneRatio { get; set; } = new() { 2, 2.5, 5.5 };
    public int MinSlotsPerZone { get; set; } = 4;
    public bool ReserveTitleRow { get; set; } = true;
    public bool PinThisPcTopLeft { get; set; } = true;
    public bool PinRecycleBinTopLeftSecond { get; set; } = true;
}

public sealed class CleanupDto
{
    public string Mode { get; set; } = "archive";
    public int KeepDays { get; set; } = 30;
    public string ArchiveRoot { get; set; } = @"D:\讲台归档";
    public double ArchiveCapGB { get; set; } = 5;
    public List<string> JunkExtensions { get; set; } = new() { "*.tmp", "*.log", "*.crdownload", "*.wbk" };
    public double JunkMaxKB { get; set; } = 1024;
    public List<string> ProtectedExtensions { get; set; } = new() { ".docx", ".pptx", ".xlsx", ".pdf", ".psd" };
    public string TeachingPathContains { get; set; } = "课件";
}

