using ApiCallInter;
using ApiCallInter.Api;
using ApiCallInter.Data;
using ApiCallInter.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

// 1) 未捕获异常兜底：记录后尽量存活（spec 11.1）
AppDomain.CurrentDomain.UnhandledException += (_, e) => { try { Log.Fatal(e.ExceptionObject as Exception, "未捕获异常"); } catch { } };
TaskScheduler.UnobservedTaskException += (_, e) => { Log.Error(e.Exception, "未观察任务异常"); e.SetObserved(); };

// 2) 数据目录 + 早期建库 + 读端口
Directory.CreateDirectory(AppPaths.DataDir);
Directory.CreateDirectory(AppPaths.LogsDir);
using (var db = AppDbContext.Create(AppPaths.DbPath)) db.Database.EnsureCreated();
int port;
using (var db = AppDbContext.Create(AppPaths.DbPath)) port = await SettingsService.GetPortAsync(db) ?? SettingsService.DefaultPort;

// 3) Web 宿主
var builder = WebApplication.CreateBuilder();
builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
builder.Host.UseSerilog((_, cfg) => cfg.WriteTo.File(
    Path.Combine(AppPaths.LogsDir, "app-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30));
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
builder.Services.Configure<SchedulerOptions>(builder.Configuration.GetSection("Scheduler"));

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapAll();
await app.RunAsync();
