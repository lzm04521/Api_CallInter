using ApiCallInter.Data;
using ApiCallInter.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ApiCallInter.Tests;

public class SchedulerServiceTests
{
    private class CountingInvoker : IApiInvoker
    {
        public int Count;
        public Task<InvokeResult> InvokeAsync(ApiEndpoint ep) => throw new NotSupportedException();
        public Task<InvokeResult> InvokeSingleAsync(ApiEndpoint ep) => throw new NotSupportedException();
        public async Task<List<InvokeResult>> InvokeProjectAsync(Project project)
        { Count++; await Task.Delay(50); return [new InvokeResult(true, 200, 1, null)]; }
    }

    private static (SchedulerService svc, CountingInvoker invoker, AppDbContext db) Build()
    {
        var db = TestDb.Create();
        var invoker = new CountingInvoker();
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton<IApiInvoker>(invoker);
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        var sp = services.BuildServiceProvider();
        var svc = new SchedulerService(sp.GetRequiredService<IServiceScopeFactory>(), new SchedulePlanner(new Random(1)), Microsoft.Extensions.Options.Options.Create(new SchedulerOptions()), tickInterval: TimeSpan.FromMilliseconds(50));
        return (svc, invoker, db);
    }

    [Fact]
    public async Task Start_RunsFirstRoundImmediately_ThenWaits()
    {
        var (svc, invoker, db) = Build();
        db.Projects.Add(new Project { Name = "P", IntervalSeconds = 3600, JitterMilliseconds = 0, Enabled = true });
        db.SaveChanges();

        using var cts = new CancellationTokenSource();
        var run = svc.StartAsync(cts.Token);   // 启动（ExecuteAsync 在后台跑）
        await Task.Delay(1000);                // 首轮立即执行；间隔 1 小时不会再有第二轮
        cts.Cancel();
        await svc.StopAsync(CancellationToken.None);

        Assert.Equal(1, invoker.Count);
    }

    [Fact]
    public async Task DisabledProject_IsNotScheduled()
    {
        var (svc, invoker, db) = Build();
        db.Projects.Add(new Project { Name = "P", IntervalSeconds = 1, JitterMilliseconds = 0, Enabled = false });
        db.SaveChanges();

        using var cts = new CancellationTokenSource();
        await svc.StartAsync(cts.Token);
        await Task.Delay(400);
        cts.Cancel();
        await svc.StopAsync(CancellationToken.None);

        Assert.Equal(0, invoker.Count);
    }

    [Fact]
    public async Task Reload_ReschedulesFromNow()
    {
        var (svc, _, db) = Build();
        db.Projects.Add(new Project { Name = "P", IntervalSeconds = 3600, JitterMilliseconds = 0, Enabled = true });
        db.SaveChanges();

        using var cts = new CancellationTokenSource();
        await svc.StartAsync(cts.Token);
        await svc.ReloadAsync();

        var snapshot = svc.GetSnapshot();
        Assert.Single(snapshot);
        var next = snapshot[1].NextRunAt;
        Assert.InRange(next, DateTimeOffset.UtcNow.AddSeconds(3595), DateTimeOffset.UtcNow.AddSeconds(3605));

        cts.Cancel();
        await svc.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task CleanLogs_RemovesOnlyExpired()
    {
        using var db = TestDb.Create();
        db.RequestLogs.Add(new RequestLog { ProjectId = 1, EndpointId = 1, RequestedAt = DateTime.UtcNow.AddDays(-91), Success = true, ElapsedMs = 1 });
        db.RequestLogs.Add(new RequestLog { ProjectId = 1, EndpointId = 1, RequestedAt = DateTime.UtcNow.AddDays(-1), Success = true, ElapsedMs = 1 });
        db.SaveChanges();

        await SchedulerService.CleanLogsAsync(db, DateTime.UtcNow, retentionDays: 90);

        Assert.Single(db.RequestLogs);
        Assert.Equal(DateTime.UtcNow.AddDays(-1).Date, db.RequestLogs.First().RequestedAt.Date);
    }
}
