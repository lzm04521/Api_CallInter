namespace ApiCallInter.Tests;

public class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;
    public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) => _handler = handler;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => _handler(request, ct);

    public static StubHttpMessageHandler Ok() => new((_, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)));
    public static StubHttpMessageHandler Status(int code) => new((_, _) => Task.FromResult(new HttpResponseMessage((System.Net.HttpStatusCode)code)));
    public static StubHttpMessageHandler Timeout() => new(async (_, ct) => { await Task.Delay(10_000, ct); return null!; });
    public static StubHttpMessageHandler NetworkError() => new((_, _) => throw new HttpRequestException("无法连接远程服务器"));
}
