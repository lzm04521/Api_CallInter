using ApiCallInter.Services;

namespace ApiCallInter.Tests;

public class SchedulePlannerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 24, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void JitterZero_IsExactInterval()
    {
        var p = new SchedulePlanner(new Random(1));
        Assert.Equal(T0 + TimeSpan.FromSeconds(120), p.ComputeNextRun(T0, 120, 0));
    }

    [Fact]
    public void Samples_StayWithin_PlusMinusJitter()
    {
        var p = new SchedulePlanner(new Random(42));
        for (var i = 0; i < 500; i++)
        {
            var next = p.ComputeNextRun(T0, 120, 3000);
            Assert.InRange(next, T0 + TimeSpan.FromSeconds(117), T0 + TimeSpan.FromSeconds(123));
        }
    }

    [Fact]
    public void Samples_AppearOnBothSides()   // 抖动必须双向，否则等于变相固定偏移
    {
        var p = new SchedulePlanner(new Random(7));
        var below = 0; var above = 0;
        for (var i = 0; i < 500; i++)
        {
            var d = (p.ComputeNextRun(T0, 120, 3000) - (T0 + TimeSpan.FromSeconds(120))).TotalMilliseconds;
            if (d < 0) below++; else if (d > 0) above++;
        }
        Assert.True(below > 50 && above > 50, $"below={below}, above={above}");
    }
}
