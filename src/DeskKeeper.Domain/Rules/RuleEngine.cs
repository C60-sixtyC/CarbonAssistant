using System.Text.RegularExpressions;
using DeskKeeper.Domain.Models;

namespace DeskKeeper.Domain.Rules;

/// <summary>
/// 优先级队列规则引擎：从上到下首次匹配胜出。
/// 纯逻辑，无 UI、无磁盘副作用，可直接单元测试。
/// </summary>
public sealed class RuleEngine
{
    private readonly List<Rule> _rules;

    public RuleEngine(IEnumerable<Rule> rules)
    {
        _rules = rules.OrderBy(r => r.Order).ToList();
    }

    public RuleDecision Evaluate(DesktopItem item, DateTimeOffset now)
    {
        foreach (var rule in _rules)
        {
            if (Matches(rule.Match, item, now))
            {
                return ToDecision(rule, item);
            }
        }

        return RuleDecision.Default;
    }

    private static bool Matches(RuleMatch match, DesktopItem item, DateTimeOffset now)
    {
        if (match.Kind is { } kind && item.Kind != kind)
        {
            return false;
        }

        if (match.Extensions is { Length: > 0 } exts && !Glob.MatchesAnyExtension(item.Name, exts))
        {
            return false;
        }

        if (match.NameGlob is { Length: > 0 } glob && !Glob.IsMatch(item.Name, glob))
        {
            return false;
        }

        if (match.NameRegex is { Length: > 0 } pattern)
        {
            try
            {
                if (!Regex.IsMatch(item.Name, pattern, RegexOptions.IgnoreCase))
                {
                    return false;
                }
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        var sizeKB = item.SizeBytes / 1024.0;
        if (match.SizeMinKB is { } min && sizeKB < min)
        {
            return false;
        }

        if (match.SizeMaxKB is { } max && sizeKB > max)
        {
            return false;
        }

        if (match.ModifiedOlderThanDays is { } modDays
            && now - item.ModifiedTime <= TimeSpan.FromDays(modDays))
        {
            return false;
        }

        if (match.AccessedOlderThanDays is { } accDays)
        {
            if (!item.AccessedTimeReliable || item.AccessedTime is not { } at
                || now - at <= TimeSpan.FromDays(accDays))
            {
                return false;
            }
        }

        if (match.PathContains is { Length: > 0 } fragment
            && !item.FullPath.Contains(fragment, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static RuleDecision ToDecision(Rule rule, DesktopItem item)
    {
        var action = rule.Action;
        return new RuleDecision(
            Zone: action.Zone ?? Zone.Files,
            Exclude: action.Exclude,
            Disposal: action.Disposal ?? Disposal.Archive,
            Pin: action.Pin ?? item.IsPinned,
            ConfirmRequired: action.ConfirmRequired,
            RuleOrder: rule.Order,
            RuleId: rule.Id);
    }
}

