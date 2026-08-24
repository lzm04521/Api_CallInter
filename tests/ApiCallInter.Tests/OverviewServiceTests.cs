using ApiCallInter.Data;
using ApiCallInter.Services;

namespace ApiCallInter.Tests;

public class OverviewServiceTests
{
    [Fact]
    public async Task Overview_Computes_24hStats_And_LastRound()
    {
        using var db = TestDb.Create();
        var p = new Project { Name = "P", IntervalSeconds = 60, Enabled = true };
        p.Endpoints.Add(new ApiEndpoint { Name = "e1", Url = "http://x", Method = "GET", Enabled = true });
        p.Endpoints.Add(new ApiEndpoint { Name = "e2", Url = "http://x", Method = "GET", Enabled = true });
        db.Projects.Add(p); db.SaveChanges();
        db.RequestLogs.Add(new RequestLog { ProjectId = p.Id, EndpointId = 1, RequestedAt = DateTime.UtcNow.AddMinutes(-1), Success = true, StatusCode = 200, ElapsedMs = 10 });
        db.RequestLogs.Add(new RequestLog { ProjectId = p.Id, EndpointId = 2, RequestedAt = DateTime.UtcNow.AddMinutes(-1), Success = false, StatusCode = 500, ElapsedMs = 10 });
        db.RequestLogs.Add(new RequestLog { ProjectId = p.Id, EndpointId = 1, RequestedAt = DateTime.UtcNow.AddDays(-2), Success = true, StatusCode = 200, ElapsedMs = 10 });
        db.SaveChanges();

        var state = new FixedState(new Dictionary<int, PlanSnapshot> { [p.Id] = new(DateTimeOffset.UtcNow.AddSeconds(30), false) });
        var o = await new OverviewService(db, state).GetAsync();

        Assert.Equal(2, o.Stats24h.Total);         // 2 天前的不计
        Assert.Equal(1, o.Stats24h.Failed);
        var proj = o.Projects.Single();
        Assert.Equal(2, proj.LastRound!.Total);
        Assert.Equal(1, proj.LastRound.Failed);
        Assert.Equal(500, proj.LastRound.LastStatusCode);
        Assert.Equal(2, proj.EndpointCount);
    }

    private class FixedState(Dictionary<int, PlanSnapshot> map) : IScheduleState
    {
        public IReadOnlyDictionary<int, PlanSnapshot> GetSnapshot() => map;
    }
}
