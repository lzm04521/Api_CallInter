using System.Diagnostics;
using ApiCallInter.Api;
using ApiCallInter.Data;
using ApiCallInter.Services;
using ApiCallInter.Tray;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace ApiCallInter;

internal static class Program
{
    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        // 1) 未捕获异常兜底：记录后尽量存活（spec 11.1）
        AppDomain.CurrentDomain.UnhandledException += (_, e) => { try { Log.Fatal(e.ExceptionObject as Exception, "未捕获异常"); } catch { } };
        TaskScheduler.UnobservedTaskException += (_, e) => { Log.Error(e.Exception, "未观察任务异常"); e.SetObserved(); };

        // 2) 引导日志：宿主 UseSerilog 接管文件日志之前，早期启动阶段的失败也要落盘（spec 11.1）
        Log.Logger = new LoggerConfiguration()
            .WriteTo.File(Path.Combine(AppPaths.LogsDir, "bootstrap-.log"),
                rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
            .CreateLogger();

        // 3) 数据目录 + 早期建库 + 读端口：失败必须写明原因（弹窗 + bootstrap 日志，spec 11.1）
        int port;
        try
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            Directory.CreateDirectory(AppPaths.LogsDir);
            using (var db = AppDbContext.Create(AppPaths.DbPath)) db.Database.EnsureCreated();
            using (var db = AppDbContext.Create(AppPaths.DbPath)) port = await SettingsService.GetPortAsync(db) ?? SettingsService.DefaultPort;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "启动失败：数据目录/建库/端口读取异常");
            Log.CloseAndFlush();
            MessageBox.Show($"ApiCallInter 启动失败：{ex.Message}", "ApiCallInter",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }

        // 4) 单实例（spec 4.2）。--delayed-start 必须在抢 Mutex 之前等旧实例退出释放端口/Mutex，
        //    否则重启时新实例会立刻抢锁失败而退化成"打开管理页后退出"
        if (args.Contains("--delayed-start")) await Task.Delay(3000);
        if (!SingleInstance.TryAcquire())
        {
            // 已在运行：直接打开管理页后退出（spec 4.2）
            try { Process.Start(new ProcessStartInfo($"http://localhost:{port}") { UseShellExecute = true }); } catch { }
            Log.CloseAndFlush();
            return 0;
        }

        // 5) Web 宿主后台运行 + 托盘消息循环
        var app = BuildWebHost(port);
        var cts = new CancellationTokenSource();
        var hostTask = Task.Run(() => app.RunAsync(cts.Token));

        ApplicationConfiguration.Initialize();
        using var ctx = new TrayApplicationContext(app.Services, () => port, () =>
        {
            cts.Cancel();
            Application.Exit();
        });
        Application.Run(ctx);

        // 消息循环结束（托盘"退出"或 AppRestarter 重启路径）后统一停 host：
        // 重启路径只调 Application.Exit 不走托盘退出回调，若不在此 Cancel，await hostTask 永不返回、Mutex 不释放
        cts.Cancel();
        try { await hostTask; } catch (OperationCanceledException) { }
        SingleInstance.Release();
        Log.CloseAndFlush();
        Environment.Exit(0);
        return 0;
    }

    /// <summary>Task 7 宿主组装整体迁移至此，另增托盘/自启/更新占位注册。</summary>
    internal static WebApplication BuildWebHost(int port)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        builder.Host.UseSerilog((_, cfg) => cfg
            .WriteTo.File(Path.Combine(AppPaths.LogsDir, "app-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30)
            .MinimumLevel.Information());
        builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={AppPaths.DbPath}"));
        // 超时由 ApiInvoker 的 per-endpoint CTS 控制，HttpClient 本体不设超时（否则 >100s 的端点会撞默认 100s 被误分类）
        builder.Services.AddHttpClient("invoker").ConfigureHttpClient(c => c.Timeout = Timeout.InfiniteTimeSpan);
        builder.Services.AddSingleton<IApiInvoker, ApiInvoker>();
        builder.Services.AddSingleton<Random>();
        builder.Services.AddSingleton<SchedulePlanner>();
        builder.Services.AddSingleton<SchedulerService>();
        builder.Services.AddSingleton<IScheduleReloader>(sp => sp.GetRequiredService<SchedulerService>());
        builder.Services.AddSingleton<IScheduleState>(sp => sp.GetRequiredService<SchedulerService>());
        builder.Services.AddHostedService(sp => sp.GetRequiredService<SchedulerService>());
        builder.Services.AddScoped<ProjectService>();
        builder.Services.AddScoped<OverviewService>();
        builder.Services.AddSingleton<IAutoStartManager, RegistryAutoStartManager>();
        builder.Services.Configure<SchedulerOptions>(builder.Configuration.GetSection("Scheduler"));
        builder.Services.Configure<UpdateOptions>(builder.Configuration.GetSection("Update"));

        var app = builder.Build();
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.MapAll();
        return app;
    }
}
