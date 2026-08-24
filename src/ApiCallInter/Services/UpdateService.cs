using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using ApiCallInter;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ApiCallInter.Services;

public class UpdateOptions { public string Repo { get; set; } = "lzm04521/Api_CallInter"; }
public record UpdateCheckResult(bool HasUpdate, string? LatestVersion, string? CurrentVersion, string? Notes, string? DownloadUrl);

/// <summary>应用内更新：GitHub Release 检查 / zip 下载解压（spec 4.5~4.6），升级替换由 update.ps1 完成。</summary>
public class UpdateService(IHttpClientFactory factory, IOptions<UpdateOptions> options, ILogger<UpdateService> logger)
{
    public async Task<UpdateCheckResult?> CheckAsync()
    {
        var client = factory.CreateClient("github");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ApiCallInter");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        using var resp = await client.GetAsync($"https://api.github.com/repos/{options.Value.Repo}/releases/latest");
        resp.EnsureSuccessStatusCode();   // 网络失败/无 release(404) 抛异常 → 端点 500，前端显示"检查失败/暂不可用"
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var current = "v" + Assembly.GetEntryAssembly()!
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
        var result = ParseLatest(doc.RootElement, current);
        if (result.HasUpdate && result.DownloadUrl is null) throw new ValidationException("Release 缺少 zip 资产");
        return result;
    }

    internal static UpdateCheckResult ParseLatest(JsonElement root, string currentVersion)
    {
        var tag = root.GetProperty("tag_name").GetString()!;
        var notes = root.TryGetProperty("body", out var b) ? b.GetString() : "";
        // 传统 foreach 扫资产：assets 为空数组或无匹配项时不抛异常，url 为 null
        string? url = null;
        if (root.TryGetProperty("assets", out var assets))
            foreach (var a in assets.EnumerateArray())
                if (a.TryGetProperty("name", out var n) && n.GetString() == "ApiCallInter-win-x64.zip" &&
                    a.TryGetProperty("browser_download_url", out var u))
                {
                    url = u.GetString();
                    break;
                }
        return new UpdateCheckResult(CompareVersions(tag, currentVersion) > 0, tag, currentVersion, notes, url);
    }

    public static int CompareVersions(string? a, string? b)
    {
        var va = Version.Parse(a!.TrimStart('v'));
        var vb = Version.Parse(b!.TrimStart('v'));
        return va.CompareTo(vb);
    }

    public async Task<string> PrepareAsync(UpdateCheckResult check)
    {
        var version = check.LatestVersion!.TrimStart('v');
        var dir = Path.Combine(AppPaths.UpdatesDir, version);
        if (Directory.Exists(dir)) return dir;
        Directory.CreateDirectory(AppPaths.UpdatesDir);   // 启动时 CleanupStaleUpdates 可能已整目录删除
        var zipPath = Path.Combine(AppPaths.UpdatesDir, $"{version}.zip");
        var client = factory.CreateClient("github");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ApiCallInter");
        await using (var s = await client.GetStreamAsync(check.DownloadUrl))
            await using (var f = File.Create(zipPath)) await s.CopyToAsync(f);
        ZipFile.ExtractToDirectory(zipPath, dir, overwriteFiles: true);
        logger.LogInformation("更新包就绪：{Dir}", dir);
        return dir;
    }

    public static void CleanupStaleUpdates()
    {
        try { if (Directory.Exists(AppPaths.UpdatesDir)) Directory.Delete(AppPaths.UpdatesDir, recursive: true); }
        catch (Exception) { /* 下次再清 */ }
    }
}
