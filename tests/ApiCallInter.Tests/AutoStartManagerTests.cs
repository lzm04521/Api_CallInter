using ApiCallInter.Services;

namespace ApiCallInter.Tests;

public class AutoStartManagerTests
{
    private class FakeManager : IAutoStartManager
    {
        public bool State;
        public bool IsEnabled() => State;
        public void SetEnabled(bool enabled) => State = enabled;
    }

    [Fact]
    public void Fake_Toggle_Works()   // 接口契约冒烟；真注册表走手动验证
    {
        IAutoStartManager m = new FakeManager();
        Assert.False(m.IsEnabled());
        m.SetEnabled(true);
        Assert.True(m.IsEnabled());
    }
}
