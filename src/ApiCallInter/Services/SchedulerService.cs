using System.Collections.Concurrent;
using ApiCallInter.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ApiCallInter.Services;

public class SchedulerOptions { public int LogRetentionDays { get; set; } = 90; }

public record PlanSnapshot(DateTimeOffset NextRunAt, bool Running);

public interface IScheduleReloader { Task ReloadAsync(); }
public interface IScheduleState { IReadOnlyDictionary<int, PlanSnapshot> GetSnapshot(); }

public sealed class Plan
{
    public required int ProjectId { get; init; }
    public required int IntervalSeconds { get; init; }
    public required int JitterMs { get; init; }
    public DateTimeOffset NextRunAt { get; set; }
    public DateTimeOffset TriggeredAt { get; set; }
    public int Running;   // 0/1，Interlocked 防重入
}

public class SchedulerService(IServiceScopeFactory scopeFactory, SchedulePlanner planner,
    Microsoft.Extensions.Options.IOptions<SchedulerOptions> options, TimeSpan? tickInterval = null)
    : BackgroundService, IScheduleReloader, IScheduleState
{
    private readonly ConcurrentDictionary<int, Plan> _plans = new();
    // 串行化本服务内所有 DbContext 访问：ReloadAsync 与启动加载/清理并发时，
    // 若共用同一 AppDbContext（测试单例注册场景）EF 并发检测会抛异常；生产 scoped 上下文下无争用
    private readonly SemaphoreSlim _dbLock = new(1, 1);
    private readonly ILogger<SchedulerService> _logger = scopeFactory.CreateScope().ServiceProvider
        .GetRequiredService<ILogger<SchedulerService>>();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 7×24：所有阶段 catch-all，ExecuteAsync 永不因单次异常退出（spec 11.1）
        try { await LoadPlansAsync(setImmediate: true, stoppingToken); }
        catch (Exception ex) { _logger.LogError(ex, "启动加载计划失败"); }

        try
        {
            using var s = scopeFactory.CreateScope();
            await _dbLock.WaitAsync(stoppingToken);
            try { await CleanLogsAsync(s.ServiceProvider.GetRequiredService<AppDbContext>(), DateTime.UtcNow, options.Value.LogRetentionDays); }
            finally { _dbLock.Release(); }
        }
        catch (Exception ex) { _logger.LogError(ex, "启动日志清理失败"); }

        var tick = new PeriodicTimer(tickInterval ?? TimeSpan.FromMilliseconds(500));
        var cleanup = CleanupLoopAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await tick.WaitForNextTickAsync(stoppingToken)) break;
                foreach (var p in _plans.Values)
                    if (p.NextRunAt <= DateTimeOffset.UtcNow && Interlocked.CompareExchange(ref p.Running, 1, 0) == 0)
                        _ = RunProjectAsync(p);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogError(ex, "调度循环异常，继续下一轮"); }
        }
        await cleanup;
    }

    internal async Task RunProjectAsync(Plan p)
    {
        try
        {
            p.TriggeredAt = DateTimeOffset.UtcNow;
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await _dbLock.WaitAsync();
            Project? project;
            try { project = await db.Projects.Include(x => x.Endpoints).FirstOrDefaultAsync(x => x.Id == p.ProjectId); }
            finally { _dbLock.Release(); }
            if (project is null || !project.Enabled) { _plans.TryRemove(p.ProjectId, out _); return; }
            await scope.ServiceProvider.GetRequiredService<IApiInvoker>().InvokeProjectAsync(project);
        }
        catch (Exception ex) { _logger.LogError(ex, "项目 {ProjectId} 执行异常", p.ProjectId); }
        finally
        {
            p.NextRunAt = planner.ComputeNextRun(p.TriggeredAt, p.IntervalSeconds, p.JitterMs);
            p.Running = 0;
        }
    }

    public async Task ReloadAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await LoadPlansAsync(setImmediate: false, cts.Token);
    }

    private async Task LoadPlansAsync(bool setImmediate, CancellationToken ct)
    {
        List<Project> projects;
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await _dbLock.WaitAsync(ct);
            try { projects = await db.Projects.Where(p => p.Enabled).ToListAsync(ct); }
            finally { _dbLock.Release(); }
        }

        _plans.Clear();
        foreach (var p in projects)
            _plans[p.Id] = new Plan
            {
                ProjectId = p.Id, IntervalSeconds = p.IntervalSeconds, JitterMs = p.JitterMilliseconds,
                NextRunAt = setImmediate ? DateTimeOffset.UtcNow
                    : planner.ComputeNextRun(DateTimeOffset.UtcNow, p.IntervalSeconds, p.JitterMilliseconds)
            };
    }

    public IReadOnlyDictionary<int, PlanSnapshot> GetSnapshot() =>
        _plans.ToDictionary(kv => kv.Key, kv => new PlanSnapshot(kv.Value.NextRunAt, kv.Value.Running == 1));

    private async Task CleanupLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var delay = DateTime.Now.Date.AddDays(1) - DateTime.Now;
            try { await Task.Delay(delay, ct); }
            catch (OperationCanceledException) { break; }
            try
            {
                using var scope = scopeFactory.CreateScope();
                await _dbLock.WaitAsync();
                try { await CleanLogsAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), DateTime.UtcNow, options.Value.LogRetentionDays); }
                finally { _dbLock.Release(); }
            }
            catch (Exception ex) { _logger.LogError(ex, "日志清理失败"); }
        }
    }

    public static async Task CleanLogsAsync(AppDbContext db, DateTime utcNow, int retentionDays) =>
        await db.RequestLogs.Where(l => l.RequestedAt < utcNow.AddDays(-retentionDays)).ExecuteDeleteAsync();
}
