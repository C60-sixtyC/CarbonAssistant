using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DeskKeeper.Domain.Rules;

namespace DeskKeeper.Application.UseCases;

public static class ConfigHash
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string Compute(RuleProfile profile)
    {
        var json = JsonSerializer.Serialize(profile, Options);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

