namespace ApiCallInter.Tray;

public static class SingleInstance
{
    private static Mutex? _mutex;
    public static bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: true, @"Global\ApiCallInter", out var createdNew);
        if (createdNew) return true;
        _mutex.Dispose(); _mutex = null;
        return false;
    }
    public static void Release()
    {
        try { _mutex?.ReleaseMutex(); } catch (ApplicationException) { }   // 非持有线程释放
        _mutex?.Dispose(); _mutex = null;
    }
}
