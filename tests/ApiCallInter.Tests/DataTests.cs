using ApiCallInter.Data;
using Microsoft.Data.Sqlite;

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

    [Fact]
    public void EnsureSortOrderColumn_Patches_LegacySchema_And_IsIdempotent()
    {
        var path = Path.Combine(Path.GetTempPath(), "apicallinter-test-" + Guid.NewGuid() + ".db");
        try
        {
            // 手工建 v0.1.x 老 schema（无 SortOrder 列）+ 一条老数据
            using (var conn = new SqliteConnection($"Data Source={path}"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    CREATE TABLE Projects (Id INTEGER CONSTRAINT "PK_Projects" PRIMARY KEY, Name TEXT NOT NULL, Description TEXT NOT NULL,
                        IntervalSeconds INTEGER NOT NULL, JitterMilliseconds INTEGER NOT NULL, Enabled INTEGER NOT NULL,
                        CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL);
                    INSERT INTO Projects (Name, Description, IntervalSeconds, JitterMilliseconds, Enabled, CreatedAt, UpdatedAt)
                    VALUES ('Old', '', 60, 0, 1, '2026-01-01 00:00:00', '2026-01-01 00:00:00');
                    """;
                cmd.ExecuteNonQuery();
            }

            AppDbContext.EnsureSortOrderColumn(path);
            AppDbContext.EnsureSortOrderColumn(path);   // 幂等：已含列时 no-op

            using (var db = AppDbContext.Create(path))
            {
                var p = db.Projects.Single();
                Assert.Equal(0, p.SortOrder);           // 补列默认 0（退化按名称排）
                p.SortOrder = 5;
                db.SaveChanges();
            }
            using (var db = AppDbContext.Create(path))
                Assert.Equal(5, db.Projects.Single().SortOrder);   // 补列后 EF 正常读写
        }
        finally { try { File.Delete(path); } catch { } }
    }
}
