using System.Text;
using InputFusion.Core.Updates;

namespace InputFusion.Tests;

public class ReleaseCheckTests
{
    [Theory]
    [InlineData("v0.2.0", "0.2.0")]
    [InlineData("1.10.3", "1.10.3")]
    public void ParseTag_ReadsVersion(string tag, string expected)
    {
        Assert.Equal(Version.Parse(expected), ReleaseCheck.ParseTag(tag));
    }

    [Theory]
    [InlineData("nightly")]
    [InlineData("v1")]
    [InlineData("")]
    public void ParseTag_RejectsOtherForms(string tag)
    {
        Assert.Null(ReleaseCheck.ParseTag(tag));
    }

    [Theory]
    [InlineData("0.2.0", "0.1.0", true)]
    [InlineData("0.10.0", "0.9.5", true)]
    [InlineData("0.2.0", "0.2.0", false)]
    [InlineData("0.2.0", "0.2.0.0", false)]
    [InlineData("0.1.9", "0.2.0", false)]
    [InlineData("1.0.0", "kaputt", false)]
    public void IsNewer_ComparesNumerically(string candidate, string current, bool expected)
    {
        Assert.Equal(expected, ReleaseCheck.IsNewer(Version.Parse(candidate), current));
    }

    [Fact]
    public void ParseChecksum_AcceptsBareHashAndSha256sumFormat()
    {
        string hash = new string('a', 64);
        Assert.Equal(hash, ReleaseCheck.ParseChecksum(hash + "\n"));
        Assert.Equal(hash, ReleaseCheck.ParseChecksum(hash.ToUpperInvariant() + "  InputFusion.Client-win-x64.zip\r\n"));
        Assert.Null(ReleaseCheck.ParseChecksum("zu kurz"));
        Assert.Null(ReleaseCheck.ParseChecksum(new string('g', 64)));
    }

    [Fact]
    public void MatchesChecksum_ComparesSha256()
    {
        byte[] data = Encoding.UTF8.GetBytes("abc");
        // SHA-256 von "abc" laut FIPS 180-2.
        const string Expected = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
        Assert.True(ReleaseCheck.MatchesChecksum(data, Expected));
        Assert.True(ReleaseCheck.MatchesChecksum(data, Expected.ToUpperInvariant()));
        Assert.False(ReleaseCheck.MatchesChecksum(Encoding.UTF8.GetBytes("abd"), Expected));
    }

    [Theory]
    [InlineData("https://github.com/NicoMertens/input-fusion", "https://api.github.com/repos/NicoMertens/input-fusion/releases/latest")]
    [InlineData("https://github.com/NicoMertens/input-fusion.git/", "https://api.github.com/repos/NicoMertens/input-fusion/releases/latest")]
    public void LatestReleaseApi_BuildsApiUrl(string repo, string expected)
    {
        Assert.Equal(new Uri(expected), ReleaseCheck.LatestReleaseApi(repo));
    }

    [Theory]
    [InlineData("https://example.com/a/b")]
    [InlineData("https://github.com/nur-owner")]
    [InlineData("kein url")]
    public void LatestReleaseApi_RejectsOthers(string repo)
    {
        Assert.Null(ReleaseCheck.LatestReleaseApi(repo));
    }

    [Fact]
    public void GitHubRelease_ParsesApiResponse()
    {
        const string Json = """
            {"tag_name":"v0.2.0","draft":false,"prerelease":false,"name":"x",
             "assets":[{"name":"InputFusion.Client-win-x64.zip","browser_download_url":"https://github.com/o/r/releases/download/v0.2.0/InputFusion.Client-win-x64.zip","size":1}]}
            """;
        GitHubRelease? release = GitHubRelease.Parse(Json);
        Assert.NotNull(release);
        Assert.Equal("v0.2.0", release.TagName);
        Assert.EndsWith("/InputFusion.Client-win-x64.zip", release.Asset(ReleaseCheck.ClientPackage)!.DownloadUrl);
        Assert.Null(release.Asset(ReleaseCheck.ClientChecksum));
        Assert.Null(GitHubRelease.Parse("kein json"));
    }
}
