using System.Diagnostics;
using ApiCallInter.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ApiCallInter.Tray;

/// <summary>托盘上下文：菜单"打开管理页/开机自启(Checked)/退出"，双击托盘打开管理页（spec 4.2/4.3）。</summary>
public class TrayApplicationContext : ApplicationContext
{
    private readonly IServiceProvider _services;
    private readonly Func<int> _port;
    private readonly Action _requestShutdown;
    private readonly NotifyIcon _icon;

    public TrayApplicationContext(IServiceProvider services, Func<int> port, Action requestShutdown)
    {
        _services = services;
        _port = port;
        _requestShutdown = requestShutdown;
        _icon = new NotifyIcon
        {
            Icon = BuildIcon(),
            Text = "ApiCallInter · API 定时保活",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        // 双击托盘 = 打开管理页：绑定放构造器体内（字段初始化器无法可靠引用 _icon，brief 注记）
        _icon.DoubleClick += (_, _) => OpenAdminPage();
    }

    public void OpenAdminPage()
    {
        try { Process.Start(new ProcessStartInfo($"http://localhost:{_port()}") { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show($"打开管理页失败：{ex.Message}", "ApiCallInter"); }
    }

    protected override void ExitThreadCore()
    {
        _icon.Visible = false;
        _icon.Dispose();
        base.ExitThreadCore();
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("打开管理页", null, (_, _) => OpenAdminPage());
        var auto = new ToolStripMenuItem("开机自启") { Checked = _services.GetRequiredService<IAutoStartManager>().IsEnabled() };
        auto.Click += (_, _) =>
        {
            var m = _services.GetRequiredService<IAutoStartManager>();
            m.SetEnabled(!m.IsEnabled());
            auto.Checked = m.IsEnabled();
        };
        menu.Items.Add(auto);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => _requestShutdown());
        return menu;
    }

    private static Icon BuildIcon()
    {
        // csproj ApplicationIcon 已把 ApiCallInter.ico 嵌入 exe，托盘直接提取内嵌图标（免随包分发 ico 文件）；
        // 提取失败退回系统默认图标：托盘图标仅是展示位，不应阻断启动
        var exePath = Environment.ProcessPath ?? Application.ExecutablePath;
        try { return Icon.ExtractAssociatedIcon(exePath) ?? SystemIcons.Application; }
        catch { return SystemIcons.Application; }
    }
}
