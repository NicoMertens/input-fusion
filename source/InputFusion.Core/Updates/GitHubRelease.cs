using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace InputFusion.Core.Updates;

/// <summary>Was der Client aus der GitHub-API über das neueste Release braucht.</summary>
public sealed record GitHubRelease(
    [property: JsonPropertyName("tag_name")] string TagName,
    [property: JsonPropertyName("draft")] bool Draft,
    [property: JsonPropertyName("prerelease")] bool Prerelease,
    [property: JsonPropertyName("assets")] IReadOnlyList<GitHubAsset> Assets)
{
    public static GitHubRelease? Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, GitHubJson.Default.GitHubRelease);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public GitHubAsset? Asset(string name) => Assets.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
}

public sealed record GitHubAsset(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("browser_download_url")] string DownloadUrl);

[JsonSerializable(typeof(GitHubRelease))]
internal sealed partial class GitHubJson : JsonSerializerContext;

/// <summary>Reine Hilfsfunktionen für das Selbst-Update des Clients, ohne Netz und Dateisystem.</summary>
public static class ReleaseCheck
{
    /// <summary>Die Asset-Namen, die der Release-Workflow für den Client hochlädt.</summary>
    public const string ClientPackage = "InputFusion.Client-win-x64.zip";
    public const string ClientChecksum = ClientPackage + ".sha256";

    /// <summary>Die Version aus einem Tag der Form <c>vX.Y.Z</c>; null bei anderer Form.</summary>
    public static Version? ParseTag(string tag)
    {
        string text = tag.StartsWith('v') || tag.StartsWith('V') ? tag[1..] : tag;
        return Version.TryParse(text, out Version? version) && version.Build >= 0 ? version : null;
    }

    /// <summary>True, wenn <paramref name="candidate"/> neuer ist als die laufende Version <paramref name="current"/>.</summary>
    public static bool IsNewer(Version candidate, string current) =>
        Version.TryParse(current, out Version? running) && Normalize(candidate) > Normalize(running);

    /// <summary>
    /// Der Hash aus einer Prüfsummendatei: entweder nur der Hash oder <c>hash  dateiname</c> wie bei
    /// <c>sha256sum</c>. Null, wenn kein gültiger SHA-256-Hash darin steht.
    /// </summary>
    public static string? ParseChecksum(string content)
    {
        string first = content.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return first.Length == 64 && first.All(Uri.IsHexDigit) ? first.ToLowerInvariant() : null;
    }

    public static bool MatchesChecksum(ReadOnlySpan<byte> data, string expectedHex) =>
        string.Equals(Convert.ToHexStringLower(SHA256.HashData(data)), expectedHex, StringComparison.OrdinalIgnoreCase);

    /// <summary>Die API-Adresse des neuesten Releases zu einer Repo-Adresse wie <c>https://github.com/owner/repo</c>.</summary>
    public static Uri? LatestReleaseApi(string repositoryUrl)
    {
        if (!Uri.TryCreate(repositoryUrl.TrimEnd('/'), UriKind.Absolute, out Uri? uri) || uri.Host != "github.com")
        {
            return null;
        }

        string[] parts = uri.AbsolutePath.Trim('/').Split('/');
        if (parts.Length != 2)
        {
            return null;
        }

        string repo = parts[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? parts[1][..^4] : parts[1];
        return new Uri($"https://api.github.com/repos/{parts[0]}/{repo}/releases/latest");
    }

    // 1.2 und 1.2.0 gelten als gleich; Version vergleicht fehlende Teile sonst als kleiner.
    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
}
