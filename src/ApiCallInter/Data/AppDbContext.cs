using Microsoft.Data.Sqlite;
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

    /// <summary>EnsureCreated 无迁移机制：老库缺 SortOrder 列时启动补列（默认 0 → 查询退化按名称排，与升级前顺序一致）。</summary>
    public static void EnsureSortOrderColumn(string dbPath)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var check = conn.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Projects') WHERE name = 'SortOrder'";
        var exists = Convert.ToInt64(check.ExecuteScalar()) > 0;
        if (exists) return;
        using var alter = conn.CreateCommand();
        alter.CommandText = "ALTER TABLE Projects ADD COLUMN SortOrder INTEGER NOT NULL DEFAULT 0";
        alter.ExecuteNonQuery();
    }
}
