using ApiCallInter.Services;

namespace ApiCallInter.Tests;

public class AppRestarterTests
{
    /// <summary>
    /// 审查 Critical 回归防护：AppContext.BaseDirectory 以 \ 结尾，手工拼命令行时
    /// 结尾 `\"` 被 CommandLineToArgvW 当转义引号 → -AppDir 与 -LogPath 粘连成坏 token。
    /// ArgumentList + 去尾反斜杠后，任何参数值不得以 \ 结尾。
    /// </summary>
    [Fact]
    public void BuildUpdaterStartInfo_ArgsHave_NoTrailingBackslash()
    {
        var psi = AppRestarter.BuildUpdaterStartInfo(@"D:\fake src dir\updates\9.9.9");
        Assert.Equal("powershell", psi.FileName);
        var args = psi.ArgumentList.ToList();

        Assert.DoesNotContain(args, a => a.EndsWith('\\'));

        var appDir = args[args.IndexOf("-AppDir") + 1];
        Assert.Equal(AppContext.BaseDirectory.TrimEnd('\\'), appDir);   // 不丢目录、只去尾 \

        var script = args[args.IndexOf("-File") + 1];
        Assert.Equal(Path.Combine(appDir, "update.ps1"), script);
        Assert.True(File.Exists(script), "update.ps1 未随构建复制到输出目录，升级脚本将找不到");

        Assert.Equal(Environment.ProcessId.ToString(), args[args.IndexOf("-OldPid") + 1]);
        Assert.Equal(@"D:\fake src dir\updates\9.9.9", args[args.IndexOf("-SrcDir") + 1]);   // 含空格路径原样传递
        Assert.EndsWith("update.log", args[args.IndexOf("-LogPath") + 1]);
    }
}
