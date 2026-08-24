using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ApiCallInter.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApiCallInter.Services;

public record InvokeResult(bool Success, int? StatusCode, long ElapsedMs, string? ErrorMessage);

public interface IApiInvoker
{
    Task<InvokeResult> InvokeAsync(ApiEndpoint ep);
    Task<InvokeResult> InvokeSingleAsync(ApiEndpoint ep);
    Task<List<InvokeResult>> InvokeProjectAsync(Project project);
}

public class ApiInvoker(IHttpClientFactory factory, IServiceScopeFactory scopeFactory) : IApiInvoker
{
    public async Task<InvokeResult> InvokeAsync(ApiEndpoint ep)
    {
        var client = factory.CreateClient("invoker");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(ep.TimeoutSeconds));
        var sw = Stopwatch.StartNew();
        try
        {
            using var req = new HttpRequestMessage(new HttpMethod(ep.Method), ep.Url);
            if (!string.IsNullOrWhiteSpace(ep.Headers))
                foreach (var (k, v) in JsonSerializer.Deserialize<Dictionary<string, string>>(ep.Headers) ?? [])
                    req.Headers.TryAddWithoutValidation(k, v);
            if (!string.IsNullOrWhiteSpace(ep.Body) && ep.Method is "POST" or "PUT")
                req.Content = new StringContent(ep.Body, Encoding.UTF8, "text/plain");
            using var resp = await client.SendAsync(req, cts.Token);
            var code = (int)resp.StatusCode;
            return new InvokeResult(code is >= 200 and < 300, code, sw.ElapsedMilliseconds, null);
        }
        catch (TaskCanceledException) when (cts.IsCancellationRequested)
        {
            return new InvokeResult(false, null, sw.ElapsedMilliseconds, $"请求超时（{ep.TimeoutSeconds}s）");
        }
        catch (HttpRequestException ex)
        {
            return new InvokeResult(false, null, sw.ElapsedMilliseconds, $"HttpRequestException: {ex.Message}");
        }
        catch (Exception ex)
        {
            return new InvokeResult(false, null, sw.ElapsedMilliseconds, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    public async Task<InvokeResult> InvokeSingleAsync(ApiEndpoint ep)
    {
        var result = await InvokeAsync(ep);
        await WriteLogsAsync([(ep, result)]);
        return result;
    }

    public async Task<List<InvokeResult>> InvokeProjectAsync(Project project)
    {
        var endpoints = project.Endpoints.Where(e => e.Enabled).ToList();
        var results = await Task.WhenAll(endpoints.Select(async e => (ep: e, r: await InvokeAsync(e))));
        await WriteLogsAsync(results);
        return [.. results.Select(x => x.r)];
    }

    private async Task WriteLogsAsync(IEnumerable<(ApiEndpoint ep, InvokeResult r)> items)
    {
        // 落库失败不影响调度主流程（spec 11.1：DB 写失败仅记文件日志）——此处由调用方 catch，Serilog 记录
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var (ep, r) in items)
            db.RequestLogs.Add(new RequestLog
            {
                ProjectId = ep.ProjectId, EndpointId = ep.Id, RequestedAt = DateTime.UtcNow,
                Success = r.Success, StatusCode = r.StatusCode, ElapsedMs = (int)r.ElapsedMs, ErrorMessage = r.ErrorMessage
            });
        await db.SaveChangesAsync();
    }
}
