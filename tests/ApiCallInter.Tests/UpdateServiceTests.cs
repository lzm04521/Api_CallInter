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

    [Fact]
    public async Task PrepareAsync_CorruptZip_CleansResidue_AndRetrySucceeds()
    {
        // APICALLINTER_DATA_DIR 隔离 UpdatesDir；测试结束恢复原值（AppPaths 为动态读取）
        var root = Path.Combine(Path.GetTempPath(), "task10-prepare-" + Guid.NewGuid().ToString("N"));
        var old = Environment.GetEnvironmentVariable("APICALLINTER_DATA_DIR");
        Environment.SetEnvironmentVariable("APICALLINTER_DATA_DIR", root);
        try
        {
            var calls = 0;
            var factory = new SingleHttpClientFactory(new StubHttpMessageHandler((_, _) =>
            {
                calls++;
                var bytes = calls == 1
                    ? System.Text.Encoding.UTF8.GetBytes("PK\x0003\x0004truncated-not-a-zip")   // 截断假 zip：下载成功但解压必失败
                    : MakeZip();                                                               // 重试给真 zip
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                { Content = new ByteArrayContent(bytes) });
            }));
            var svc = new UpdateService(factory, Microsoft.Extensions.Options.Options.Create(new UpdateOptions()),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<UpdateService>.Instance);
            var check = new UpdateCheckResult(true, "v9.8.7", "v1.0.0", null, "http://localhost/fake.zip");

            var zipPath = Path.Combine(root, "updates", "9.8.7.zip");
            var dir = Path.Combine(root, "updates", "9.8.7");

            // 第一次：解压失败 → 异常上抛，半截 zip 与解压目录必须清掉（spec 4.6）
            await Assert.ThrowsAsync<System.IO.InvalidDataException>(() => svc.PrepareAsync(check));
            Assert.False(File.Exists(zipPath), "失败后残留 zip 未清理");
            Assert.False(Directory.Exists(dir), "失败后残留解压目录未清理（会让下次早退误判已就绪）");

            // 第二次：早退未被投毒 → 重新下载解压成功
            var got = await svc.PrepareAsync(check);
            Assert.Equal(dir, got);
            Assert.True(File.Exists(Path.Combine(dir, "ApiCallInter.exe")), "真 zip 未解出文件");
        }
        finally
        {
            Environment.SetEnvironmentVariable("APICALLINTER_DATA_DIR", old);
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static byte[] MakeZip()
    {
        using var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            var e = zip.CreateEntry("ApiCallInter.exe");
            using var w = new StreamWriter(e.Open());
            w.Write("NEW-EXE-BYTES");
        }
        return ms.ToArray();
    }
}
