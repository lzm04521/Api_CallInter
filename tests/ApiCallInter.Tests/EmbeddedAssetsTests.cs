namespace ApiCallInter.Tests;

using Microsoft.Extensions.FileProviders;

/// <summary>
/// 单文件发布改造回归防护：wwwroot 静态页嵌入程序集并由 ManifestEmbeddedFileProvider 提供，
/// GenerateEmbeddedFilesManifest 的 targets 随 Microsoft.Extensions.FileProviders.Embedded NuGet 包分发，
/// 若缺失/失效，管理页将整页 404——此处按 Program.BuildWebHost 同参数构造 Provider，直接断言清单与文件可读。
/// </summary>
public class EmbeddedAssetsTests
{
    [Fact]
    public void Wwwroot_AssetsEmbedded_WithManifest()
    {
        var names = typeof(Program).Assembly.GetManifestResourceNames();

        Assert.Contains(names, n => n.EndsWith("wwwroot.index.html", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(names, n => n.EndsWith("wwwroot.vendor.vue.global.prod.js", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(names, n => n.EndsWith("Microsoft.Extensions.FileProviders.Embedded.Manifest.xml", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Wwwroot_ManifestProvider_ServesFiles()
    {
        var provider = new ManifestEmbeddedFileProvider(typeof(Program).Assembly, "wwwroot");

        var index = provider.GetFileInfo("index.html");   // UseDefaultFiles 的默认文档
        Assert.True(index.Exists);
        using (var s = index.CreateReadStream())
            Assert.True(s.Length > 0, "index.html 嵌入流为空");

        var vue = provider.GetFileInfo("vendor/vue.global.prod.js");   // 子目录资源
        Assert.True(vue.Exists);
        using (var s = vue.CreateReadStream())
            Assert.True(s.Length > 0, "vue.global.prod.js 嵌入流为空");
    }
}
