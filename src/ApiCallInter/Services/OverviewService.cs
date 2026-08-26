using System.Diagnostics;
using System.Reflection;
using ApiCallInter.Data;
using Microsoft.EntityFrameworkCore;

namespace ApiCallInter.Services;

public record LastRoundInfo(DateTime RequestedAt, int Total, int Ok, int Failed, int? LastStatusCode);
public record ProjectOverview(int Id, string Name, bool Enabled, int IntervalSeconds, int JitterMilliseconds,
    int EndpointCount, int EnabledEndpointCount, DateTimeOffset? NextRunAt, LastRoundInfo? LastRound);
public record Stats24h(long Total, long Failed);
public record OverviewDto(string Version, TimeSpan Uptime, DateTime ProcessStartTime, long WorkingSetBytes,
    long ManagedMemoryBytes, Stats24h Stats24h, List<ProjectOverview> Projects);

public class OverviewService(AppDbContext db, IScheduleState scheduleState)
{
    public async Task<OverviewDto> GetAsync()
    {
        var since = DateTime.UtcNow.AddHours(-24);
        var total = await db.RequestLogs.LongCountAsync(l => l.RequestedAt >= since);
        var failed = await db.RequestLogs.LongCountAsync(l => l.RequestedAt >= since && !l.Success);
        var snapshot = scheduleState.GetSnapshot();
        var projects = await db.Projects.Include(p => p.Endpoints)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Name).ToListAsync();   // 与项目管理页同序（老库 SortOrder 全 0 时退化按名称）

        var list = projects.Select(p =>
        {
            // 不在调度快照中的项目（如已禁用）NextRunAt 为 null，而非抛 NRE
            DateTimeOffset? nextRunAt = snapshot.TryGetValue(p.Id, out var plan) ? plan.NextRunAt : null;
            return new ProjectOverview(p.Id, p.Name, p.Enabled, p.IntervalSeconds, p.JitterMilliseconds,
                p.Endpoints.Count, p.Endpoints.Count(e => e.Enabled),
                nextRunAt, BuildLastRound(p.Id));
        }).ToList();

        using var proc = Process.GetCurrentProcess();
        return new OverviewDto(
            Assembly.GetEntryAssembly()!.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0],
            DateTime.UtcNow - proc.StartTime.ToUniversalTime(), proc.StartTime.ToUniversalTime(),
            proc.WorkingSet64, GC.GetTotalMemory(false), new Stats24h(total, failed), list);
    }

    private LastRoundInfo? BuildLastRound(int projectId)
    {
        var logs = db.RequestLogs.Where(l => l.ProjectId == projectId).OrderByDescending(l => l.Id).Take(50).ToList();
        if (logs.Count == 0) return null;
        // 最新一轮取时间最新的记录（Id 序≠时间序：补录/乱序日志会让旧数据 Id 更大）
        var newest = logs.Max(l => l.RequestedAt);
        var round = logs.Where(l => (newest - l.RequestedAt).TotalSeconds <= 5).ToList();  // 同一轮：5 秒窗口内
        return new LastRoundInfo(newest, round.Count, round.Count(r => r.Success), round.Count(r => !r.Success),
            round.OrderByDescending(r => r.Id).First().StatusCode);
    }
}
