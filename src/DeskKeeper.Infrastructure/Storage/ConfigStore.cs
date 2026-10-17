using System.Text.Json;

namespace DeskKeeper.Infrastructure.Storage;

/// <summary>
/// JSON 配置存储：写入前备份为 .bak，新文件落盘成功后再替换；
/// 启动时校验 schema，损坏则回滚 .bak 并提示。
/// </summary>
public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _baseDir;

    public ConfigStore(string baseDir)
    {
        _baseDir = baseDir;
        Directory.CreateDirectory(baseDir);
    }

    public string ConfigPath => Path.Combine(_baseDir, "config.json");

    public AppConfigDto Load()
    {
        var (config, _) = LoadWithSource();
        return config;
    }

    public (AppConfigDto Config, bool FromBackup) LoadWithSource()
    {
        var fromBackup = false;
        var text = TryRead(ConfigPath);
        if (text is null)
        {
            text = TryRead(ConfigPath + ".bak");
            fromBackup = true;
        }

        if (text is not null)
        {
            var parsed = TryDeserialize(text);
            if (parsed is not null && IsValid(parsed))
            {
                return (parsed, fromBackup);
            }

            if (!fromBackup)
            {
                text = TryRead(ConfigPath + ".bak");
                if (text is not null)
                {
                    var backup = TryDeserialize(text);
                    if (backup is not null && IsValid(backup))
                    {
                        return (backup, true);
                    }
                }
            }
        }

        return (CreateDefault(), false);
    }

    public void Save(AppConfigDto config)
    {
        if (!IsValid(config))
        {
            throw new ArgumentException("配置 schema 校验失败，拒绝写入。");
        }

        var json = JsonSerializer.Serialize(config, JsonOptions);
        if (File.Exists(ConfigPath))
        {
            File.Copy(ConfigPath, ConfigPath + ".bak", overwrite: true);
        }

        var temp = ConfigPath + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, ConfigPath, overwrite: true);
    }

    public AppConfigDto CreateDefault() => CreateDefaultConfig();

    public static AppConfigDto CreateDefaultConfig() => new()
    {
        SchemaVersion = 1,
        ActiveProfile = "classroom",
        Profiles = new Dictionary<string, ProfileDto>
        {
            ["classroom"] = new()
            {
                Name = "教室模式",
                Rules = new List<RuleDto>
                {
                    new()
                    {
                        Order = 100,
                        Id = "apps",
                        Match = new RuleMatchDto { Kind = "AppLink" },
                        Action = new RuleActionDto { Zone = "apps", Pin = false },
                    },
                    new()
                    {
                        Order = 200,
                        Id = "folders",
                        Match = new RuleMatchDto { Kind = "Folder" },
                        Action = new RuleActionDto { Zone = "folders" },
                    },
                    new()
                    {
                        Order = 300,
                        Id = "lnk-exclude",
                        Match = new RuleMatchDto { Ext = new List<string> { "*.lnk" } },
                        Action = new RuleActionDto { Exclude = true, Disposal = "skip" },
                    },
                    new()
                    {
                        Order = 400,
                        Id = "schedule-exclude",
                        Match = new RuleMatchDto { NameGlob = "*课表*" },
                        Action = new RuleActionDto { Exclude = true, Disposal = "skip" },
                    },
                    new()
                    {
                        Order = 500,
                        Id = "junk",
                        Match = new RuleMatchDto
                        {
                            Ext = new List<string> { "*.tmp", "*.crdownload" },
                            SizeMaxKB = 1024,
                        },
                        Action = new RuleActionDto { Disposal = "recycle" },
                    },
                    new()
                    {
                        Order = 9000,
                        Id = "default-files",
                        Match = new RuleMatchDto(),
                        Action = new RuleActionDto { Zone = "files" },
                    },
                },
                Layout = new LayoutDto(),
                Cleanup = new CleanupDto(),
            },
        },
    };

    private static AppConfigDto? TryDeserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<AppConfigDto>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? TryRead(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsValid(AppConfigDto config)
    {
        if (config.SchemaVersion != 1
            || config.Profiles.Count == 0
            || string.IsNullOrWhiteSpace(config.ActiveProfile)
            || !config.Profiles.ContainsKey(config.ActiveProfile))
        {
            return false;
        }

        foreach (var (_, profile) in config.Profiles)
        {
            if (profile.Rules.Count == 0
                || profile.Layout.ZoneRatio.Count == 0
                || profile.Layout.ZoneRatio.Sum() <= 0
                || profile.Layout.ZoneRatio.Any(r => r < 0)
                || profile.Cleanup.KeepDays < 0
                || profile.Cleanup.Mode is not ("archive" or "recycle"))
            {
                return false;
            }
        }

        return true;
    }
}

