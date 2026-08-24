using ApiCallInter.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ApiCallInter.Tests;

public static class TestDb
{
    public static AppDbContext Create()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(conn).Options);
        db.Database.EnsureCreated();
        return db;
    }
}
