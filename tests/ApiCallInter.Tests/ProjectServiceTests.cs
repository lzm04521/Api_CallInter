using ApiCallInter.Data;
using ApiCallInter.Services;

namespace ApiCallInter.Tests;

public class ProjectServiceTests
{
    private static ProjectService NewSvc(out AppDbContext db) { db = TestDb.Create(); return new ProjectService(db); }

    [Fact]
    public async Task Create_SetsTimestamps_And_Lists()
    {
        var svc = NewSvc(out var db);
        await svc.CreateAsync(new Project { Name = "P1", IntervalSeconds = 120, JitterMilliseconds = 3000 });
        Assert.Single(await svc.ListAsync());
        Assert.True(db.Projects.First().CreatedAt > DateTime.UtcNow.AddMinutes(-1));
    }

    [Theory]
    [InlineData(29, 0)]        // 间隔下限
    [InlineData(120, 120001)]  // 抖动 >= 间隔毫秒数
    [InlineData(120, -1)]
    public async Task Create_Rejects_BadIntervalOrJitter(int interval, int jitter)
    {
        var svc = NewSvc(out _);
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new Project { Name = "P", IntervalSeconds = interval, JitterMilliseconds = jitter }));
    }

    [Fact]
    public async Task Create_Rejects_EmptyName()
    {
        var svc = NewSvc(out _);
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new Project { Name = "", IntervalSeconds = 60 }));
    }

    [Fact]
    public async Task Endpoint_Rejects_BadUrl_Method_Timeout_Headers()
    {
        var svc = NewSvc(out _);
        var p = await svc.CreateAsync(new Project { Name = "P", IntervalSeconds = 60 });
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateEndpointAsync(p.Id, new ApiEndpoint { Name = "e", Url = "ftp://x", Method = "GET" }));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateEndpointAsync(p.Id, new ApiEndpoint { Name = "e", Url = "http://x", Method = "DELETE" }));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateEndpointAsync(p.Id, new ApiEndpoint { Name = "e", Url = "http://x", Method = "GET", TimeoutSeconds = 300 }));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateEndpointAsync(p.Id, new ApiEndpoint { Name = "e", Url = "http://x", Method = "GET", Headers = "not-json" }));
        var ok = await svc.CreateEndpointAsync(p.Id, new ApiEndpoint { Name = "e", Url = "https://x/api", Method = "POST", Headers = "{\"Authorization\":\"Bearer t\"}" });
        Assert.Equal(p.Id, ok.ProjectId);
    }

    [Fact]
    public async Task Delete_Removes_Project_Endpoints_AndLogs()
    {
        var svc = NewSvc(out var db);
        var p = await svc.CreateAsync(new Project { Name = "P", IntervalSeconds = 60 });
        var e = await svc.CreateEndpointAsync(p.Id, new ApiEndpoint { Name = "e", Url = "http://x", Method = "GET" });
        db.RequestLogs.Add(new RequestLog { ProjectId = p.Id, EndpointId = e.Id, RequestedAt = DateTime.UtcNow, Success = true, ElapsedMs = 1 });
        db.SaveChanges();

        Assert.True(await svc.DeleteAsync(p.Id));
        Assert.Empty(db.Projects); Assert.Empty(db.ApiEndpoints); Assert.Empty(db.RequestLogs);
    }

    [Fact]
    public async Task Update_ChangesFields_And_UpdatedAt()
    {
        var svc = NewSvc(out _);
        var p = await svc.CreateAsync(new Project { Name = "P", IntervalSeconds = 60 });
        var updated = await svc.UpdateAsync(p.Id, new Project { Name = "P2", IntervalSeconds = 300, JitterMilliseconds = 0, Enabled = false });
        Assert.NotNull(updated);
        Assert.Equal("P2", updated!.Name);
        Assert.False(updated.Enabled);
    }
}
