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
    // 同步 Main：async Main 在首个真正挂起的 await 后会把续体调度到线程池 MTA 线程，
    // Application.Run/托盘就会离开 STA 主线程（[STAThread] 形同虚设，OLE 类功能会静默损坏）。
    // 因此初始化阶段全部用阻塞等待（GetAwaiter().GetResult()/Thread.Sleep）完成，消息循环始终运行在 STA 主线程。
    [STAThread]
    private static int Main(string[] args)
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
            UpdateService.CleanupStaleUpdates();   // 启动清上次升级残留（spec 4.6），失败留待下次
            using (var db = AppDbContext.Create(AppPaths.DbPath)) db.Database.EnsureCreated();
            using (var db = AppDbContext.Create(AppPaths.DbPath)) port = SettingsService.GetPortAsync(db).GetAwaiter().GetResult() ?? SettingsService.DefaultPort;
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
        //    否则重启时新实例会立刻抢锁失败而退化成"打开管理页后退出"。
        //    同步 Sleep 阻塞等待，主线程不挂起让渡，保持 STA
        if (args.Contains("--delayed-start")) Thread.Sleep(3000);
        if (!SingleInstance.TryAcquire())
        {
            // 已在运行：直接打开管理页后退出（spec 4.2）
            try { Process.Start(new ProcessStartInfo($"http://localhost:{port}") { UseShellExecute = true }); } catch { }
            Log.CloseAndFlush();
            return 0;
        }

        // 5) Web 宿主后台运行 + 托盘消息循环（以下均在 STA 主线程）
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
        // 重启路径只调 Application.Exit 不走托盘退出回调，若不在此 Cancel，hostTask 永不完成、Mutex 不释放
        cts.Cancel();
        try { hostTask.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
        SingleInstance.Release();
        Log.CloseAndFlush();
        Environment.Exit(0);
        return 0;
    }

    /// <summary>Task 7 宿主组装整体迁移至此，另增托盘/自启/更新占位注册。</summary>
    internal static WebApplication BuildWebHost(int port)
    {
        // 内容根固定为 exe 目录：开机自启/AppRestarter 不带工作目录启动，CWD 任意时 UseStaticFiles 找不到 wwwroot 会整页 404
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
        // spec §10：host 由 appsettings Urls 模板控制（可改 0.0.0.0 远程管理），端口由设置页（DB）控制；
        // 代码 UseUrls 会覆盖配置 Urls 键，故从模板解析 host 后与 DB 端口重组，而不是直接忽略配置
        var urlTemplate = builder.Configuration["Urls"] ?? "http://127.0.0.1:61121";
        string host;
        try { host = new Uri(urlTemplate.Replace("*", "0.0.0.0")).Host; } catch { host = "127.0.0.1"; }
        builder.WebHost.UseUrls($"http://{host}:{port}");
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
        builder.Services.AddHttpClient("github");   // GitHub Release 检查/下载，默认超时 100s 足够
        builder.Services.AddSingleton<UpdateService>();
        builder.Services.Configure<SchedulerOptions>(builder.Configuration.GetSection("Scheduler"));
        builder.Services.Configure<UpdateOptions>(builder.Configuration.GetSection("Update"));

        var app = builder.Build();
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.MapAll();
        return app;
    }
}
