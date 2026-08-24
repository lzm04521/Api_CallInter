using ApiCallInter.Data;

namespace ApiCallInter.Tests;

public class DataTests
{
    [Fact]
    public void EnsureCreated_Supports_Project_Endpoint_Log_Setting()
    {
        using var db = TestDb.Create();
        var p = new Project { Name = "P1", IntervalSeconds = 120, JitterMilliseconds = 3000, Enabled = true };
        p.Endpoints.Add(new ApiEndpoint { Name = "健康检查", Url = "http://x/api", Method = "GET", TimeoutSeconds = 30, Enabled = true });
        db.Projects.Add(p);
        db.RequestLogs.Add(new RequestLog { ProjectId = 1, EndpointId = 1, RequestedAt = DateTime.UtcNow, Success = true, StatusCode = 200, ElapsedMs = 10 });
        db.AppSettings.Add(new AppSetting { Key = "WebPort", Value = "61121" });
        db.SaveChanges();

        Assert.Equal(1, db.Projects.Count());
        Assert.Equal(1, db.Projects.First().Endpoints.Count);
        Assert.Equal(1, db.RequestLogs.Count());
    }

    [Fact]
    public async Task Settings_Port_Defaults_And_ReadWrite()
    {
        using var db = TestDb.Create();
        Assert.Null(await SettingsService.GetPortAsync(db));          // 未设置时为 null（调用方取默认）
        await SettingsService.SetPortAsync(db, 61199);
        Assert.Equal(61199, await SettingsService.GetPortAsync(db));
        await SettingsService.SetPortAsync(db, 61200);                // 覆盖更新
        Assert.Equal(61200, await SettingsService.GetPortAsync(db));
    }
}
