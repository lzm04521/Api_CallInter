using ApiCallInter.Data;
using ApiCallInter.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApiCallInter.Api;

public static class ApiEndpoints
{
    public static void MapAll(this WebApplication app)
    {
        // 项目增删改后刷新调度计划（endpoint 增删改不用 reload，调度执行时实时读库）
        var reloader = app.Services.GetRequiredService<IScheduleReloader>();

        // ValidationException → 400
        app.Use(async (ctx, next) =>
        {
            try { await next(); }
            catch (ValidationException ex)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsJsonAsync(new { message = ex.Message });
            }
        });

        var g = app.MapGroup("/api");
        g.MapGet("/projects", async (ProjectService s) => await s.ListAsync());
        g.MapPost("/projects", async (ProjectService s, Project p) => { var created = await s.CreateAsync(p); await reloader.ReloadAsync(); return Results.Ok(created); });
        g.MapPut("/projects/{id}", async (ProjectService s, int id, Project p) => { var u = await s.UpdateAsync(id, p); if (u is null) return Results.NotFound(); await reloader.ReloadAsync(); return Results.Ok(u); });
        g.MapDelete("/projects/{id}", async (ProjectService s, int id) => { var ok = await s.DeleteAsync(id); if (!ok) return Results.NotFound(); await reloader.ReloadAsync(); return Results.Ok(); });

        g.MapPost("/projects/{id}/endpoints", async (ProjectService s, int id, ApiEndpoint e) => Results.Ok(await s.CreateEndpointAsync(id, e)));
        g.MapPut("/endpoints/{id}", async (ProjectService s, int id, ApiEndpoint e) => await s.UpdateEndpointAsync(id, e) is { } u ? Results.Ok(u) : Results.NotFound());
        g.MapDelete("/endpoints/{id}", async (ProjectService s, int id) => await s.DeleteEndpointAsync(id) ? Results.Ok() : Results.NotFound());

        g.MapPost("/endpoints/{id}/invoke", async (AppDbContext db, IApiInvoker invoker, int id) =>
        {
            var ep = await db.ApiEndpoints.FindAsync(id);
            return ep is null ? Results.NotFound() : Results.Ok(await invoker.InvokeSingleAsync(ep));
        });

        g.MapGet("/logs", async (AppDbContext db, int? projectId, int? endpointId, string? result, int page = 1, int pageSize = 50) =>
        {
            page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 200);
            var q = db.RequestLogs.AsQueryable();
            if (projectId is not null) q = q.Where(l => l.ProjectId == projectId);
            if (endpointId is not null) q = q.Where(l => l.EndpointId == endpointId);
            if (result == "success") q = q.Where(l => l.Success);
            if (result == "failed") q = q.Where(l => !l.Success);
            var total = await q.LongCountAsync();
            var items = await q.OrderByDescending(l => l.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            return Results.Ok(new { items, total, page, pageSize });
        });

        g.MapGet("/overview", async (OverviewService s) => await s.GetAsync());
        g.MapGet("/settings", async (AppDbContext db, IAutoStartManager auto) =>
            Results.Ok(new { webPort = await SettingsService.GetPortAsync(db) ?? SettingsService.DefaultPort, autoStart = auto.IsEnabled() }));

        // 修改端口写库并提示需重启；1024~65535 校验走 ValidationException → 400
        g.MapPut("/settings", async (AppDbContext db, UpdatePortRequest req) =>
        {
            if (req.WebPort is < 1024 or > 65535) throw new ValidationException("端口必须在 1024~65535");
            var old = await SettingsService.GetPortAsync(db) ?? SettingsService.DefaultPort;
            await SettingsService.SetPortAsync(db, req.WebPort);
            return Results.Ok(new { needsRestart = req.WebPort != old });
        });
        g.MapPost("/settings/autostart", (IAutoStartManager m, AutoStartRequest req) =>
        {
            m.SetEnabled(req.Enabled);
            return Results.Ok(new { enabled = req.Enabled });
        });
        // 延迟 300ms 让响应先回到客户端，再拉起 --delayed-start 新实例并退出当前进程
        g.MapPost("/app/restart", () =>
        {
            _ = Task.Run(async () => { await Task.Delay(300); AppRestarter.RestartDelayed(); });
            return Results.Ok();
        });
    }
}

// 请求体 DTO：minimal API 的简单类型参数只会从 query 绑定，按契约 {webPort}/{enabled} 的 JSON body 需用记录类型
public record UpdatePortRequest(int WebPort);
public record AutoStartRequest(bool Enabled);
