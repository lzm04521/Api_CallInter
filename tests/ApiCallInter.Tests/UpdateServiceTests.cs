using ApiCallInter.Services;

namespace ApiCallInter.Tests;

public class UpdateServiceTests
{
    [Theory]
    [InlineData("v1.0.0", "v1.0.0", 0)]
    [InlineData("v1.0.0", "v1.1.0", -1)]
    [InlineData("v2.0.0", "v1.9.9", 1)]
    [InlineData("1.0.0", "v1.0.0", 0)]     // 前缀差异不影响
    public void CompareVersions_Works(string a, string b, int expectedSign)
    {
        var c = UpdateService.CompareVersions(a, b);
        Assert.Equal(expectedSign, Math.Sign(c));
    }

    [Fact]
    public void ParseRelease_Extracts_Fields()
    {
        var json = """
        { "tag_name": "v1.2.0", "body": "修复若干问题",
          "assets": [ { "name": "ApiCallInter-win-x64.zip", "browser_download_url": "https://example.com/dl.zip" } ] }
        """;
        var r = UpdateService.ParseLatest(System.Text.Json.JsonDocument.Parse(json).RootElement, "v1.1.0");
        Assert.True(r.HasUpdate);
        Assert.Equal("v1.2.0", r.LatestVersion);
        Assert.Equal("修复若干问题", r.Notes);
        Assert.Equal("https://example.com/dl.zip", r.DownloadUrl);
    }

    [Fact]
    public void ParseRelease_SameVersion_NoUpdate()
    {
        var json = """{ "tag_name": "v1.1.0", "body": "", "assets": [] }""";
        var r = UpdateService.ParseLatest(System.Text.Json.JsonDocument.Parse(json).RootElement, "v1.1.0");
        Assert.False(r.HasUpdate);
    }
}
