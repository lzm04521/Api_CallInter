namespace ApiCallInter.Services;

/// <summary>下一轮执行时间计算：间隔 ± 随机抖动（双向均匀），打散请求节奏。</summary>
public class SchedulePlanner(Random random)
{
    public DateTimeOffset ComputeNextRun(DateTimeOffset triggerTime, int intervalSeconds, int jitterMs)
    {
        var jitter = TimeSpan.FromMilliseconds(random.Next(-jitterMs, jitterMs + 1));
        return triggerTime + TimeSpan.FromSeconds(intervalSeconds) + jitter;
    }
}
