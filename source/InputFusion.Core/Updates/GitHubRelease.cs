using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NC2.InputFusion.Core.Updates;

/// <summary>Was der Client aus der GitHub-API über das neueste Release braucht.</summary>
public sealed record GitHubRelease(
    [property: JsonPropertyName("tag_name")] String TagName,
    [property: JsonPropertyName("draft")] Boolean Draft,
    [property: JsonPropertyName("prerelease")] Boolean Prerelease,
    [property: JsonPropertyName("assets")] IReadOnlyList<GitHubAsset> Assets)
{
    public static GitHubRelease? Parse(String json)
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

    public GitHubAsset? Asset(String name) => Assets.FirstOrDefault(a => String.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
}

public sealed record GitHubAsset(
    [property: JsonPropertyName("name")] String Name,
    [property: JsonPropertyName("browser_download_url")] String DownloadUrl);

[JsonSerializable(typeof(GitHubRelease))]
internal sealed partial class GitHubJson : JsonSerializerContext;

/// <summary>Reine Hilfsfunktionen für das Selbst-Update des Clients, ohne Netz und Dateisystem.</summary>
public static class ReleaseCheck
{
    /// <summary>Die Asset-Namen, die der Release-Workflow für den Client hochlädt.</summary>
    public const String ClientPackage = "InputFusion.Client-win-x64.zip";
    public const String ClientChecksum = ClientPackage + ".sha256";

    /// <summary>Die Version aus einem Tag der Form <c>vX.Y.Z</c>; null bei anderer Form.</summary>
    public static Version? ParseTag(String tag)
    {
        String text = tag.StartsWith('v') || tag.StartsWith('V') ? tag[1..] : tag;
        return Version.TryParse(text, out Version? version) && version.Build >= 0 ? version : null;
    }

    /// <summary>True, wenn <paramref name="candidate"/> neuer ist als die laufende Version <paramref name="current"/>.</summary>
    public static Boolean IsNewer(Version candidate, String current) =>
        Version.TryParse(current, out Version? running) && Normalize(candidate) > Normalize(running);

    /// <summary>
    /// Der Hash aus einer Prüfsummendatei: entweder nur der Hash oder <c>hash  dateiname</c> wie bei
    /// <c>sha256sum</c>. Null, wenn kein gültiger SHA-256-Hash darin steht.
    /// </summary>
    public static String? ParseChecksum(String content)
    {
        String first = content.Trim().Split((Char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return first.Length == 64 && first.All(Uri.IsHexDigit) ? first.ToLowerInvariant() : null;
    }

    public static Boolean MatchesChecksum(ReadOnlySpan<Byte> data, String expectedHex) =>
        String.Equals(Convert.ToHexStringLower(SHA256.HashData(data)), expectedHex, StringComparison.OrdinalIgnoreCase);

    /// <summary>Die API-Adresse des neuesten Releases zu einer Repo-Adresse wie <c>https://github.com/owner/repo</c>.</summary>
    public static Uri? LatestReleaseApi(String repositoryUrl)
    {
        if (!Uri.TryCreate(repositoryUrl.TrimEnd('/'), UriKind.Absolute, out Uri? uri) || uri.Host != "github.com")
        {
            return null;
        }

        String[] parts = uri.AbsolutePath.Trim('/').Split('/');
        if (parts.Length != 2)
        {
            return null;
        }

        String repo = parts[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? parts[1][..^4] : parts[1];
        return new Uri($"https://api.github.com/repos/{parts[0]}/{repo}/releases/latest");
    }

    // 1.2 und 1.2.0 gelten als gleich; Version vergleicht fehlende Teile sonst als kleiner.
    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
}
