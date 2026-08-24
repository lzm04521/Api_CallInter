using Microsoft.EntityFrameworkCore;

namespace ApiCallInter.Data;

public static class SettingsService
{
    public const int DefaultPort = 61121;

    public static async Task<int?> GetPortAsync(AppDbContext db)
    {
        var s = await db.AppSettings.FindAsync("WebPort");
        return s is null ? null : int.Parse(s.Value);
    }

    public static async Task SetPortAsync(AppDbContext db, int port)
    {
        var s = await db.AppSettings.FindAsync("WebPort");
        if (s is null) db.AppSettings.Add(new AppSetting { Key = "WebPort", Value = port.ToString() });
        else s.Value = port.ToString();
        await db.SaveChangesAsync();
    }
}
