using Microsoft.EntityFrameworkCore;

namespace ApiCallInter.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ApiEndpoint> ApiEndpoints => Set<ApiEndpoint>();
    public DbSet<RequestLog> RequestLogs => Set<RequestLog>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    // EF 约定不识别 Key 作为主键，需显式配置（SettingsService.FindAsync("WebPort") 依赖此键）
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppSetting>().HasKey(e => e.Key);
    }

    public static AppDbContext Create(string dbPath) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={dbPath}").Options);
}
