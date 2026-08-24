using ApiCallInter.Data;
using ApiCallInter.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ApiCallInter.Tests;

public class ApiInvokerTests
{
    private static (ApiInvoker invoker, AppDbContext db) Build(HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        var db = TestDb.Create();
        services.AddSingleton(db);
        services.AddLogging();
        services.AddSingleton<IHttpClientFactory>(new SingleHttpClientFactory(handler));
        var invoker = new ApiInvoker(
            new SingleHttpClientFactory(handler),
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>());
        return (invoker, db);
    }

    [Fact]
    public async Task Ok200_Success_NoError()
    {
        var (invoker, _) = Build(StubHttpMessageHandler.Ok());
        var r = await invoker.InvokeAsync(Ep());
        Assert.True(r.Success);
        Assert.Equal(200, r.StatusCode);
        Assert.Null(r.ErrorMessage);
    }

    [Fact]
    public async Task Status500_Fails_WithStatusCode()
    {
        var (invoker, _) = Build(StubHttpMessageHandler.Status(500));
        var r = await invoker.InvokeAsync(Ep());
        Assert.False(r.Success);
        Assert.Equal(500, r.StatusCode);
    }

    [Fact]
    public async Task Timeout_Classified_AsTimeout_NoStatusCode()
    {
        var (invoker, _) = Build(StubHttpMessageHandler.Timeout());
        var r = await invoker.InvokeAsync(Ep());   // Ep() 超时 1 秒
        Assert.False(r.Success);
        Assert.Null(r.StatusCode);
        Assert.Contains("超时", r.ErrorMessage);
    }

    [Fact]
    public async Task NetworkError_Classified_HttpRequestException()
    {
        var (invoker, _) = Build(StubHttpMessageHandler.NetworkError());
        var r = await invoker.InvokeAsync(Ep());
        Assert.False(r.Success);
        Assert.Contains("HttpRequestException", r.ErrorMessage);
    }

    [Fact]
    public async Task InvokeProject_WritesLogPerEndpoint()
    {
        var (invoker, db) = Build(StubHttpMessageHandler.Ok());
        var project = new Project { Id = 9, Name = "P", Enabled = true };
        project.Endpoints.Add(Ep(1)); project.Endpoints.Add(Ep(2));
        project.Endpoints[1].Enabled = false;      // 停用接口不请求不落日志

        await invoker.InvokeProjectAsync(project);

        var logs = db.RequestLogs.ToList();
        Assert.Single(logs);
        Assert.Equal(9, logs[0].ProjectId);
        Assert.True(logs[0].Success);
    }

    [Fact]
    public async Task Headers_And_Body_Sent()
    {
        string? authHeader = null; string? bodyText = null;
        var handler = new StubHttpMessageHandler(async (req, _) =>
        {
            authHeader = req.Headers.TryGetValues("Authorization", out var v) ? string.Join(",", v) : null;
            bodyText = req.Content == null ? null : await req.Content.ReadAsStringAsync();
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        });
        var (invoker, _) = Build(handler);
        var ep = Ep(); ep.Method = "POST"; ep.Headers = """{"Authorization":"Bearer t1"}"""; ep.Body = "hello=1";
        var r = await invoker.InvokeAsync(ep);
        Assert.True(r.Success);
        Assert.Equal("Bearer t1", authHeader);
        Assert.Equal("hello=1", bodyText);
    }

    [Fact]
    public async Task Post_JsonContentType_RoutedToContentHeaders_NoException()
    {
        string? contentType = null;
        var handler = new StubHttpMessageHandler((req, _) =>
        {
            contentType = req.Content?.Headers.ContentType?.ToString();
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        });
        var (invoker, _) = Build(handler);
        var ep = Ep(); ep.Method = "POST";
        ep.Headers = """{"Content-Type":"application/json"}""";
        ep.Body = """{"name":"保活","url":"http://test.local/api"}""";
        var r = await invoker.InvokeAsync(ep);
        Assert.True(r.Success);
        Assert.Null(r.ErrorMessage);                    // 修复前：Misused header name → 每轮都记失败
        Assert.Equal("application/json", contentType);  // 内容头实际生效
    }

    [Fact]
    public async Task Get_WithContentTypeHeader_Skipped_NoThrow()
    {
        var (invoker, _) = Build(StubHttpMessageHandler.Ok());
        var ep = Ep(); ep.Headers = """{"Content-Type":"application/json"}""";   // GET 无 body，无 req.Content
        var r = await invoker.InvokeAsync(ep);
        Assert.True(r.Success);
        Assert.Null(r.ErrorMessage);
    }

    private static ApiEndpoint Ep(int id = 1) => new()
    { Id = id, ProjectId = 9, Name = "健康检查", Url = "http://test.local/api", Method = "GET", TimeoutSeconds = 1, Enabled = true };
}

public class SingleHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler);
}
